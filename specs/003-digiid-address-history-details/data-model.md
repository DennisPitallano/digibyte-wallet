# Data Model: Digi-ID Address Visibility & Login History Details

## AuthHistoryEntry (stored in `digiid_login_history`, JSON list, newest first, max 50)

| Field | Type | Required | Notes |
|---|---|---|---|
| Domain | string | yes (existing) | Site host |
| Address | string | yes (existing) | Full signing address (legacy P2PKH) |
| Timestamp | DateTime (UTC) | yes (existing) | Shown in local time, with seconds, in details |
| CallbackUrl | string? | no (new) | Full callback URL (with port/query) |
| Uri | string? | no (new) | Original `digiid://` challenge URI that was signed |
| Nonce | string? | no (new) | Challenge value (`x` parameter) |
| Signature | string? | no (new) | Base64 compact signature sent to the site |
| IsUnsecure | bool? | no (new) | True when the site used HTTP |

- **Outcome**: not stored; every entry is a successful sign-in and the details view labels it "Success".
- **Legacy entries**: new fields absent → null → shown as "Not recorded".
- **Lifecycle**: created on successful sign-in; removed by "Clear all" or when pushed past the 50-entry cap.

## Signing Address (derived, not stored)

Deterministic function of (wallet seed, domain): path `m/13'/siteIndex'/0'/0`, where `siteIndex = SHA256(lowercase domain)[0..4] & 0x7FFFFFFF`. Unavailable when the wallet is locked or watch-only.
