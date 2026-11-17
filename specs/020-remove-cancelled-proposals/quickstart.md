# Quickstart: Validate Remove Cancelled Proposals

**Prerequisites**: run the Web app locally (`dotnet run --project src/DigiByte.Web`) and open a multisig wallet with at least three payments awaiting signatures.

**Automated**: `dotnet test tests/DigiByte.Wallet.Tests --filter MultisigRemoveCancelled`

**Manual**
1. Cancel two proposals. Open Pending: both show "Cancelled or expired" with "Remove from list". "Clear all cancelled" appears above the list.
2. Click "Remove from list" on one. Only that card disappears; the notice says it was removed from this device's list. Reload: it stays gone.
3. Cancel another so two are cancelled. Click "Clear all cancelled": a prompt states the count and that it can't be undone. "Keep them" changes nothing; "Remove N" deletes them and the open proposal stays.
4. Open Activity: removed proposals are gone; on-chain transactions remain.
5. With only one cancelled proposal, confirm "Clear all cancelled" is not shown.
