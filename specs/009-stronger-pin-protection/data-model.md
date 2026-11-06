# Data Model: Stronger Wallet PIN & Passphrase Protection

All values live in the existing IndexedDB `keyval` store through `ISecureStorage`. Nothing below is sent to any server. Metadata is plaintext by design (it contains no secret); secrets are only ever inside envelopes.

## Protected secret (envelope)

A single encrypted wallet secret (recovery phrase, WIF key, xpub, multisig config, DigiDollar store payload).

| Field | Meaning | Rules |
|---|---|---|
| format version | `dgb2` now; legacy values have no prefix | Unknown version: refuse, change nothing |
| KDF id | Argon2id | Only known ids accepted |
| KDF parameters | memory KiB, passes, lanes | Must be at or above the floor profile; below floor is rejected on write and read |
| credential kind | `pin` or `passphrase` | Part of the authenticated header |
| salt | 16 random bytes | Fresh per encryption |
| nonce | 12 random bytes | Fresh per encryption |
| ciphertext + tag | AES-256-GCM with the header as AAD | Tamper or wrong credential fails authentication |

Byte layout: [contracts/envelope-format.md](contracts/envelope-format.md).

## Wallet record additions (`WalletInfo`, plaintext)

| Field | Meaning |
|---|---|
| `CredentialType` | `pin` (default for existing wallets) or `passphrase`. Used only to choose which input to show; the envelope header is the authority. |
| `ProtectionLevel` | `standard`, `legacy` (created on an unsupported browser, with user agreement), or `upgrading` is never persisted; derived at runtime from which keys exist |
| `PassphraseNudgeDismissed` | Boolean for the dismissible upgrade card (SC-010) |

## Migration state (`wallet_migration_{walletId}`)

| Field | Meaning |
|---|---|
| `migratedAt` | UTC timestamp when the verified `v2` envelope was written |
| `legacyFingerprint` | SHA-256 of the legacy ciphertext at migration time. A mismatch later means an older app version changed the legacy value |
| `legacyRemovedAt` | Set when the legacy copy has been deleted |

### Wallet protection states and transitions

```text
Legacy only ──unlock OK──► Legacy + V2 (verified, ≤ 7 days) ──unlock OK after 7 days──► V2 only
     ▲                              │                                             │
     │                              ├─ change/reset credential ───────────────────┘ (legacy deleted immediately)
     │                              └─ legacy fingerprint changed (rollback + older app) ─► re-migrate
     └──── migration failure at any step: stays Legacy only, retried on next unlock

New wallet (supported browser) ─► V2 only
New wallet (unsupported browser, user agreed) ─► Legacy only, marked legacy protection ─► upgrades on first unlock on a capable browser
```

## Biometric records

| Key | Before | After (PRF supported) | After (PRF unsupported) |
|---|---|---|---|
| `bio_cred` | credential id | credential id (new, PRF-enabled) | unchanged |
| `bio_key` | random AES key stored in the clear | **removed**; key derived at unlock from the PRF output | unchanged |
| `bio_seed_{id}` | seed encrypted with `bio_key` | seed encrypted with the PRF-derived key | unchanged |
| `bio_prf_state` | n/a | `enrolled-prf` | `enrolled-weak`, with `bio_weak_warned` once acknowledged |

## Credential

Not stored. A six-digit PIN, or a passphrase (NFKC-normalised, 12 to 256 characters, not on the deny list). The type is recorded in `WalletInfo.CredentialType` and in the envelope header.

## Validation rules (from requirements)

- Passphrase rejected with a specific reason: too short, on the common list, trivially repeated or sequential (FR-002).
- A migrated value must decrypt to the identical plaintext before the record is written (FR-011).
- Parameters below the floor profile are never written; a read of such an envelope is refused (FR-021).
- Credential change and reset delete the legacy copy immediately (FR-012, research R4).
