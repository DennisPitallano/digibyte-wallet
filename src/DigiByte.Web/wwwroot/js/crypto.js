// AES-256-GCM encryption via SubtleCrypto (browser-native, WASM-compatible)
// Format: salt(16) + iv(12) + ciphertext+tag (GCM appends 16-byte tag automatically)

const SALT_SIZE = 16;
const IV_SIZE = 12;
const KEY_SIZE = 256;
const ITERATIONS = 100000;

async function deriveKey(pin, salt) {
    const enc = new TextEncoder();
    const keyMaterial = await crypto.subtle.importKey(
        "raw", enc.encode(pin), "PBKDF2", false, ["deriveKey"]
    );
    return crypto.subtle.deriveKey(
        { name: "PBKDF2", salt: salt, iterations: ITERATIONS, hash: "SHA-256" },
        keyMaterial,
        { name: "AES-GCM", length: KEY_SIZE },
        false,
        ["encrypt", "decrypt"]
    );
}

window.dgbCrypto = {
    encrypt: async function (plaintextBase64, pin) {
        const plaintext = Uint8Array.from(atob(plaintextBase64), c => c.charCodeAt(0));
        const salt = crypto.getRandomValues(new Uint8Array(SALT_SIZE));
        const iv = crypto.getRandomValues(new Uint8Array(IV_SIZE));
        const key = await deriveKey(pin, salt);
        const encrypted = await crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv }, key, plaintext
        );
        // Pack: salt + iv + ciphertext (includes GCM tag)
        const result = new Uint8Array(SALT_SIZE + IV_SIZE + encrypted.byteLength);
        result.set(salt, 0);
        result.set(iv, SALT_SIZE);
        result.set(new Uint8Array(encrypted), SALT_SIZE + IV_SIZE);
        return btoa(String.fromCharCode(...result));
    },

    decrypt: async function (packedBase64, pin) {
        const packed = Uint8Array.from(atob(packedBase64), c => c.charCodeAt(0));
        if (packed.length < SALT_SIZE + IV_SIZE + 1) return null;

        const salt = packed.slice(0, SALT_SIZE);
        const iv = packed.slice(SALT_SIZE, SALT_SIZE + IV_SIZE);
        const ciphertext = packed.slice(SALT_SIZE + IV_SIZE);

        const key = await deriveKey(pin, salt);
        try {
            const decrypted = await crypto.subtle.decrypt(
                { name: "AES-GCM", iv: iv }, key, ciphertext
            );
            return btoa(String.fromCharCode(...new Uint8Array(decrypted)));
        } catch {
            // Wrong PIN or tampered data
            return null;
        }
    }
};
