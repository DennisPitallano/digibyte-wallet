# Research: Wallet Addresses in Activity

No open "NEEDS CLARIFICATION" items remained after the spec's clarification rounds. The points below record what the existing code does and the decisions that follow from it.

## R1. Where do transactions get built, and what do we know at that point?

**Finding**: `WalletService.GetTransactionHistoryAsync` (src/DigiByte.Wallet/Services/WalletService.cs) merges explorer results with locally tracked sends. For each explorer transaction it already tests every input and output against the wallet's own address set (`isOurs`), but it keeps only the counterparty address.

**Decision**: Record the own addresses it already finds, on the same pass. No extra explorer calls.

**Alternatives**: Re-fetch each transaction for its details (extra network, slower); derive addresses in the UI (UI would need wallet internals, breaks layering).

## R2. Locally tracked sends

**Finding**: `TransactionTracker.RecordSendAsync` stores a send before the explorer knows about it, with only the destination. Local records overwrite explorer records on merge ("local wins for freshness"), which would drop any addresses found from the explorer.

**Decision**: After the merge, copy own-address fields from the explorer record onto the local record when the local one has none. The copied addresses are saved with the local record through `TransactionTracker`, so they still show after a reload when the explorer is unreachable. Before the explorer knows the transaction, the row shows no wallet address (spec edge case: omit rather than guess).

**Alternatives**: Record the funding address at send time (the wallet may spend from many inputs and change selection happens inside the send flow, so it would need changes in six send paths); let local records win entirely (loses addresses).

## R3. Telling receiving addresses from change

**Finding**: `GetAllAddresses()` returns receive and change addresses mixed. FR-007 says change must not appear as a used receiving address. A transfer between own addresses (clarified) has own outputs that are real receiving addresses and own outputs that are change.

**Decision**: Add a way to get the wallet's change addresses (normalized) from the same derivation cache, and treat own outputs on that set as change. For single-key wallets there is no change address distinct from the wallet addresses; the change goes back to the same key, so for a send with a single-key wallet own outputs are treated as change unless the destination is a different own address format.

**Alternatives**: Treat "own output of a sent tx that is not the largest" as change (heuristic, wrong in batch/self-transfer cases).

## R4. Address comparison

**Finding**: The code normalizes bech32 addresses (`NormalizeAddress`) because explorers can return a different checksum variant. Labels are stored by the address string.

**Decision**: Group, filter and look up labels using the normalized form, but display the address as the wallet generated it. Label lookup tries the displayed form.

## R5. Old stored data

**Finding**: `TransactionRecord` is serialized as JSON in secure storage and in Home's cache.

**Decision**: New members are optional with an empty default. Old JSON loads unchanged and shows no wallet address until history is rebuilt. No migration needed.

## R6. Where the picker lives

**Decision (from clarification)**: In the activity filter bar on `Transactions.razor`. Home shows the same `TransactionRow`, so it gets the address text but not the filter.

## R7. Row space on phones

**Decision**: One short line under the existing title/time line: label if one exists, else a shortened address (start…end), then "+N more" when several apply. Full text only in the detail dialog. Exact styling is a design detail settled during implementation, using existing tokens.
