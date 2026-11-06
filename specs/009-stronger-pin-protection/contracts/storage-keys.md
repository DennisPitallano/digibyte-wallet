# Contract: Storage keys

| Key | Written by | Read by | Notes |
|---|---|---|---|
| `wallet_seed_{id}` | Existing code | Existing and new code | Legacy ciphertext. Never rewritten by migration. Deleted after the 7-day window, or immediately on credential change or reset. |
| `wallet_seed_v2_{id}` | New code | New code | `dgb2` envelope. New wallets on supported browsers write only this key. Preferred on read. |
| `wallet_migration_{id}` | New code | New code | `{ migratedAt, legacyFingerprint, legacyRemovedAt? }` |
| `wallet_info_{id}` | Existing | Existing and new | Gains `CredentialType` and `PassphraseNudgeDismissed`; unknown fields are ignored by older builds. |
| `bio_cred`, `bio_enabled` | Existing | Existing and new | `bio_cred` is replaced when a wallet set is re-enrolled with PRF. |
| `bio_key` | Existing | Existing | Removed after PRF re-enrolment. Present only for browsers without PRF. |
| `bio_seed_{id}` | Existing | Existing and new | Re-encrypted under the PRF-derived key at re-enrolment. |
| `bio_prf_state`, `bio_weak_warned` | New | New | Drives the one-time warning and re-enrolment prompt. |

## Read order for a wallet seed

1. If `wallet_seed_v2_{id}` exists and the migration record's `legacyFingerprint` matches the current legacy value (or the legacy key is gone): use `v2`.
2. If the fingerprint does not match: legacy is authoritative; unlock from it and re-migrate.
3. Otherwise use the legacy key and migrate after a successful unlock.

## Compatibility promises

- An older app build keeps working during the window because it only knows `wallet_seed_{id}`, which is unchanged.
- After the legacy copy is removed, an older cached build will not find a seed. Recovery from the recovery phrase is the supported path; the forward-compatible `dgb2$` prefix means future format changes will be detected by this and later builds.
- Multisig config and DigiDollar store payloads follow the same read order under their existing keys with a `_v2` sibling.
