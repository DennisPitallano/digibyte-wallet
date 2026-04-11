// WebAuthn + PRF extension for biometric wallet unlock
// Uses platform authenticator (fingerprint/face) with PRF to derive wrapping keys.
// No secrets are stored at rest — the PRF output is bound to the authenticator.

(function () {
    const WA_IV_SIZE = 12;

    function toBase64(buffer) {
        return btoa(String.fromCharCode(...new Uint8Array(buffer)));
    }

    function fromBase64(base64) {
        return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    }

    // Deterministic PRF salt per wallet: SHA-256("dgb-wallet-bio:" + walletId)
    async function prfSalt(walletId) {
        const enc = new TextEncoder();
        const hash = await crypto.subtle.digest("SHA-256", enc.encode("dgb-wallet-bio:" + walletId));
        return new Uint8Array(hash);
    }

    // Import raw PRF output as AES-256-GCM key
    async function importPrfKey(prfOutput) {
        return crypto.subtle.importKey(
            "raw", prfOutput, { name: "AES-GCM" }, false, ["encrypt", "decrypt"]
        );
    }

    // AES-256-GCM encrypt with raw key (not PBKDF2-derived)
    async function encryptWithKey(key, plaintext) {
        const iv = crypto.getRandomValues(new Uint8Array(WA_IV_SIZE));
        const encrypted = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext);
        const result = new Uint8Array(WA_IV_SIZE + encrypted.byteLength);
        result.set(iv, 0);
        result.set(new Uint8Array(encrypted), WA_IV_SIZE);
        return result;
    }

    // AES-256-GCM decrypt with raw key
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

    window.dgbWebAuthn = {
        // Check if platform authenticator is available.
        // PRF support is validated at enrollment time — if the authenticator doesn't
        // support PRF, enroll() will throw and the UI handles it gracefully.
        isSupported: async function () {
            if (!window.PublicKeyCredential) return false;
            try {
                return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
            } catch {
                return false;
            }
        },

        // Enroll biometric for a wallet. Returns { credentialId, wrappedKey, bioSeed } (all base64)
        enroll: async function (walletId, userName, wrappingKeyBase64, seedBase64) {
            const salt = await prfSalt(walletId);
            const userId = new TextEncoder().encode(walletId);

            const credential = await navigator.credentials.create({
                publicKey: {
                    rp: { name: "DigiByte Wallet", id: window.location.hostname },
                    user: { id: userId, name: userName, displayName: userName },
                    challenge: crypto.getRandomValues(new Uint8Array(32)),
                    pubKeyCredParams: [
                        { alg: -7, type: "public-key" },   // ES256
                        { alg: -257, type: "public-key" },  // RS256
                    ],
                    authenticatorSelection: {
                        authenticatorAttachment: "platform",
                        userVerification: "required",
                        residentKey: "discouraged",
                    },
                    timeout: 60000,
                    extensions: {
                        prf: { eval: { first: salt } }
                    }
                }
            });

            // Extract PRF result
            const prfResult = credential.getClientExtensionResults()?.prf?.results?.first;
            if (!prfResult) {
                throw new Error("PRF extension not supported by this authenticator");
            }

            const prfKey = await importPrfKey(new Uint8Array(prfResult));
            const wrappingKey = fromBase64(wrappingKeyBase64);
            const seed = fromBase64(seedBase64);

            // Encrypt wrapping key with PRF-derived key
            const wrappedKey = await encryptWithKey(prfKey, wrappingKey);

            // Encrypt seed with wrapping key (raw AES, not PBKDF2)
            const wrapKey = await crypto.subtle.importKey(
                "raw", wrappingKey, { name: "AES-GCM" }, false, ["encrypt"]
            );
            const bioSeed = await encryptWithKey(wrapKey, seed);

            return {
                credentialId: toBase64(credential.rawId),
                wrappedKey: toBase64(wrappedKey),
                bioSeed: toBase64(bioSeed)
            };
        },

        // Simple identity verification (no PRF / no decryption).
        // Returns true if the user confirms with biometric, false otherwise.
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

        // Authenticate with biometric and return the unwrapped seed (base64) or null
        authenticate: async function (walletId, credentialIdBase64, wrappedKeyBase64, bioSeedBase64) {
            const salt = await prfSalt(walletId);
            const credentialId = fromBase64(credentialIdBase64);
            const wrappedKey = fromBase64(wrappedKeyBase64);
            const bioSeed = fromBase64(bioSeedBase64);

            let assertion;
            try {
                assertion = await navigator.credentials.get({
                    publicKey: {
                        challenge: crypto.getRandomValues(new Uint8Array(32)),
                        allowCredentials: [{ id: credentialId, type: "public-key", transports: ["internal"] }],
                        userVerification: "required",
                        timeout: 60000,
                        extensions: {
                            prf: { eval: { first: salt } }
                        }
                    }
                });
            } catch {
                return null; // User cancelled or authenticator error
            }

            const prfResult = assertion.getClientExtensionResults()?.prf?.results?.first;
            if (!prfResult) return null;

            // Decrypt wrapping key with PRF-derived key
            const prfKey = await importPrfKey(new Uint8Array(prfResult));
            const unwrappedKey = await decryptWithKey(prfKey, wrappedKey);
            if (!unwrappedKey) return null;

            // Decrypt seed with unwrapped key
            const wrapKey = await crypto.subtle.importKey(
                "raw", unwrappedKey, { name: "AES-GCM" }, false, ["decrypt"]
            );
            const seed = await decryptWithKey(wrapKey, bioSeed);
            if (!seed) return null;

            return toBase64(seed);
        }
    };
})();
