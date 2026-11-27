# Research: DigiDollar on the merchant's own keys

Facts below were checked in the code on 2026-11-27. Paths are relative to the repository root.

## R1. Key format and derivation

**Decision**: the store's DD key is an NBitcoin `ExtPubKey` string (`xpub…` on mainnet, `tpub…` on testnet) for the account node `m/86'/coin'/0'`. This is exactly what `ExtPubKey.ToString(network)` gives, the same format `/settings/xpub` exports for DGB. DigiPay derives receive address `i` as follows:

1. Take the non-hardened child `0/i`.
2. Compute the Taproot output key: `PubKey.TaprootInternalKey.GetTaprootFullPubKey().OutputKey`.
3. Encode it with `DigiDollarAddressValidator.Encode(outputKey, network)`.

The new class `DigiDollarAccountKey` in `DigiByte.Crypto/KeyGeneration` does both parsing and derivation.

**Rationale**:

- `DigiDollarKeyDerivation` (`src/DigiByte.Crypto/KeyGeneration/DigiDollarKeyDerivation.cs:79-122`) only takes a mnemonic or a master `ExtKey`. Its address encoding (:30-32) needs only a public key, so the public-only path gives the same addresses.
- A test must prove that `DigiDollarAccountKey(xpub).Address(i)` equals `DigiDollarKeyDerivation(seed).Address(Receive, i)` for both networks (Principle III).

**Alternatives considered**:

- A raw x-only key plus chain code: a new format with no tooling, so rejected.
- Descriptors (`tr(xpub/0/*)`): wallets don't export them yet. A later version can accept them.

## R2. Validating the pasted key

**Decision**: DigiPay refuses a pasted key in each of these cases, with these reasons:

| Case | Reason |
|---|---|
| Private key (`xprv`/`tprv` version bytes, or anything that parses as `ExtKey`) | `private_key`. The value is never stored, logged or echoed back. |
| Bad base58 or checksum, or wrong length | `malformed` |
| Version bytes of the other network | `wrong_network`. Strict, with no fallback, unlike DGB's `HdKeyDerivation.ParseXpub` (`src/DigiByte.Crypto/KeyGeneration/HdKeyDerivation.cs:318`), which tries every network. |
| Depth ≠ 3 | `not_account_key`. A master or address-level key would give the wrong addresses. |

A DGB account xpub (`m/84'/20'/0'`) has the same version bytes and depth, so it can't be told apart. The first-address check catches it (spec FR-003).

**Rationale**: strict network matching closes the gap the DGB parser leaves open. The depth check is cheap and catches the most likely wrong pastes.

**Alternatives considered**: tightening DGB xpub parsing as well. That's out of scope, so it's noted as a follow-up.

## R3. Making sure the merchant did the first-address check

**Decision**:

1. `POST /v1/pay/stores/{id}/dd-key/preview` returns the derived first address (index 0) without saving anything.
2. The save (`PUT /v1/pay/stores/{id}/dd-key`) must echo that address back as `confirmFirstAddress`.
3. The server re-derives index 0 and refuses the save with `first_address_mismatch` unless the two match.

**Rationale**: this makes FR-003 enforceable and testable rather than only a UI step. Index 0 is only a fingerprint: it may also go to a payment later, which is harmless.

**Alternatives considered**: a UI-only checkbox. That can't be tested at the API level, and API clients would skip it.

## R4. Shared index sequence per key

**Decision**:

- A new table, `DdKeySequences`, has one row per distinct (network, key) and holds `NextIndex`. Its id is a SHA-256 of the canonical key string, so the raw key isn't duplicated as a lookup column.
- An index is allocated with one atomic `UPDATE … SET NextIndex = NextIndex + 1 RETURNING NextIndex - 1` (PostgreSQL), with a transaction-serialised fallback for SQLite in tests.
- Each allocated address is recorded in `DdAddresses` (key id, index, address, kind, store, session or option), which has a unique (key id, index) and a unique address.
- Donation addresses take the next index, the first time they're asked for, and are kept on the store (`DdDonationAddress`).

**Rationale**:

- It answers clarification Q4: stores with the same key share one sequence, and putting an old key back continues where it stopped.
- The unique constraints make reuse impossible even if allocation has a bug.

**Alternatives considered**:

- A per-store `NextAddressIndex`, as DGB does: collides between stores.
- Scanning the explorer for the highest used index: slow, and depends on the explorer, which FR-010 forbids.

## R5. Watching DD payments on the explorer

**Decision**:

- `InvoiceMonitor` replaces `DigiDollarNodeClient.GetBalanceAsync` / `GetFirstReceiveTxidAsync` with `EsploraDigiDollarApi.GetHistoryAsync([address])`.
- **Amount received**: the sum of `DdOutputs` paying the address over all its transactions, which is total received, not current balance.
- **Confirmations**: the confirmations of the transaction that took the received total to the amount due. A payment counts as confirmed at 6, as for DGB.
- **`PaidTxid`**: the first transaction paying the address.
- The buyer-choice paths (`ReceivedOnAsync`, extra-payment checks) use the same function.
- Polling stays on the monitor's existing 10-second loop (`src/DigiByte.Pay.Api/Services/InvoiceMonitor.cs:16`), checking only open, late-window or confirming DD addresses, as DGB does.

**Rationale**:

- Today's node path reads the current balance (`InvoiceMonitor.cs:355-425`), which drops if the merchant spends before DigiPay checks. That's harmless with a DigiPay-owned wallet, but wrong when the merchant owns the key. History is spend-proof.
- Confirmations were approximated (confirmed ≥ due → 6). Now they are real.
- The request volume matches what the DGB monitor already sends to the same explorer: one address listing per open address per tick, plus a transaction fetch for each transaction found.

**Alternatives considered**:

- `GetUtxosAsync`: unspent-only, so it has the same spend problem.
- Per-transaction status polling: more requests and no benefit.

## R6. Availability and outages

**Decision**:

- `DdAvailability` calls `EsploraDigiDollarApi.GetHealthAsync` for the store's network. DD counts as available only if both `Available` and `DeploymentActive` are true.
- The result stays cached for 30 s, with a 3 s timeout, as today.
- While DD is unavailable, the monitor skips DD addresses for that network in the tick, with no error and no status change, and tries again when it's available. Payments that expire during an outage are still checked in the 3-day late window (FR-014), so SC-005 holds.
- Creating DD payments and options never calls the explorer (FR-010). The old 3-second cap in `SessionOptionService.TryCreateAtCreationAsync` and the `digidollar_node_unavailable` 503 go away.
- The checkout's `ddAvailable`/`ddReason` keeps its shape. The reason `node_unavailable` is renamed `dd_service_unavailable`.

**Rationale**: keeps the Principle V behaviour already designed in spec 033, with the explorer in the node's place.

## R7. One explorer client per network

**Decision**:

- `DdChainProvider` holds two `EsploraDigiDollarApi` instances, one with `isTestnet: () => false` and one with `() => true`. Each is chosen by the store's network, like `ChainServiceProvider`.
- Configuration:
  - `DigiPay:DigiDollar:EsploraMainnetUrl` (default `https://digiexplorer.info/api`)
  - `DigiPay:DigiDollar:EsploraTestnetUrl` (default `https://digiexplorer.info/testnet/api`)
  - env `DIGIPAY_DD_ESPLORA_MAINNET_URL` / `_TESTNET_URL`
- The `DdNode` section and `NODEAPI_*` variables are removed.

**Rationale**: `EsploraDigiDollarApi.GetTxStatusAsync` uses one network per instance (`CurrentBase`), so mixing networks in one instance would be wrong. With one explorer and no fallback (clarification Q3), there's no explorer list.

## R8. Testnet coin type

**Decision**: DigiPay doesn't care about the coin type. It gets the account key already derived. The wallet exports whatever account it really uses for DD: `m/86'/20'/0'` on mainnet, and `m/86'/1'/0'` on testnet today (`DigiDollarKeyDerivation.cs:72-73, 93`).

**Rationale**: the export and the wallet's own scanning must use the same account, or the merchant's wallet won't see the DD. The spec's "testnet coin type" assumption is satisfied either way.

## R9. Wallet export screen

**Decision**:

- The new page `/settings/digidollar-key` mirrors `src/DigiByte.Web/Pages/ExportXpub.razor`. It shows:
  - the account key, with a copy button
  - the first DD receive address (`0/0`)
  - the "can see, can't spend, shows all DD in this account" text (FR-022)
  - a link to the deeper DD scan
- The key comes from `WalletService.GetDigiDollarAccountKeyAsync()`, which calls the new `DigiDollarKeyDerivation.GetAccountExtPubKey()`.
- Watch-only wallets have no DD account (the DD session is created only for seed wallets, `WalletService.cs:242-248`). The page explains that the key is only available in a wallet with its recovery phrase.

## R10. Deeper DD scan in the wallet

**Decision**:

- `DigiDollarService` takes a gap-limit argument (default 20, deep 500) for the receive chain.
- `Recovery.razor` gets a "Look further for DigiDollar" action next to the existing receive search. It runs one scan with gap 500 and keeps any addresses it finds in use.
- The DD key screen links to it.

**Rationale**:

- It's clarification Q2. `WalletService.DeepScanGapLimit = 500` already exists for DGB, but only behind `RestoreViaGatewayAsync(deep)`, with no UI. The user-facing DGB "look further" is the receive search on `/recovery` (`Recovery.razor:170-247`), so DD goes in the same place.
- The DD scan loop (`DigiDollarService.cs:165-223`) already scans in gap-sized steps, so only the limit changes.

## R11. The Dashboard warning when no DD has been paid for a long run of addresses

**Decision**:

- The store's DD card shows a warning when the key's sequence has given out more than 500 addresses since the highest-index address that received DD.
- "Received" comes from `DdAddresses.FirstPaidAt`, set by the monitor.
- The count is computed for the key, across stores, since the wallet scans by key.

## R12. Removing spec 034

**Decision**:

- Delete the code, tests and migration listed below from PR #131's branch.
- Replace `AddDdPayouts` with a new migration, `AddDdMerchantKeys`. Nothing has shipped, so the migration chain on main only ever gains `AddDdEverywhere` and `AddDdMerchantKeys`.
- Keep `DdAddress`, reshaped (R4).
- Mark spec 034 as superseded, and spec 033 as changed by spec 035, at the top of each.
- Delete `specs/034-digipay-dd-payouts/constitution-amendment.md`'s instructions by marking them as not applied.

**What to remove** (from the code survey):

| Area | Files |
|---|---|
| API data | `DdLedgerEntry`, `DdPayout`/`DdPayoutItem`, `PayoutAddressChange`, `StepUpGrant`, `MerchantDdSettings`, `OperatorSetting`, and the `PayStore` `DdPayout*` fields with `IDdPayoutOwner` and `DdPayoutAddressRequiredExempt`. Also `WebhookDelivery.PayoutId`. |
| API services | everything in `Services/Payouts/*` except what `DdAddress` needs. |
| API endpoints | `PayoutsEndpoints.cs`, the payout parts of `OperatorEndpoints.cs`, and `SessionPayoutInfo` use in `PaymentsEndpoints`. |
| API store delete | `StoreDdRetirement` use in `StoresEndpoints`, so a store with DD on deletes normally. |
| Webhooks | the payout events in `WebhookDispatcher`. |
| Node client | `DigiDollarNodeClient`, and its DI in `Program.cs`. |
| Dev sign-in | the payout seeding in `DevSignInEndpoints`. |
| Pay.Web | `DdPayoutsPanel`, `PayoutAlertsBanner`, `PayoutAddressRules`, the Dashboard payout and `dd_held` flows, the payout link in `SessionDetail`, the `ApiError` payout reasons, and the payouts entry in `DashboardSections`. |
| Tests | `PayoutAddressTests`, `PayoutHarness`, `PayoutLedgerTests`, `PayoutSendTests`, `DdPayoutsPanelTests`, and the payout parts of the Dashboard/PosTill/SessionDetail tests and `PayTestHost`. |
| E2E | `fake-nodeapi.mjs` becomes `fake-esplora.mjs`, serving the Esplora paths `EsploraDigiDollarApi` calls (`/address/{a}/txs`, `/tx/{id}`, `/tx/{id}/status`, `/blocks/tip/height`, and the health paths), with real Taproot DD addresses. |

**What 033 keeps, rewired**:

- explicit DD sessions, POS, links and one-off payments
- buyer choice
- the DD donation address
- the surfaces switch
- DD dashboards, totals and CSV

Turning DD on now needs the key (409 `dd_key_required`) instead of a payout address.

## R13. Dev and E2E seeding

**Decision**:

- The dev sign-in seeds the dev store with the testnet DD account key derived from the existing test mnemonic at `m/86'/1'/0'`.
- DigiDollar is turned on for the dev store, with no payout address.
- The POS E2E checks that the DD address shown starts with `TD` and equals the next derived address.
