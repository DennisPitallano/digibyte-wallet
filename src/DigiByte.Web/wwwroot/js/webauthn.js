// WebAuthn biometric unlock for DigiByte Wallet
// Single credential for all wallets. Each wallet's seed copy is encrypted with an AES-256 key.
//
// Where the browser supports the WebAuthn PRF extension, that key is DERIVED from the authenticator at every unlock
// (enrollPrf / deriveKeyPrf) and never stored, so copying the browser's data is not enough to open the seed copies.
// Where it does not, the old behaviour is kept behind a warning and the user's consent: a random key is stored next
// to the ciphertext (newBioKey / authenticate), so the biometric prompt is only a gate in the interface.
// See specs/009-stronger-pin-protection/research.md R5 and contracts/js-crypto-api.md.
//
// Works in the browser (window.dgbWebAuthn) and under Node's test runner (module.exports).

(function (root) {
    const WA_IV_SIZE = 12;

    // Fixed per-app PRF input and the HKDF label for the derived key.
    const PRF_INPUT = new TextEncoder().encode("digibyte-wallet/biometric-prf/v1");
    const HKDF_INFO = new TextEncoder().encode("digibyte-wallet/biometric-key/v1");

    function toBase64(buffer) {
        return btoa(String.fromCharCode(...new Uint8Array(buffer)));
    }

    function fromBase64(base64) {
        return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    }

    async function importAesKey(rawBytes, usages) {
        return crypto.subtle.importKey(
            "raw", rawBytes, { name: "AES-GCM" }, false, usages
        );
    }

    async function encryptWithKey(key, plaintext) {
        const iv = crypto.getRandomValues(new Uint8Array(WA_IV_SIZE));
        const encrypted = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext);
        const result = new Uint8Array(WA_IV_SIZE + encrypted.byteLength);
        result.set(iv, 0);
        result.set(new Uint8Array(encrypted), WA_IV_SIZE);
        return result;
    }

    async function decryptWithKey(key, packed) {
        if (packed.length < WA_IV_SIZE + 1) return null;
        const iv = packed.slice(0, WA_IV_SIZE);
        const ciphertext = packed.slice(WA_IV_SIZE);
        try {
            return new Uint8Array(await crypto.subtle.decrypt({ name: "AES-GCM", iv }, key, ciphertext));
        } catch {
            return null;
        }
    }

    const api = {
        // Check if platform authenticator is available.
        isSupported: async function () {
            if (!root.PublicKeyCredential) return false;
            try {
                return await root.PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
            } catch {
                return false;
            }
        },

        // Create a single WebAuthn credential for the app.
        // Returns { credentialId, bioKey } (both base64) or null.
        // bioKey is a random AES-256 key for encrypting/decrypting wallet seeds.
        enroll: async function (userName) {
            const userId = crypto.getRandomValues(new Uint8Array(16));

            let credential;
            try {
                credential = await navigator.credentials.create({
                    publicKey: {
                        rp: { name: "DigiByte Wallet", id: root.location.hostname },
                        user: { id: userId, name: userName, displayName: userName },
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        pubKeyCredParams: [
                            { alg: -7, type: "public-key" },
                            { alg: -257, type: "public-key" },
                        ],
                        authenticatorSelection: {
                            authenticatorAttachment: "platform",
                            userVerification: "required",
                            residentKey: "discouraged",
                        },
                        timeout: 60000
                    }
                });
            } catch {
                return null;
            }

            const bioKeyBytes = crypto.getRandomValues(new Uint8Array(32));

            return {
                credentialId: toBase64(credential.rawId),
                bioKey: toBase64(bioKeyBytes)
            };
        },

        // Create the credential asking for the PRF extension. Returns { credentialId, prfSupported } or null when the
        // user cancels. No key material is returned: with PRF the key is derived later, never stored.
        enrollPrf: async function (userName) {
            const userId = crypto.getRandomValues(new Uint8Array(16));
            let credential;
            try {
                credential = await navigator.credentials.create({
                    publicKey: {
                        rp: { name: "DigiByte Wallet", id: root.location.hostname },
                        user: { id: userId, name: userName, displayName: userName },
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        pubKeyCredParams: [
                            { alg: -7, type: "public-key" },
                            { alg: -257, type: "public-key" },
                        ],
                        authenticatorSelection: {
                            authenticatorAttachment: "platform",
                            userVerification: "required",
                            residentKey: "discouraged",
                        },
                        extensions: { prf: {} },
                        timeout: 60000
                    }
                });
            } catch {
                return null;
            }

            let prfEnabled = false;
            try {
                prfEnabled = credential.getClientExtensionResults()?.prf?.enabled === true;
            } catch {
                prfEnabled = false;
            }
            return { credentialId: toBase64(credential.rawId), prfSupported: prfEnabled };
        },

        // Run an assertion that evaluates the PRF and derive the AES key from it with HKDF-SHA256. Returns the key
        // (base64) for immediate use, or null if the user cancels or PRF is unavailable. Never persisted.
        deriveKeyPrf: async function (credentialIdBase64) {
            let assertion;
            try {
                assertion = await navigator.credentials.get({
                    publicKey: {
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        allowCredentials: [{ id: fromBase64(credentialIdBase64), type: "public-key", transports: ["internal"] }],
                        userVerification: "required",
                        extensions: { prf: { eval: { first: PRF_INPUT } } },
                        timeout: 60000
                    }
                });
            } catch {
                return null;
            }

            let prfOutput;
            try {
                prfOutput = assertion.getClientExtensionResults()?.prf?.results?.first;
            } catch {
                return null;
            }
            if (!prfOutput) return null;

            const material = await crypto.subtle.importKey("raw", prfOutput, "HKDF", false, ["deriveBits"]);
            const bits = await crypto.subtle.deriveBits(
                { name: "HKDF", hash: "SHA-256", salt: new Uint8Array(0), info: HKDF_INFO },
                material,
                256
            );
            return toBase64(bits);
        },

        // A random AES-256 key for the weak path (browsers without PRF), which the caller stores next to the ciphertext.
        newBioKey: function () {
            return toBase64(crypto.getRandomValues(new Uint8Array(32)));
        },

        // Encrypt a seed with the bio key. Returns base64 ciphertext.
        encryptSeed: async function (bioKeyBase64, seedBase64) {
            const key = await importAesKey(fromBase64(bioKeyBase64), ["encrypt"]);
            const encrypted = await encryptWithKey(key, fromBase64(seedBase64));
            return toBase64(encrypted);
        },

        // Decrypt a seed with the bio key. Returns base64 plaintext or null.
        decryptSeed: async function (bioKeyBase64, encryptedSeedBase64) {
            const key = await importAesKey(fromBase64(bioKeyBase64), ["decrypt"]);
            const decrypted = await decryptWithKey(key, fromBase64(encryptedSeedBase64));
            return decrypted ? toBase64(decrypted) : null;
        },

        // Verify identity with biometric assertion. Returns true/false.
        verifyIdentity: async function (credentialIdBase64) {
            const credentialId = fromBase64(credentialIdBase64);
            try {
                await navigator.credentials.get({
                    publicKey: {
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        allowCredentials: [{ id: credentialId, type: "public-key", transports: ["internal"] }],
                        userVerification: "required",
                        timeout: 60000
                    }
                });
                return true;
            } catch {
                return false;
            }
        },

        // Authenticate with biometric, then decrypt the given seed.
        // Returns decrypted seed (base64) or null.
        authenticate: async function (credentialIdBase64, bioKeyBase64, encryptedSeedBase64) {
            const credentialId = fromBase64(credentialIdBase64);

            try {
                await navigator.credentials.get({
                    publicKey: {
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        allowCredentials: [{ id: credentialId, type: "public-key", transports: ["internal"] }],
                        userVerification: "required",
                        timeout: 60000
                    }
                });
            } catch {
                return null;
            }

            if (!bioKeyBase64 || !encryptedSeedBase64) return null;

            const key = await importAesKey(fromBase64(bioKeyBase64), ["decrypt"]);
            const seed = await decryptWithKey(key, fromBase64(encryptedSeedBase64));
            return seed ? toBase64(seed) : null;
        }
    };

    root.dgbWebAuthn = api;
    if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
