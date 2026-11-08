// Dedicated worker that runs Argon2id off the main thread (FR-020).
// Created per operation by crypto.js and terminated afterwards so the memory is released.
// Protocol: see specs/009-stronger-pin-protection/contracts/js-crypto-api.md
//   in : { id, op: "derive", password: Uint8Array, salt: Uint8Array, memoryKiB, passes, lanes }
//   out: { id, ok: true, key: Uint8Array } | { id, ok: false, reason }

importScripts("/js/vendor/hash-wasm-argon2.umd.min.js");

self.onmessage = async (event) => {
    const { id, op, password, salt, memoryKiB, passes, lanes } = event.data || {};
    if (op !== "derive") {
        self.postMessage({ id, ok: false, reason: "Unreadable" });
        return;
    }
    try {
        const key = await self.hashwasm.argon2id({
            password,
            salt,
            parallelism: lanes,
            iterations: passes,
            memorySize: memoryKiB,
            hashLength: 32,
            outputType: "binary",
        });
        self.postMessage({ id, ok: true, key }, [key.buffer]);
    } catch (err) {
        // Allocation or instantiation failure: report as environmental, never as a wrong credential.
        self.postMessage({ id, ok: false, reason: "ResourceExhausted" });
    } finally {
        if (password && password.fill) password.fill(0);
    }
};
