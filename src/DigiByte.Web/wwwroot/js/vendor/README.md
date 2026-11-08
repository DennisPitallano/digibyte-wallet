# Vendored scripts

## hash-wasm-argon2.umd.min.js

- Source: npm `hash-wasm@4.13.0`, file `dist/argon2.umd.min.js` (30,094 bytes), MIT licence (`hash-wasm-LICENSE.txt`).
- SHA-256: `acfe11b015852f593aaddc51116f71dc7850e4551e6b34d42c1f88cf39635f03`
- WASM is embedded as base64 and instantiated via `WebAssembly.instantiate`; the file contains no `fetch`, `eval` or `new Function`, so the existing CSP (`'wasm-unsafe-eval'`) is enough with no policy change.
- Sanity check (Node): argon2id, password `pw`, salt 16 x 0x01, 64 MiB, 3 passes, 1 lane, 32 bytes = `6cc8330b81cc4258e0806d00d18523028996d710cd2bebfc703ab616a18ea0f6` (about 0.3 s on the dev desktop).
- Browser CSP instantiation is still to be confirmed in the running app.
