import { test, beforeEach } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { hkdfSync } from "node:crypto";

const require = createRequire(import.meta.url);

// A stand-in for the browser pieces webauthn.js touches.
let creates = [];
let gets = [];
let createResult;
let getResult;
let storageTouches = [];

function installBrowser() {
    creates = [];
    gets = [];
    storageTouches = [];
    globalThis.PublicKeyCredential = function () {};
    globalThis.PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable = async () => true;
    Object.defineProperty(globalThis, "navigator", {
        configurable: true,
        value: {
            credentials: {
                create: async (opts) => { creates.push(opts); return createResult(opts); },
                get: async (opts) => { gets.push(opts); return getResult(opts); },
            },
        },
    });
    Object.defineProperty(globalThis, "location", { configurable: true, value: { hostname: "wallet.test" } });
    // Anything the glue writes to browser storage would be a persisted key: record it.
    for (const name of ["localStorage", "sessionStorage"]) {
        Object.defineProperty(globalThis, name, {
            configurable: true,
            value: { setItem: (...a) => storageTouches.push([name, ...a]), getItem: () => null, removeItem: () => {} },
        });
    }
    globalThis.indexedDB = { open: (...a) => { storageTouches.push(["indexedDB", ...a]); throw new Error("must not be used"); } };
}

function loadWebAuthn() {
    delete require.cache[require.resolve("../../wwwroot/js/webauthn.js")];
    return require("../../wwwroot/js/webauthn.js");
}

const rawId = new Uint8Array([1, 2, 3, 4, 5]).buffer;

beforeEach(installBrowser);

test("enrollPrf: PRF enabled returns the credential id and prfSupported true, and no key material", async () => {
    createResult = () => ({ rawId, getClientExtensionResults: () => ({ prf: { enabled: true } }) });
    const wa = loadWebAuthn();

    const r = await wa.enrollPrf("My Wallet");

    assert.equal(r.prfSupported, true);
    assert.equal(r.credentialId, Buffer.from(rawId).toString("base64"));
    assert.deepEqual(Object.keys(r).sort(), ["credentialId", "prfSupported"]); // nothing to store but the id
    assert.deepEqual(creates[0].publicKey.extensions, { prf: {} });
    assert.equal(creates[0].publicKey.authenticatorSelection.userVerification, "required");
});

test("enrollPrf: PRF missing yields prfSupported false", async () => {
    createResult = () => ({ rawId, getClientExtensionResults: () => ({}) });
    const wa = loadWebAuthn();

    assert.equal((await wa.enrollPrf("My Wallet")).prfSupported, false);

    createResult = () => ({ rawId, getClientExtensionResults: () => ({ prf: { enabled: false } }) });
    assert.equal((await loadWebAuthn().enrollPrf("My Wallet")).prfSupported, false);
});

test("enrollPrf: a cancelled prompt returns null", async () => {
    createResult = () => { throw new Error("NotAllowedError"); };
    assert.equal(await loadWebAuthn().enrollPrf("My Wallet"), null);
});

test("deriveKeyPrf: the key is HKDF-SHA256 of the PRF output and is returned, not persisted", async () => {
    const prfOut = new Uint8Array(32).map((_, i) => i * 7 + 1);
    getResult = () => ({ getClientExtensionResults: () => ({ prf: { results: { first: prfOut.buffer } } }) });
    const wa = loadWebAuthn();

    const keyB64 = await wa.deriveKeyPrf(Buffer.from(rawId).toString("base64"));

    const expected = Buffer.from(hkdfSync("sha256", prfOut, new Uint8Array(0), "digibyte-wallet/biometric-key/v1", 32));
    assert.equal(keyB64, expected.toString("base64"));
    assert.deepEqual(storageTouches, []); // never written to any browser storage
    const ext = gets[0].publicKey.extensions.prf.eval.first;
    assert.ok(ext instanceof Uint8Array && ext.length > 0); // the fixed per-app PRF input
    assert.equal(gets[0].publicKey.userVerification, "required");
});

test("deriveKeyPrf: the same PRF output gives the same key and a different one gives a different key", async () => {
    const wa = loadWebAuthn();
    const make = (b) => () => ({ getClientExtensionResults: () => ({ prf: { results: { first: new Uint8Array(32).fill(b).buffer } } }) });

    getResult = make(1); const a1 = await wa.deriveKeyPrf("AQID");
    getResult = make(1); const a2 = await wa.deriveKeyPrf("AQID");
    getResult = make(2); const b = await wa.deriveKeyPrf("AQID");

    assert.equal(a1, a2);
    assert.notEqual(a1, b);
});

test("deriveKeyPrf: no PRF result means no key", async () => {
    const wa = loadWebAuthn();

    getResult = () => ({ getClientExtensionResults: () => ({}) });
    assert.equal(await wa.deriveKeyPrf("AQID"), null);

    getResult = () => ({ getClientExtensionResults: () => ({ prf: {} }) });
    assert.equal(await wa.deriveKeyPrf("AQID"), null);

    getResult = () => { throw new Error("NotAllowedError"); };
    assert.equal(await wa.deriveKeyPrf("AQID"), null);
});

test("seed copies sealed with a derived key open only with that key", async () => {
    const wa = loadWebAuthn();
    const prf = (b) => () => ({ getClientExtensionResults: () => ({ prf: { results: { first: new Uint8Array(32).fill(b).buffer } } }) });
    getResult = prf(5);
    const key = await wa.deriveKeyPrf("AQID");
    const seed = Buffer.from("seed words").toString("base64");

    const sealed = await wa.encryptSeed(key, seed);

    assert.equal(await wa.decryptSeed(key, sealed), seed);
    getResult = prf(6);
    assert.equal(await wa.decryptSeed(await wa.deriveKeyPrf("AQID"), sealed), null);
});

test("newBioKey returns 32 random bytes for the weak path", () => {
    const wa = loadWebAuthn();
    const a = Buffer.from(wa.newBioKey(), "base64");
    const b = Buffer.from(wa.newBioKey(), "base64");
    assert.equal(a.length, 32);
    assert.notDeepEqual(a, b);
});
