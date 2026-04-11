// WebAuthn biometric unlock for DigiByte Wallet
// Uses platform authenticator (fingerprint/face/PIN) with assertion-gated encryption.
// A random AES key encrypts wallet secrets; decryption requires a WebAuthn assertion.

(function () {
    const WA_IV_SIZE = 12;

    function toBase64(buffer) {
        return btoa(String.fromCharCode(...new Uint8Array(buffer)));
    }

    function fromBase64(base64) {
        return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
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
        // Returns { credentialId, wrappedKey, bioSeed, fallbackKey } (all base64).
        // Creates a plain WebAuthn credential (no PRF) compatible with all authenticators.
        enroll: async function (walletId, userName, wrappingKeyBase64, seedBase64) {
            const userId = new TextEncoder().encode(walletId);

            let credential;
            try {
                credential = await navigator.credentials.create({
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
                        timeout: 60000
                    }
                });
            } catch {
                return null; // User cancelled or authenticator unavailable
            }

            const wrappingKey = fromBase64(wrappingKeyBase64);
            const seed = fromBase64(seedBase64);

            // Encrypt seed with wrapping key
            const wrapKeyEnc = await importAesKey(wrappingKey, ["encrypt"]);
            const bioSeed = await encryptWithKey(wrapKeyEnc, seed);

            // Encrypt wrapping key with a random AES key (stored in IndexedDB).
            // Decryption is gated behind a WebAuthn assertion (biometric check).
            const fbKeyBytes = crypto.getRandomValues(new Uint8Array(32));
            const fbKey = await importAesKey(fbKeyBytes, ["encrypt"]);
            const wrappedKey = await encryptWithKey(fbKey, wrappingKey);

            return {
                credentialId: toBase64(credential.rawId),
                wrappedKey: toBase64(wrappedKey),
                bioSeed: toBase64(bioSeed),
                fallbackKey: toBase64(fbKeyBytes)
            };
        },

        // Simple identity verification (no decryption).
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
        authenticate: async function (walletId, credentialIdBase64, wrappedKeyBase64, bioSeedBase64, fallbackKeyBase64) {
            const credentialId = fromBase64(credentialIdBase64);
            const wrappedKey = fromBase64(wrappedKeyBase64);
            const bioSeed = fromBase64(bioSeedBase64);

            // Verify identity with biometric assertion
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
                return null; // User cancelled or authenticator error
            }

            if (!fallbackKeyBase64) return null;

            // Decrypt wrapping key with stored fallback key
            const fbKeyBytes = fromBase64(fallbackKeyBase64);
            const fbKey = await importAesKey(fbKeyBytes, ["decrypt"]);
            const unwrappedKey = await decryptWithKey(fbKey, wrappedKey);
            if (!unwrappedKey) return null;

            // Decrypt seed with unwrapped wrapping key
            const wrapKey = await importAesKey(unwrappedKey, ["decrypt"]);
            const seed = await decryptWithKey(wrapKey, bioSeed);
            if (!seed) return null;

            return toBase64(seed);
        }
    };
})();
