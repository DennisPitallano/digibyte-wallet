import { test } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const argon = require("../../wwwroot/js/vendor/hash-wasm-argon2.umd.min.js");
const crypto = require("../../wwwroot/js/crypto.js");

// Real Argon2id from the vendored build, standing in for the worker (which cannot run under Node).
async function realDeriver(req) {
    return argon.argon2id({
        password: req.password,
        salt: req.salt,
        parallelism: req.lanes,
        iterations: req.passes,
        memorySize: req.memoryKiB,
        hashLength: 32,
        outputType: "binary",
    });
}

const b64 = (s) => Buffer.from(s, "utf8").toString("base64");
const unb64 = (s) => Buffer.from(s, "base64").toString("utf8");
const SECRET = b64("abandon ability able about above absent absorb abstract");

test.beforeEach(() => crypto._setDeriver(realDeriver));

test("Argon2id known answer: vendored build output is pinned", async () => {
    const hex = await argon.argon2id({
        password: "pw",
        salt: new Uint8Array(16).fill(1),
        parallelism: 1,
        iterations: 3,
        memorySize: 65536,
        hashLength: 32,
        outputType: "hex",
    });
    assert.equal(hex, "6cc8330b81cc4258e0806d00d18523028996d710cd2bebfc703ab616a18ea0f6");
});

test("Argon2id output depends on every cost parameter", async () => {
    const base = { password: "pw", salt: new Uint8Array(16).fill(1), hashLength: 32, outputType: "hex" };
    const a = await argon.argon2id({ ...base, parallelism: 1, iterations: 2, memorySize: 19456 });
    const b = await argon.argon2id({ ...base, parallelism: 1, iterations: 3, memorySize: 19456 });
    const c = await argon.argon2id({ ...base, parallelism: 1, iterations: 2, memorySize: 32768 });
    assert.equal(new Set([a, b, c]).size, 3);
});

for (const profile of ["standard", "low-memory", "floor"]) {
    test(`encryptV2 then decryptV2 round trips (${profile}, pin)`, async () => {
        const enc = await crypto.encryptV2(SECRET, "123456", "pin", profile);
        assert.equal(enc.ok, true);
        assert.match(enc.envelope, /^dgb2\$/);
        assert.deepEqual(enc.params, crypto._profiles[profile]);

        const dec = await crypto.decryptV2(enc.envelope, "123456");
        assert.equal(dec.ok, true);
        assert.equal(dec.format, "dgb2");
        assert.equal(unb64(dec.plaintextBase64), unb64(SECRET));
    });
}

test("passphrase round trips and is normalised (composed and decomposed forms match)", async () => {
    const composed = "café passphrase long enough";
    const decomposed = "café passphrase long enough";
    const enc = await crypto.encryptV2(SECRET, composed, "passphrase", "floor");
    const dec = await crypto.decryptV2(enc.envelope, decomposed);
    assert.equal(dec.ok, true);
});

test("wrong credential returns WrongCredential", async () => {
    const enc = await crypto.encryptV2(SECRET, "123456", "pin", "floor");
    const dec = await crypto.decryptV2(enc.envelope, "654321");
    assert.deepEqual({ ok: dec.ok, reason: dec.reason }, { ok: false, reason: "WrongCredential" });
});

function tamper(envelope, mutate) {
    const raw = Buffer.from(envelope.slice(5).replace(/-/g, "+").replace(/_/g, "/"), "base64");
    mutate(raw);
    return "dgb2$" + raw.toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

test("flipped header bit fails authentication (parameter downgrade is detected)", async () => {
    // Raise passes in the header from 2 to 3 (still above the floor): the key changes, so it must fail.
    const enc = await crypto.encryptV2(SECRET, "123456", "pin", "floor");
    const changed = tamper(enc.envelope, (raw) => (raw[6] = 3));
    const dec = await crypto.decryptV2(changed, "123456");
    assert.equal(dec.ok, false);
    assert.equal(dec.reason, "WrongCredential");

    // Flip the credential-kind byte only: key is derived from the same bytes for a numeric PIN, so the
    // header AAD is what catches this.
    const kind = tamper(enc.envelope, (raw) => (raw[8] = 2));
    const dec2 = await crypto.decryptV2(kind, "123456");
    assert.equal(dec2.ok, false);
    assert.equal(dec2.reason, "WrongCredential");
});

test("flipped ciphertext bit fails authentication", async () => {
    const enc = await crypto.encryptV2(SECRET, "123456", "pin", "floor");
    const changed = tamper(enc.envelope, (raw) => (raw[raw.length - 1] ^= 1));
    const dec = await crypto.decryptV2(changed, "123456");
    assert.equal(dec.reason, "WrongCredential");
});

test("truncated, unknown-version and below-floor values are refused untouched", async () => {
    const enc = await crypto.encryptV2(SECRET, "123456", "pin", "floor");
    assert.equal((await crypto.decryptV2(enc.envelope.slice(0, 30), "123456")).reason, "Unreadable");
    assert.equal((await crypto.decryptV2("dgb3$AAAA", "123456")).reason, "UnsupportedVersion");
    const below = tamper(enc.envelope, (raw) => {
        raw[2] = 0; raw[3] = 0; raw[4] = 0x4b; raw[5] = 0xff; // 19,455 KiB
    });
    assert.equal((await crypto.decryptV2(below, "123456")).reason, "Unreadable");
    const onePass = tamper(enc.envelope, (raw) => (raw[6] = 1));
    assert.equal((await crypto.decryptV2(onePass, "123456")).reason, "Unreadable");
});

test("legacy PBKDF2 value decrypts via decryptV2, and old decrypt() still works for both formats", async () => {
    const legacy = await crypto.encrypt(SECRET, "123456");
    assert.ok(!legacy.includes("$"));

    const dec = await crypto.decryptV2(legacy, "123456");
    assert.equal(dec.ok, true);
    assert.equal(dec.format, "legacy");
    assert.equal(unb64(dec.plaintextBase64), unb64(SECRET));

    assert.equal((await crypto.decryptV2(legacy, "000000")).reason, "WrongCredential");
    assert.equal(await crypto.decrypt(legacy, "123456"), SECRET);

    const v2 = await crypto.encryptV2(SECRET, "123456", "pin", "floor");
    assert.equal(await crypto.decrypt(v2.envelope, "123456"), SECRET);
    assert.equal(await crypto.decrypt(v2.envelope, "nope"), null);
});

test("ResourceExhausted from every profile is reported, and nothing is produced below the floor", async () => {
    const seen = [];
    crypto._setDeriver(async (req) => {
        seen.push(req.memoryKiB);
        throw { reason: "ResourceExhausted" };
    });

    const enc = await crypto.encryptV2(SECRET, "123456", "pin");
    assert.deepEqual({ ok: enc.ok, reason: enc.reason }, { ok: false, reason: "ResourceExhausted" });
    assert.deepEqual(seen, [65536, 32768, 19456]); // falls back to the floor and never below it
    assert.equal(enc.envelope, undefined);
});

test("automatic fallback uses the next smaller profile when the standard one cannot allocate", async () => {
    crypto._setDeriver(async (req) => {
        if (req.memoryKiB > 32768) throw { reason: "ResourceExhausted" };
        return realDeriver(req);
    });
    const enc = await crypto.encryptV2(SECRET, "123456", "pin");
    assert.equal(enc.ok, true);
    assert.equal(enc.params.memoryKiB, 32768);
});

test("decrypt reports ResourceExhausted (not WrongCredential) when the worker cannot allocate", async () => {
    const enc = await crypto.encryptV2(SECRET, "123456", "pin", "floor");
    crypto._setDeriver(async () => {
        throw { reason: "ResourceExhausted" };
    });
    const dec = await crypto.decryptV2(enc.envelope, "123456");
    assert.equal(dec.reason, "ResourceExhausted");
});

test("normalisePassphrase applies NFKC", () => {
    assert.equal(crypto.normalisePassphrase("café"), "café");
    assert.equal(crypto.normalisePassphrase("ＡＢＣ"), "ABC");
});

test("capabilities reports argon2 availability", () => {
    assert.equal(crypto.capabilities().argon2, true); // a deriver is installed
});

test("a browser with no Argon2 support is reported as Unsupported, for both encrypt and decrypt", async () => {
    const enc = await crypto.encryptV2(SECRET, "123456", "pin", "floor"); // made while a deriver exists
    crypto._setDeriver(null); // no deriver and no Worker under Node: this is a browser that cannot run Argon2id

    assert.equal(crypto.capabilities().argon2, false);
    assert.deepEqual(await crypto.encryptV2(SECRET, "123456", "pin"), { ok: false, reason: "Unsupported" });
    const dec = await crypto.decryptV2(enc.envelope, "123456");
    assert.deepEqual({ ok: dec.ok, reason: dec.reason }, { ok: false, reason: "Unsupported" });
});

test("a legacy value still opens on a browser with no Argon2 support", async () => {
    const legacy = await crypto.encrypt(SECRET, "123456");
    crypto._setDeriver(null);

    const dec = await crypto.decryptV2(legacy, "123456");

    assert.equal(dec.ok, true);
    assert.equal(dec.format, "legacy");
});

function setLocation(hostname, search = "") {
    Object.defineProperty(globalThis, "location", { configurable: true, value: { hostname, search } });
}

test("bench reports derive time per profile and the legacy cost, on a developer host", async () => {
    setLocation("localhost");
    const rows = await crypto.bench({ runs: 1 });

    assert.deepEqual(rows.map((r) => r.profile), ["standard", "low-memory", "floor", "legacy-pbkdf2"]);
    for (const r of rows) {
        assert.equal(r.ok, true);
        assert.ok(r.ms >= 0);
    }
    assert.equal(rows[0].memoryKiB, 65536);
    assert.equal(rows[2].memoryKiB, 19456);
});

test("bench is disabled outside developer hosts (release builds)", async () => {
    setLocation("dgbwallet.app");
    await assert.rejects(() => crypto.bench({ runs: 1 }), /disabled/i);
});

test("bench can be switched on explicitly with the developer flag", async () => {
    setLocation("staging.example.test");
    globalThis.localStorage = { getItem: (k) => (k === "dgbBench" ? "1" : null) };
    const rows = await crypto.bench({ runs: 1 });
    assert.equal(rows.length, 4);
    delete globalThis.localStorage;
});

// ---- known-answer fixtures (T004), shared with the C# suite ----
import { readFileSync } from "node:fs";
const fx = JSON.parse(readFileSync(new URL("./fixtures/envelope-fixtures.json", import.meta.url), "utf8"));

test("fixture: legacy value opens with the known PIN", async () => {
    const r = await crypto.decryptV2(fx.legacy, fx.pin);
    assert.equal(r.ok, true);
    assert.equal(r.format, "legacy");
    assert.equal(unb64(r.plaintextBase64), fx.plaintext);
});

for (const profile of ["standard", "low-memory", "floor"]) {
    test(`fixture: dgb2 ${profile} opens with the known PIN and not with a wrong one`, async () => {
        const ok = await crypto.decryptV2(fx.valid[profile], fx.pin);
        assert.equal(ok.ok, true);
        assert.deepEqual(ok.params, crypto._profiles[profile]);
        assert.equal(unb64(ok.plaintextBase64), fx.plaintext);
        assert.equal((await crypto.decryptV2(fx.valid[profile], "000000")).reason, "WrongCredential");
    });
}

test("fixture: passphrase envelope opens with the known passphrase", async () => {
    const r = await crypto.decryptV2(fx.passphraseFloor, fx.passphrase);
    assert.equal(r.ok, true);
    assert.equal(unb64(r.plaintextBase64), fx.plaintext);
});

test("fixture: tampered variants are refused with the right failure class", async () => {
    assert.equal((await crypto.decryptV2(fx.tampered.headerBitFlip, fx.pin)).reason, "WrongCredential");
    assert.equal((await crypto.decryptV2(fx.tampered.ciphertextBitFlip, fx.pin)).reason, "WrongCredential");
    assert.equal((await crypto.decryptV2(fx.tampered.truncated, fx.pin)).reason, "Unreadable");
    assert.equal((await crypto.decryptV2(fx.tampered.unknownVersion, fx.pin)).reason, "UnsupportedVersion");
    assert.equal((await crypto.decryptV2(fx.tampered.belowFloorMemory, fx.pin)).reason, "Unreadable");
    assert.equal((await crypto.decryptV2(fx.tampered.belowFloorPasses, fx.pin)).reason, "Unreadable");
});

test("bench report lists every row with its time and device facts", () => {
    const text = crypto._formatBench(
        [
            { profile: "standard", memoryKiB: 65536, passes: 3, ms: 812, ok: true },
            { profile: "floor", memoryKiB: 19456, passes: 2, ms: 0, ok: false, reason: "ResourceExhausted" },
        ],
        { userAgent: "TestUA", cores: 8, memoryGb: 4 }
    );
    assert.match(text, /ua: TestUA/);
    assert.match(text, /standard\s+65536\s+3\s+812\s+ok/);
    assert.match(text, /floor\s+19456\s+2\s+0\s+FAIL ResourceExhausted/);
});
