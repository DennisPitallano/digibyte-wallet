// WebAuthn biometric unlock for DigiByte Wallet
// Single credential for all wallets. Each wallet's seed is encrypted with
// a shared AES-256 key; decryption requires a WebAuthn assertion (biometric).

(function () {
    const WA_IV_SIZE = 12;

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

        // Create a single WebAuthn credential for the app.
        // Returns { credentialId, bioKey } (both base64) or null.
        // bioKey is a random AES-256 key for encrypting/decrypting wallet seeds.
        enroll: async function (userName) {
            const userId = crypto.getRandomValues(new Uint8Array(16));

            let credential;
            try {
                credential = await navigator.credentials.create({
                    publicKey: {
                        rp: { name: "DigiByte Wallet", id: window.location.hostname },
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
})();
