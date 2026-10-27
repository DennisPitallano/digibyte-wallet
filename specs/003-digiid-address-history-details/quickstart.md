# Quickstart: Validating Digi-ID Address Visibility & Login History Details

## Prerequisites

- An unlocked HD wallet in the web app, and a Digi-ID test site or the local dev callback (`digiid://localhost:<port>/...?x=<nonce>&u=1`).

## Automated

```powershell
dotnet test tests/DigiByte.Crypto.Tests
dotnet test tests/DigiByte.Wallet.Tests
```

Expect: the address helper equals `Sign().Address`; same domain gives the same address and different domains give different ones; a locked or watch-only wallet throws; legacy history JSON (domain, address, timestamp only) deserializes.

## Manual

1. Open Identity and paste a Digi-ID URI. The confirmation card shows the full "Signing address". Copy it, paste elsewhere and compare. (US1)
2. Approve. The success card shows the same address with a copy button, and History gains an entry. (US1, US3, FR-004)
3. Tap the history entry. The details modal lists all fields; copy the signature, nonce and URL; closing returns to the list. (US2)
4. Sign in to the same site again: same address. Another site: different address.
5. Use a `u=1` (HTTP) URI: the details flag the insecure connection.
6. In browser devtools, remove the new fields from one stored entry: its details show "Not recorded" with no errors. (FR-009)
7. Check at 360px width and in dark theme; "Clear all" removes entries and their details.
