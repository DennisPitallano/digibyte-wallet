// WebAuthn + PRF extension for biometric wallet unlock
// Uses platform authenticator (fingerprint/face) with PRF to derive wrapping keys.
// Falls back to assertion-gated encryption when PRF is not supported.

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

    // Import raw bytes as AES-256-GCM key
    async function importAesKey(rawBytes, usages) {
        return crypto.subtle.importKey(
            "raw", rawBytes, { name: "AES-GCM" }, false, usages
        );
    }

    // AES-256-GCM encrypt
    async function encryptWithKey(key, plaintext) {
        const iv = crypto.getRandomValues(new Uint8Array(WA_IV_SIZE));
        const encrypted = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext);
        const result = new Uint8Array(WA_IV_SIZE + encrypted.byteLength);
        result.set(iv, 0);
        result.set(new Uint8Array(encrypted), WA_IV_SIZE);
        return result;
    }

    // AES-256-GCM decrypt
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
        isSupported: async function () {
            if (!window.PublicKeyCredential) return false;
            try {
                return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
            } catch {
                return false;
            }
        },

        // Enroll biometric for a wallet.
        // Returns { credentialId, wrappedKey, bioSeed, fallbackKey? } (all base64).
        // If PRF is supported, fallbackKey is null (wrapping key is PRF-derived).
        // If PRF is NOT supported, fallbackKey contains the encryption key (assertion-gated).
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

            const wrappingKey = fromBase64(wrappingKeyBase64);
            const seed = fromBase64(seedBase64);

            // Encrypt seed with wrapping key
            const wrapKeyEnc = await importAesKey(wrappingKey, ["encrypt"]);
            const bioSeed = await encryptWithKey(wrapKeyEnc, seed);

            // Try PRF-based encryption first (most secure — key bound to authenticator)
            const prfResult = credential.getClientExtensionResults()?.prf?.results?.first;

            let wrappedKey;
            let fallbackKey = null;

            if (prfResult) {
                // PRF path: encrypt wrapping key with authenticator-derived key
                const prfKey = await importAesKey(new Uint8Array(prfResult), ["encrypt"]);
                wrappedKey = await encryptWithKey(prfKey, wrappingKey);
            } else {
                // Fallback: generate random encryption key, encrypt wrapping key with it.
                // Security: the fallback key is stored in IndexedDB but decryption is
                // gated behind a WebAuthn assertion (biometric check) in authenticate().
                const fbKeyBytes = crypto.getRandomValues(new Uint8Array(32));
                const fbKey = await importAesKey(fbKeyBytes, ["encrypt"]);
                wrappedKey = await encryptWithKey(fbKey, wrappingKey);
                fallbackKey = toBase64(fbKeyBytes);
            }

            return {
                credentialId: toBase64(credential.rawId),
                wrappedKey: toBase64(wrappedKey),
                bioSeed: toBase64(bioSeed),
                fallbackKey: fallbackKey
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

        // Authenticate with biometric and return the unwrapped seed (base64) or null.
        // fallbackKeyBase64 is provided when PRF was not available during enrollment.
        authenticate: async function (walletId, credentialIdBase64, wrappedKeyBase64, bioSeedBase64, fallbackKeyBase64) {
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
                        extensions: fallbackKeyBase64 ? {} : {
                            prf: { eval: { first: salt } }
                        }
                    }
                });
            } catch {
                return null; // User cancelled or authenticator error
            }

            let unwrappedKey;

            if (fallbackKeyBase64) {
                // Fallback path: assertion succeeded (user authenticated), use stored key
                const fbKeyBytes = fromBase64(fallbackKeyBase64);
                const fbKey = await importAesKey(fbKeyBytes, ["decrypt"]);
                unwrappedKey = await decryptWithKey(fbKey, wrappedKey);
            } else {
                // PRF path: decrypt wrapping key with authenticator-derived key
                const prfResult = assertion.getClientExtensionResults()?.prf?.results?.first;
                if (!prfResult) return null;
                const prfKey = await importAesKey(new Uint8Array(prfResult), ["decrypt"]);
                unwrappedKey = await decryptWithKey(prfKey, wrappedKey);
            }

            if (!unwrappedKey) return null;

            // Decrypt seed with unwrapped wrapping key
            const wrapKey = await importAesKey(unwrappedKey, ["decrypt"]);
            const seed = await decryptWithKey(wrapKey, bioSeed);
            if (!seed) return null;

            return toBase64(seed);
        }
    };
})();
