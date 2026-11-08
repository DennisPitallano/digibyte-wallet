import { test } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";

const require = createRequire(import.meta.url);
const argon = require("../../wwwroot/js/vendor/hash-wasm-argon2.umd.min.js");
const crypto = require("../../wwwroot/js/crypto.js");

// Same vectors the C# CredentialPolicy tests read (kept in both fixtures folders).
const vectors = JSON.parse(readFileSync(new URL("./fixtures/normalisation-vectors.json", import.meta.url), "utf8"));

test("JS normalisation matches the shared NFKC vectors (and therefore the C# policy)", () => {
    for (const v of vectors) assert.equal(crypto.normalisePassphrase(v.input), v.nfkc);
});

test("leading and trailing spaces are preserved", () => {
    assert.equal(crypto.normalisePassphrase("  spaced  "), "  spaced  ");
});

test("same visible text from different Unicode forms derives the same key", async () => {
    crypto._setDeriver(async (req) =>
        argon.argon2id({
            password: req.password,
            salt: req.salt,
            parallelism: req.lanes,
            iterations: req.passes,
            memorySize: req.memoryKiB,
            hashLength: 32,
            outputType: "hex",
        }).then((hex) => Uint8Array.from(Buffer.from(hex, "hex")))
    );

    const secret = Buffer.from("seed words").toString("base64");
    const composed = "café passphrase long enough";
    const decomposed = "café passphrase long enough";
    const fullWidthMix = "ｃafé passphrase long enough"; // compatibility form of "c"

    const enc = await crypto.encryptV2(secret, composed, "passphrase", "floor");
    assert.equal(enc.ok, true);
    for (const form of [composed, decomposed, fullWidthMix]) {
        const dec = await crypto.decryptV2(enc.envelope, form);
        assert.equal(dec.ok, true, "form should open the same envelope");
    }
    const other = await crypto.decryptV2(enc.envelope, "cafe passphrase long enough");
    assert.equal(other.ok, false);
});

test("a PIN is passed through unchanged (no normalisation)", async () => {
    const seen = [];
    crypto._setDeriver(async (req) => {
        seen.push(Buffer.from(req.password).toString("utf8"));
        return new Uint8Array(32).fill(7);
    });
    await crypto.encryptV2(Buffer.from("x").toString("base64"), "123456", "pin", "floor");
    assert.deepEqual(seen, ["123456"]);
});
