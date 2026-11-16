# Quickstart: Validating the Multisig Management Revamp

## Prerequisites

- Two wallets on the device (Wallet A, Wallet B) on testnet/regtest or mainnet with small amounts.
- Two other co-signer public keys (or a second browser profile acting as co-signer).
- Build and tests green:

```bash
dotnet build
dotnet test tests/DigiByte.Wallet.Tests
```

Run the app with the usual dev launch configuration and test at 360×800 and 320 px, in light and dark themes.

## Scenario 1 — Wallet scoping (US1, SC-001)

1. In Wallet A create a 2-of-3 multisig and save a draft.
2. Switch to Wallet B → Multisig. Expect the empty state; neither item appears.
3. Open Wallet A's multisig URL while Wallet B is active. Expect a "belongs to another wallet" message and a way back.
4. Switch back to Wallet A. Both items are present and unchanged.
5. Legacy: with pre-update data, open Multisig. Items matching the wallet key are attributed; unmatched ones appear only under "Unassigned multisigs" with Claim.
6. Open a saved link to your own pre-update multisig before ever opening the list: it opens normally (not "another wallet").
7. If two wallets on the device both hold a key of the same multisig, it appears under each of them, and a rename in one is seen in the other.
8. Delete a multisig: a confirmation shows the redeem-script backup warning with a Download option before anything is removed. If another wallet also owns it, it remains available there.

## Scenario 2 — Rename (US2, SC-002)

1. Rename the multisig from the dashboard (empty name rejected; 41+ chars rejected with a message; duplicate warns).
2. Reload the app. The name shows in list, header, Activity, and backup note; the address is unchanged.
3. Rename a draft; its progress is intact.

## Scenario 3 — Full keys (US3, SC-003)

1. Expand each co-signer. The full 66-character key is shown wrapped, with Copy and QR; "You" is marked.
2. Edit a co-signer label; the key is unchanged.
3. At 320 px, no clipped text or horizontal scroll.

## Scenario 4 — Dashboard (US4, SC-004)

1. At 360×800 the name, badge, balance, address, and Send/Receive/Pending are visible without scrolling.
2. Backup prompt shows until Download or Copy is used; afterwards a dated "Backed up" line shows.
3. Disconnect the network: skeleton/offline state, not a 0 balance.
4. Tap Receive: a sheet shows the address, its QR code, and Copy.

## Scenario 5 — Send (US5, SC-005)

1. Paste an address; scan an address QR; scan a `digibyte:` URI with amount (prefills).
2. Wrong-network or bad-checksum address: inline error, Continue disabled.
3. Tap Max: amount = balance − fee, remaining 0. Over-balance and dust amounts are blocked; a change amount below dust is added to the fee (no change output).
4. Review shows full recipient, amount, fee, signatures required, memo; double-tap Confirm creates one proposal; Sign now / Share offered.

## Scenario 6 — Pending (US7, SC-007)

1. Sign locally, share the PSBT (copy/QR/file), import a co-signer's signed copy by paste/scan.
2. Importing a PSBT for a different proposal or wallet is rejected with a reason.
3. At threshold, Broadcast becomes primary. Cancel shows the local-only warning and moves it to history; a cancelled proposal cannot be broadcast.
4. A proposal created before the update still accepts a valid co-signer signature copy.
5. Double-tapping Broadcast broadcasts once.

## Scenario 7 — Activity (US6, SC-006)

1. Receive funds to the multisig; a Received entry appears with confirmations.
2. Broadcast a proposal; it appears once as Sent with TxID (copy / open in explorer).
3. Filter by Received/Sent/Pending/Failed; empty states make sense.
4. With 25+ entries, the first 25 show and Load more fetches the next page.
5. Offline: local proposals and prior sends show with an "offline" notice.
