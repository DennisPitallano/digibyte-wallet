// Wallet encryption via SubtleCrypto (AES-256-GCM) with two key-derivation schemes.
//
// Legacy format  : base64( salt(16) | iv(12) | ciphertext+tag ), key = PBKDF2-SHA256, 100,000 iterations.
// "dgb2" format  : "dgb2$" + base64url( header(9) | salt(16) | nonce(12) | ciphertext+tag ),
//                  key = Argon2id (memory-hard, runs in crypto-worker.js), header is the GCM
//                  additional authenticated data so changing the stored parameters fails authentication.
// See specs/009-stronger-pin-protection/contracts/envelope-format.md and js-crypto-api.md.
//
// This file works in the browser (window.dgbCrypto) and under Node's test runner (module.exports).

(function (root) {
    "use strict";

    const SALT_SIZE = 16;
    const IV_SIZE = 12;
    const KEY_SIZE = 256;
    const LEGACY_ITERATIONS = 100000;

    const PREFIX = "dgb2$";
    const VERSION = 0x02;
    const KDF_ARGON2ID = 0x01;
    const HEADER_LENGTH = 9;
    const MIN_LENGTH = HEADER_LENGTH + SALT_SIZE + IV_SIZE + 16;

    // Provisional until the T001 benchmark freezes them (research R3). Must match KdfProfile.cs.
    const PROFILES = {
        standard: { memoryKiB: 65536, passes: 3, lanes: 1 },
        "low-memory": { memoryKiB: 32768, passes: 3, lanes: 1 },
        floor: { memoryKiB: 19456, passes: 2, lanes: 1 },
    };
    const FALLBACK_ORDER = ["standard", "low-memory", "floor"];
    const FLOOR = PROFILES.floor;

    const KIND_BYTE = { pin: 1, passphrase: 2 };

    const textEncoder = new TextEncoder();

    // ---- encoding helpers (chunk-safe, no spread of large arrays) ----

    function bytesToBase64(bytes) {
        let s = "";
        const chunk = 0x8000;
        for (let i = 0; i < bytes.length; i += chunk) {
            s += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        }
        return btoa(s);
    }

    function base64ToBytes(b64) {
        const bin = atob(b64);
        const out = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
        return out;
    }

    function toBase64Url(bytes) {
        return bytesToBase64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }

    function fromBase64Url(text) {
        let t = text.replace(/-/g, "+").replace(/_/g, "/");
        while (t.length % 4) t += "=";
        return base64ToBytes(t);
    }

    function atFloor(p) {
        return p.memoryKiB >= FLOOR.memoryKiB && p.passes >= FLOOR.passes && p.lanes === 1;
    }

    // NFKC so the same visible text gives the same key on every device (FR-006).
    function normalisePassphrase(text) {
        return String(text).normalize("NFKC");
    }

    function credentialBytes(credential, kind) {
        return textEncoder.encode(kind === "passphrase" ? normalisePassphrase(credential) : String(credential));
    }

    // ---- Argon2id deriver (worker by default, replaceable for tests) ----

    function workerDeriver(req) {
        return new Promise((resolve, reject) => {
            let worker;
            try {
                worker = new Worker(new URL("js/crypto-worker.js", document.baseURI));
            } catch (e) {
                reject({ reason: "ResourceExhausted" });
                return;
            }
            const id = Math.random().toString(36).slice(2);
            const done = (fn, value) => {
                worker.terminate(); // releases the Argon2 memory immediately
                fn(value);
            };
            worker.onmessage = (event) => {
                const msg = event.data;
                if (!msg || msg.id !== id) return;
                if (msg.ok) done(resolve, msg.key);
                else done(reject, { reason: msg.reason || "ResourceExhausted" });
            };
            worker.onerror = () => done(reject, { reason: "ResourceExhausted" });
            worker.postMessage({
                id,
                op: "derive",
                password: req.password,
                salt: req.salt,
                memoryKiB: req.memoryKiB,
                passes: req.passes,
                lanes: req.lanes,
            });
        });
    }

    let deriver = null; // null means "use the worker"

    function hasArgon2() {
        if (deriver) return true;
        return typeof WebAssembly === "object" && typeof Worker !== "undefined";
    }

    async function deriveArgon2(credential, kind, salt, params) {
        const req = {
            password: credentialBytes(credential, kind),
            salt,
            memoryKiB: params.memoryKiB,
            passes: params.passes,
            lanes: params.lanes,
        };
        const fn = deriver || workerDeriver;
        try {
            return await fn(req);
        } catch (e) {
            const reason = e && e.reason ? e.reason : "ResourceExhausted";
            throw { reason };
        }
    }

    async function aesKeyFromBytes(raw) {
        return crypto.subtle.importKey("raw", raw, { name: "AES-GCM", length: KEY_SIZE }, false, ["encrypt", "decrypt"]);
    }

    // ---- legacy PBKDF2 path ----

    async function deriveLegacyKey(pin, salt) {
        const keyMaterial = await crypto.subtle.importKey("raw", textEncoder.encode(pin), "PBKDF2", false, ["deriveKey"]);
        return crypto.subtle.deriveKey(
            { name: "PBKDF2", salt: salt, iterations: LEGACY_ITERATIONS, hash: "SHA-256" },
            keyMaterial,
            { name: "AES-GCM", length: KEY_SIZE },
            false,
            ["encrypt", "decrypt"]
        );
    }

    async function encryptLegacy(plaintextBase64, pin) {
        const plaintext = base64ToBytes(plaintextBase64);
        const salt = crypto.getRandomValues(new Uint8Array(SALT_SIZE));
        const iv = crypto.getRandomValues(new Uint8Array(IV_SIZE));
        const key = await deriveLegacyKey(pin, salt);
        const encrypted = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext));
        const result = new Uint8Array(SALT_SIZE + IV_SIZE + encrypted.length);
        result.set(salt, 0);
        result.set(iv, SALT_SIZE);
        result.set(encrypted, SALT_SIZE + IV_SIZE);
        return bytesToBase64(result);
    }

    async function decryptLegacy(packedBase64, pin) {
        let packed;
        try {
            packed = base64ToBytes(packedBase64);
        } catch (e) {
            return { ok: false, reason: "Unreadable" };
        }
        if (packed.length < SALT_SIZE + IV_SIZE + 17) return { ok: false, reason: "Unreadable" };

        const salt = packed.slice(0, SALT_SIZE);
        const iv = packed.slice(SALT_SIZE, SALT_SIZE + IV_SIZE);
        const ciphertext = packed.slice(SALT_SIZE + IV_SIZE);
        const key = await deriveLegacyKey(pin, salt);
        try {
            const decrypted = await crypto.subtle.decrypt({ name: "AES-GCM", iv }, key, ciphertext);
            return { ok: true, plaintextBase64: bytesToBase64(new Uint8Array(decrypted)), format: "legacy", params: null };
        } catch (e) {
            return { ok: false, reason: "WrongCredential" };
        }
    }

    // ---- dgb2 path ----

    function buildHeader(params, kind) {
        const h = new Uint8Array(HEADER_LENGTH);
        h[0] = VERSION;
        h[1] = KDF_ARGON2ID;
        h[2] = (params.memoryKiB >>> 24) & 0xff;
        h[3] = (params.memoryKiB >>> 16) & 0xff;
        h[4] = (params.memoryKiB >>> 8) & 0xff;
        h[5] = params.memoryKiB & 0xff;
        h[6] = params.passes;
        h[7] = params.lanes;
        h[8] = KIND_BYTE[kind];
        return h;
    }

    function parseEnvelope(value) {
        const m = /^dgb(\d+)\$/.exec(value);
        if (!m) return { status: "legacy" };
        if (Number(m[1]) > VERSION) return { status: "UnsupportedVersion" };
        if (Number(m[1]) !== VERSION) return { status: "Unreadable" };

        let raw;
        try {
            raw = fromBase64Url(value.slice(m[0].length));
        } catch (e) {
            return { status: "Unreadable" };
        }
        if (raw.length < MIN_LENGTH || raw[0] !== VERSION || raw[1] !== KDF_ARGON2ID) return { status: "Unreadable" };

        const params = {
            memoryKiB: ((raw[2] << 24) | (raw[3] << 16) | (raw[4] << 8) | raw[5]) >>> 0,
            passes: raw[6],
            lanes: raw[7],
        };
        if (!atFloor(params)) return { status: "Unreadable" };
        if (raw[8] !== 1 && raw[8] !== 2) return { status: "Unreadable" };

        return {
            status: "ok",
            params,
            kind: raw[8] === 2 ? "passphrase" : "pin",
            header: raw.slice(0, HEADER_LENGTH),
            salt: raw.slice(HEADER_LENGTH, HEADER_LENGTH + SALT_SIZE),
            nonce: raw.slice(HEADER_LENGTH + SALT_SIZE, HEADER_LENGTH + SALT_SIZE + IV_SIZE),
            ciphertext: raw.slice(HEADER_LENGTH + SALT_SIZE + IV_SIZE),
        };
    }

    async function encryptV2(plaintextBase64, credential, kind, profile) {
        if (kind !== "pin" && kind !== "passphrase") return { ok: false, reason: "Unsupported" };
        if (!hasArgon2()) return { ok: false, reason: "Unsupported" };

        const order = profile ? [profile] : FALLBACK_ORDER;
        const plaintext = base64ToBytes(plaintextBase64);

        for (const name of order) {
            const params = PROFILES[name];
            if (!params || !atFloor(params)) return { ok: false, reason: "Unsupported" }; // never below the floor

            const salt = crypto.getRandomValues(new Uint8Array(SALT_SIZE));
            const nonce = crypto.getRandomValues(new Uint8Array(IV_SIZE));

            let keyBytes;
            try {
                keyBytes = await deriveArgon2(credential, kind, salt, params);
            } catch (e) {
                if (e.reason === "ResourceExhausted") continue; // try the next smaller profile
                return { ok: false, reason: e.reason };
            }

            const header = buildHeader(params, kind);
            const key = await aesKeyFromBytes(keyBytes);
            const sealed = new Uint8Array(
                await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce, additionalData: header }, key, plaintext)
            );
            const raw = new Uint8Array(HEADER_LENGTH + SALT_SIZE + IV_SIZE + sealed.length);
            raw.set(header, 0);
            raw.set(salt, HEADER_LENGTH);
            raw.set(nonce, HEADER_LENGTH + SALT_SIZE);
            raw.set(sealed, HEADER_LENGTH + SALT_SIZE + IV_SIZE);
            return { ok: true, envelope: PREFIX + toBase64Url(raw), params, kind };
        }
        return { ok: false, reason: "ResourceExhausted" };
    }

    async function decryptV2(value, credential) {
        if (typeof value !== "string" || value.length === 0) return { ok: false, reason: "Unreadable" };

        const parsed = parseEnvelope(value);
        if (parsed.status === "legacy") return decryptLegacy(value, credential);
        if (parsed.status !== "ok") return { ok: false, reason: parsed.status };
        if (!hasArgon2()) return { ok: false, reason: "Unsupported" }; // this browser cannot run Argon2id at all

        let keyBytes;
        try {
            keyBytes = await deriveArgon2(credential, parsed.kind, parsed.salt, parsed.params);
        } catch (e) {
            return { ok: false, reason: e.reason };
        }

        const key = await aesKeyFromBytes(keyBytes);
        try {
            const plain = await crypto.subtle.decrypt(
                { name: "AES-GCM", iv: parsed.nonce, additionalData: parsed.header },
                key,
                parsed.ciphertext
            );
            return { ok: true, plaintextBase64: bytesToBase64(new Uint8Array(plain)), format: "dgb2", params: parsed.params };
        } catch (e) {
            return { ok: false, reason: "WrongCredential" };
        }
    }

    // ---- developer benchmark (T001, quickstart) ----
    // Prints how long each Argon2id profile takes on this device, next to the legacy PBKDF2 cost, so the profile values can
    // be frozen against real phones. Available only on developer hosts (localhost) or with localStorage.dgbBench = "1";
    // in a release build it is off. Open the app with ?crypto-bench on a developer host to run it automatically.

    function benchEnabled() {
        try {
            const host = typeof location !== "undefined" ? location.hostname : "";
            if (host === "localhost" || host === "127.0.0.1" || host === "[::1]" || host === "::1") return true;
            return typeof localStorage !== "undefined" && localStorage.getItem("dgbBench") === "1";
        } catch (e) {
            return false;
        }
    }

    function median(values) {
        const sorted = values.slice().sort((a, b) => a - b);
        return sorted[Math.floor(sorted.length / 2)];
    }

    async function bench(options) {
        if (!benchEnabled()) throw new Error("The crypto benchmark is disabled in release builds.");
        const runs = (options && options.runs) || 3;
        const secret = bytesToBase64(textEncoder.encode("benchmark secret"));
        const now = () => (typeof performance !== "undefined" ? performance.now() : Date.now());
        const heapMb = () =>
            typeof performance !== "undefined" && performance.memory
                ? Math.round(performance.memory.usedJSHeapSize / 1048576)
                : null;

        const rows = [];
        for (const name of FALLBACK_ORDER) {
            const times = [];
            let ok = true;
            let reason = null;
            for (let i = 0; i < runs; i++) {
                const t0 = now();
                const res = await encryptV2(secret, "123456", "pin", name);
                times.push(now() - t0);
                if (!res.ok) {
                    ok = false;
                    reason = res.reason;
                    break;
                }
            }
            rows.push({
                profile: name,
                memoryKiB: PROFILES[name].memoryKiB,
                passes: PROFILES[name].passes,
                ms: Math.round(median(times)),
                ok,
                reason,
                jsHeapMb: heapMb(), // JS heap only; the WASM memory of the worker is not visible here
            });
        }

        const legacyTimes = [];
        for (let i = 0; i < runs; i++) {
            const t0 = now();
            await encryptLegacy(secret, "123456");
            legacyTimes.push(now() - t0);
        }
        rows.push({ profile: "legacy-pbkdf2", memoryKiB: 0, passes: LEGACY_ITERATIONS, ms: Math.round(median(legacyTimes)), ok: true, reason: null, jsHeapMb: heapMb() });

        if (typeof console !== "undefined" && console.table) console.table(rows);
        return rows;
    }

    function capabilities() {
        const worker = typeof Worker !== "undefined";
        return {
            argon2: hasArgon2(),
            worker,
            webLocks: typeof navigator !== "undefined" && !!navigator.locks,
            prf: "unknown",
        };
    }

    // ---- cross-tab lock (Web Locks) used while a wallet is migrated ----

    const heldLocks = new Map();
    const LOCK_WAIT_MS = 10000;

    // Resolves to a handle id once the lock is held, "unsupported" without Web Locks, or null on timeout.
    function acquireLock(name) {
        if (typeof navigator === "undefined" || !navigator.locks) return Promise.resolve("unsupported");
        return new Promise((resolve) => {
            const controller = new AbortController();
            const timer = setTimeout(() => controller.abort(), LOCK_WAIT_MS);
            navigator.locks
                .request("dgb-migration-" + name, { mode: "exclusive", signal: controller.signal }, () => {
                    clearTimeout(timer);
                    return new Promise((release) => {
                        const id = Math.random().toString(36).slice(2);
                        heldLocks.set(id, release);
                        resolve(id);
                    });
                })
                .catch(() => {
                    clearTimeout(timer);
                    resolve(null);
                });
        });
    }

    function releaseLock(id) {
        const release = heldLocks.get(id);
        if (release) {
            heldLocks.delete(id);
            release();
        }
    }

    const api = {
        // New typed surface (contracts/js-crypto-api.md)
        encryptV2,
        decryptV2,
        capabilities,
        bench,
        acquireLock,
        releaseLock,
        normalisePassphrase,

        // Existing surface. `encrypt` stays on the legacy format until the legacy write path is
        // scoped down in T052; it is what the unsupported-device flow (FR-021) uses. `decrypt`
        // reads both formats and returns null for any failure, as before.
        encrypt: encryptLegacy,
        decrypt: async function (packed, credential) {
            const r = await decryptV2(packed, credential);
            return r.ok ? r.plaintextBase64 : null;
        },

        // Test hook: replace the Argon2id deriver (the worker cannot run under Node).
        _setDeriver: function (fn) {
            deriver = fn;
        },
        _profiles: PROFILES,
    };

    root.dgbCrypto = api;

    // ?crypto-bench on a developer host runs the benchmark once the page has loaded.
    if (typeof location !== "undefined" && typeof document !== "undefined" && /[?&]crypto-bench\b/.test(location.search || "") && benchEnabled()) {
        window.addEventListener("load", () => setTimeout(() => bench().catch(() => {}), 1500));
    }
    if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
