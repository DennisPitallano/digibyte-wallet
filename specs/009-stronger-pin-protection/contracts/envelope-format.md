# Contract: Encryption envelope `dgb2`

## String form

```text
dgb2$<base64url, unpadded>
```

A stored value that does not start with `dgb2$` is a legacy value (standard Base64 of `salt(16) | iv(12) | ciphertext+tag`) and is read with the legacy PBKDF2 path. Legacy Base64 never contains `$`, so detection is unambiguous. Any other `dgbN$` prefix with N greater than 2 is an unknown future version: refuse, report "update required", and change nothing.

## Binary layout (before base64url)

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | version | `0x02` |
| 1 | 1 | kdf id | `0x01` = Argon2id |
| 2 | 4 | memory KiB | big-endian |
| 6 | 1 | passes | at least 2 |
| 7 | 1 | lanes | 1 today |
| 8 | 1 | credential kind | `0x01` pin, `0x02` passphrase |
| 9 | 16 | salt | random per encryption |
| 25 | 12 | nonce | random per encryption |
| 37 | n | ciphertext + 16-byte GCM tag | |

Header = bytes 0 to 8. It is passed to AES-256-GCM as additional authenticated data.

## Rules

- Key = Argon2id(password = credential bytes, salt, memory, passes, lanes) with a 32-byte output. Credential bytes: PIN as ASCII digits; passphrase as UTF-8 of the NFKC-normalised text.
- Reject on read and write: memory below 19,456 KiB, passes below 2, lanes other than 1, unknown kdf id, truncated input (shorter than 37 + 16 bytes), kind not in {1, 2}.
- Decrypt failure classes: authentication failure is `WrongCredential` (cannot distinguish tamper from wrong credential, and need not); structural failure is `Unreadable`; version above 2 is `UnsupportedVersion`; allocation or worker failure is `ResourceExhausted`.
- Salt and nonce are never reused; every encryption generates both.

## Known-answer fixtures

`tests/js/fixtures/` and `tests/DigiByte.Wallet.Tests/Fixtures/` hold: one legacy value produced by the current code, one `dgb2` value per profile, and tampered variants (flipped header bit, flipped ciphertext bit, truncated, unknown version). Both test suites read the same files.
