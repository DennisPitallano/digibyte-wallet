# Contract: Browser crypto interop

`window.dgbCrypto` stays the single entry point. Existing method names keep working for callers that have not moved; the new ones return typed results.

## Existing (kept, behaviour extended)

| Method | Behaviour |
|---|---|
| `encrypt(plaintextBase64, credential)` | Stays on the legacy PBKDF2 format until T052; it is the legacy write path used by the unsupported-device flow (FR-021). New callers use `encryptV2`. |
| `decrypt(packedBase64, credential)` | Reads both formats; returns plaintext Base64 or `null` for any failure (unchanged contract for callers not yet migrated). |

## New

| Method | Input | Output |
|---|---|---|
| `encryptV2(plaintextBase64, credential, kind, profile?)` | kind is `pin` or `passphrase`; profile is `standard`, `low-memory`, or omitted for automatic fallback down to the floor | `{ ok: true, envelope }` or `{ ok: false, reason: "ResourceExhausted" \| "Unsupported" }`. Never writes below the floor. |
| `decryptV2(value, credential)` | Either format | `{ ok: true, plaintextBase64, format: "legacy" \| "dgb2", params }` or `{ ok: false, reason: "WrongCredential" \| "Unreadable" \| "UnsupportedVersion" \| "ResourceExhausted" }` |
| `capabilities()` | none | `{ argon2: boolean, worker: boolean, webLocks: boolean, prf: "yes" \| "no" \| "unknown" }` |
| `normalisePassphrase(text)` | string | NFKC-normalised string |

## Worker protocol (`crypto-worker.js`)

Message in: `{ id, op: "derive", password: Uint8Array, salt: Uint8Array, memoryKiB, passes, lanes }`.
Message out: `{ id, ok: true, key: Uint8Array }` or `{ id, ok: false, reason }`.
The worker is created per operation and terminated afterwards. Credentials are transferred, not logged, and wiped after use where the platform allows.

## C# side (`ICryptoService`)

```text
Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential);   // typed result
Task<CryptoResult<string>> EncryptAsync(byte[] plaintext, string credential, CredentialKind kind);
```

`CryptoResult` carries `Success` or a `CryptoFailure` of `WrongCredential | Unreadable | UnsupportedVersion | ResourceExhausted`. Only `WrongCredential` increments the throttle. The previous byte-array methods remain as thin wrappers during the transition and are removed in the final task.

## WebAuthn additions (`dgbWebAuthn`)

| Method | Behaviour |
|---|---|
| `enrollPrf(walletName)` | Creates a credential requesting `prf`; returns `{ credentialId, prfSupported }`. No key material is returned for storage. |
| `deriveKeyPrf(credentialId)` | Runs an assertion with the PRF input, derives an AES key with HKDF-SHA256, returns it for immediate use; never persisted. |
| `enroll` / `authenticate` | Existing weak path kept only for browsers without PRF, behind the warning flow. |
