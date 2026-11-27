---
description: "Tasks for spec 035: DigiDollar on the merchant's own keys, without a node"
---

# Tasks: DigiDollar on the merchant's own keys, without a node

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Branch**: `feature/digipay-dd-buyer-choice` (PR #131). Spec 034's code is removed here, before merge.

**Tests**: included.

- The constitution (Principle III) requires tests first for `DigiByte.Crypto` and `DigiByte.Wallet` changes, with negative paths.
- Pay.Api and Pay.Web keep their existing test suites green.

**Format**: `- [ ] T### [P?] [US#?] Description with file path`

**Why spec 034's removal is in Foundational, not US5**: the 034 code (payouts, ledger, node sends) depends on `DigiDollarNodeClient` and on `PayStore` payout columns that this design removes. Nothing builds until it's gone. US5 keeps the wording, docs and the checks that no custody remains.

## Phase 1: Setup

- [ ] T001 Add a "Superseded by spec 035" note at the top of `specs/034-digipay-dd-payouts/spec.md` and `specs/034-digipay-dd-payouts/plan.md`. Mark `specs/034-digipay-dd-payouts/constitution-amendment.md` "Not applied: superseded by spec 035 (DigiPay holds no DigiDollar)".
- [ ] T002 [P] Add a "Changed by spec 035" note at the top of `specs/033-digipay-dd-everywhere/spec.md`. It should list:
  - addresses now come from the store's key
  - availability comes from explorer health
  - turning DD on needs the key, not a payout address
- [ ] T003 [P] Add the `DigiPay:DigiDollar` settings to `src/DigiByte.Pay.Api/appsettings.json` and remove the `DdNode` section:
  - `EsploraMainnetUrl` = `https://digiexplorer.info/api`
  - `EsploraTestnetUrl` = `https://digiexplorer.info/testnet/api`

## Phase 2: Foundational (blocks every story)

### DD address derivation from a public key (Crypto, test-first)

- [ ] T004 [P] Write failing tests in `tests/DigiByte.Crypto.Tests/DigiDollarAccountKeyTests.cs`:
  - **Matches the seed path**: `DigiDollarAccountKey.Parse(xpub, network).Address(i)` equals `DigiDollarKeyDerivation(seed).GetAddress(Receive, i)` for i = 0, 1 and 19, on mainnet (coin 20) and testnet (coin 1), from the test mnemonic.
  - **Refusals**: `ParseResult` reasons `malformed` (bad checksum, truncated, empty, random text), `private_key` (xprv and tprv), `wrong_network` (tpub on mainnet and xpub on testnet) and `not_account_key` (depth 0 master key and depth 4 or 5 keys).
  - **Fingerprint**: the `KeyId` is SHA-256 of `network + ":" + canonical key`, and is stable.
- [ ] T005 Add `GetAccountExtPubKey()` to `src/DigiByte.Crypto/KeyGeneration/DigiDollarKeyDerivation.cs`. It returns the neutered `ExtPubKey` at `m/86'/coin'/0'`. Also add a test to `tests/DigiByte.Crypto.Tests/DigiDollarAccountKeyTests.cs` that its `ToString(network)` parses back to the same addresses.
- [ ] T006 Implement `src/DigiByte.Crypto/KeyGeneration/DigiDollarAccountKey.cs` (research R1 and R2). It must:
  - parse strictly by version bytes for the given network, with no fallback
  - reject private extended keys without putting the input in any exception message
  - require depth 3
  - derive with `Derive(0).Derive(i)`, then Taproot `GetTaprootFullPubKey().OutputKey`, then `DigiDollarAddressValidator.Encode`
  - expose `Canonical`, `KeyId`, `Fingerprint` (the first 8 hex characters of KeyId) and `Address(int index)`

  T004 must pass.

### Explorer client and test fake

- [ ] T007 [P] Create `src/DigiByte.Pay.Api/Services/DdChainProvider.cs` (R7). It holds two `EsploraDigiDollarApi` instances (mainnet with `isTestnet: () => false`, testnet with `() => true`) built from the `DigiPay:DigiDollar` URLs, with env overrides `DIGIPAY_DD_ESPLORA_MAINNET_URL` and `DIGIPAY_DD_ESPLORA_TESTNET_URL`. Provide `For(string network)` and register it in `src/DigiByte.Pay.Api/Program.cs` with the named HttpClient "DigiPayDd".
- [ ] T008 [P] Create `tests/DigiByte.Pay.Api.Tests/FakeDdExplorer.cs`, an `HttpMessageHandler` serving the Esplora paths `EsploraDigiDollarApi` calls: `/address/{a}/txs`, `/address/{a}/txs/chain/{last}`, `/tx/{id}`, `/tx/{id}/status`, `/blocks/tip/height` and the health paths. It should offer:
  - `Pay(address, cents, confirmations)`
  - `Confirm(txid, n)`
  - `Down` (all requests fail)
  - a `Requests` log

### Data model (data-model.md)

- [ ] T009 Update `src/DigiByte.Pay.Api/Data/PayStore.cs`:
  - Add `DdAccountKey` (string?, "≤ 200"), `DdKeyId` (string?, "64 hex") and `DdAccountKeySetAt` (DateTime?).
  - Remove `IDdPayoutOwner`, every `DdPayout*` field and `DdPayoutAddressRequiredExempt`.
- [ ] T010 [P] Create `src/DigiByte.Pay.Api/Data/DdKeySequence.cs` with `Id` (64 hex, PK), `Network`, `NextIndex` ("only ever increases, starts at 0") and `CreatedAt`.
- [ ] T011 [P] Reshape `src/DigiByte.Pay.Api/Data/DdAddress.cs` to have:
  - `Address` (PK), `DdKeyId`, `Index`
  - `Kind`: `payment`, `option` or `donation`
  - `StoreId`, `SessionId?`, `CreatedAt`, `FirstPaidAt?`

  Remove the ledger links.
- [ ] T012 Update `src/DigiByte.Pay.Api/Data/DigiPayDbContext.cs`:
  - Add the DbSets `DdKeySequences` and `DdAddresses`, with a unique (`DdKeyId`, `Index`) and FK `DdKeyId`.
  - Remove the DbSets and config for `DdLedgerEntry`, `DdPayout`, `DdPayoutItem`, `PayoutAddressChange`, `StepUpGrant`, `MerchantDdSettings` and `OperatorSetting`.
  - Remove `WebhookDelivery.PayoutId` from `src/DigiByte.Pay.Api/Data/WebhookDelivery.cs`.

  Delete these entity files: `DdLedgerEntry.cs`, `DdPayout.cs`, `PayoutAddressChange.cs`, `StepUpGrant.cs`, `MerchantDdSettings.cs` and `OperatorSetting.cs`.

### Remove spec 034 and the node client

- [ ] T013 Delete these files:
  - every file in `src/DigiByte.Pay.Api/Services/Payouts/`
  - `src/DigiByte.Pay.Api/Endpoints/PayoutsEndpoints.cs`

  Then remove their DI, the operator key, the payouts worker, the SQLite shim, the DdAddress backfill and `MapPayoutsEndpoints` from `src/DigiByte.Pay.Api/Program.cs`.

  In `src/DigiByte.Pay.Api/Services/DigiDollarNodeClient.cs`, delete only the spec 034 methods and records (`GetHealthAsync`, `GetWalletBalanceAsync`, `ListReceivesAsync`, `SendAsync`, `ListSendsAsync`, `GetTxStatusAsync`). Keep `GenerateAddressAsync`, `GetBalanceAsync`, `GetFirstReceiveTxidAsync` and `IsHealthyAsync`: they still have callers until T029, T031 and T044, and the client is deleted in T045.
- [ ] T014 Remove the remaining 034 code:
  - the payout routes (pause, resume, health) from `src/DigiByte.Pay.Api/Endpoints/OperatorEndpoints.cs`
  - `SessionPayoutInfo` use from `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs`
  - `StoreDdRetirement` and the payout-address network check from `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs`
  - the payout fields from `src/DigiByte.Pay.Api/Endpoints/MerchantMeEndpoints.cs`
  - the payout events from `src/DigiByte.Pay.Api/Services/WebhookDispatcher.cs`
- [ ] T015 Delete `src/DigiByte.Pay.Api/Migrations/20261127011500_AddDdPayouts.cs` and its `.Designer.cs`. Add the migration `AddDdMerchantKeys` with `dotnet ef migrations add AddDdMerchantKeys` in `src/DigiByte.Pay.Api/Migrations/` (data-model.md "Migration"). It should:
  - add the store columns
  - create `DdKeySequences` and `DdAddresses`
  - set `DigiDollarEnabled = false` and clear `DdDonationAddress` where `DdAccountKey` is null

  Update `DigiPayDbContextModelSnapshot.cs` and the SQLite shim in `src/DigiByte.Pay.Api/Program.cs`.
- [ ] T016 Delete these Pay.Web files:
  - `src/DigiByte.Pay.Web/Components/Shared/DdPayoutsPanel.razor`
  - `src/DigiByte.Pay.Web/Components/Shared/PayoutAlertsBanner.razor`
  - `src/DigiByte.Pay.Web/Components/Pages/Merchant/PayoutAddressRules.cs`

  Then remove their uses, and the payouts / `dd_held` / payout-address flows, from:
  - `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor`
  - `src/DigiByte.Pay.Web/Components/Pages/SessionDetail.razor`
  - `src/DigiByte.Pay.Web/Components/Pages/Merchant/DashboardSections.cs`
  - `src/DigiByte.Pay.Web/Components/Shared/Ui/ApiError.cs`
- [ ] T017 Delete these tests:
  - `tests/DigiByte.Pay.Api.Tests/PayoutAddressTests.cs`, `PayoutHarness.cs`, `PayoutLedgerTests.cs` and `PayoutSendTests.cs`
  - `tests/DigiByte.Pay.Web.Tests/DdPayoutsPanelTests.cs`

  Remove payout setup from `tests/DigiByte.Pay.Api.Tests/PayTestHost.cs` (`DigiPay:Payouts:WorkerEnabled`), and the payout cases in `tests/DigiByte.Pay.Web.Tests` Dashboard, PosTill and SessionDetail tests.
- [ ] T018 Replace `FakeDdNode` with `FakeDdExplorer`:
  - Wire `FakeDdExplorer` (T008) into `tests/DigiByte.Pay.Api.Tests/PayTestHost.cs` as the "DigiPayDd" handler, and delete `tests/DigiByte.Pay.Api.Tests/FakeDdNode.cs`.
  The DD routes compile, but DD isn't usable in tests until US2: the node client is still wired, and the fake explorer doesn't serve node paths. The existing DD Pay.Api tests (`BuyerChoiceSessionTests`, `DdOptionEndpointTests`, `InvoiceMonitorBuyerChoiceTests`, `DonationOptionsTests`, `DdSurfacesGateTests`, `SessionsCsvDdTests`, `PosShiftTotalsDdTests`) stay red until T032. Only the Crypto tests and the Pay.Api tests not about DD are expected to pass at this checkpoint.

**Checkpoint**: the solution builds, no spec 034 code is left, the Crypto tests pass, and the Pay.Api tests not about DD pass.

## Phase 3: User Story 1, connect the store's DigiDollar key (P1) 🎯 MVP

**Goal**: a merchant pastes their DD key, sees the first address, confirms it, and can then turn DD on.

**Independent test**:

1. Preview and save a testnet key, then turn DD on.
2. Paste a mainnet key, an `xprv` or a depth-0 key, and see each refused with its reason.
3. Turn DD on with no key and get `dd_key_required`.

- [ ] T019 [P] [US1] Write API tests in `tests/DigiByte.Pay.Api.Tests/DdKeyTests.cs`, against contracts/stores-api.md:
  - **S1 preview**: valid key, then each `dd_key_invalid` reason. Also assert that the response body and logs never contain the pasted `tprv`.
  - **S2 save**: `first_address_mismatch` (409). Success sets `ddKey.fingerprint`/`firstAddress` and writes the audit event `store.dd_key_set`.
  - **S3 remove**: sets `digiDollarEnabled` false and writes the audit event `store.dd_key_removed`.
  - **S4**: the DTO never contains the raw key.
  - **S5**: `dd_key_required` (409).
  - **Access**: a store-bound POS key gets 403.
- [ ] T020 [US1] Implement S1, S2 and S3 (`POST …/dd-key/preview`, `PUT …/dd-key`, `DELETE …/dd-key`) in `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs` using `DigiDollarAccountKey`. Owner-only, and not `AllowPosKey`.
  - PUT creates or reuses the `DdKeySequence` row (`Id = KeyId`) and clears `DdDonationAddress` when the key changes.
  - Audit: `store.dd_key_set`, `store.dd_key_replaced` and `store.dd_key_removed`, with the fingerprint only.
  - Never log the request body.
  - Then, in `tests/DigiByte.Pay.Api.Tests/BuyerChoiceTestKit.cs`, change `DdStore(factory)` to set a real testnet DD account key (from the test mnemonic at `m/86'/1'/0'`) through this S2 route, instead of seeding a payout address.
- [ ] T021 [US1] In `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs`, replace the `payout_address_required` check on `digiDollarEnabled: true` with 409 `dd_key_required` (S5).
- [ ] T022 [US1] Add the `ddKey` object to the store DTO in `src/DigiByte.Pay.Api/Endpoints/MerchantMeEndpoints.cs` and the store GET/PATCH responses: `set`, `network`, `fingerprint`, `firstAddress`, `setAt`, `unpaidRun`, `unpaidRunWarning`. For now, `unpaidRun` is 0 and `unpaidRunWarning` is false.
- [ ] T023 [P] [US1] Write bUnit tests in `tests/DigiByte.Pay.Web.Tests/DdKeyCardTests.cs` for this flow:
  1. Paste, preview and show the first address.
  2. "Matches my wallet" confirm, then save.
  3. Each refusal reason shows plain text.
  4. The DD toggle is disabled with "Add your DigiDollar key first" when there's no key.
  5. Remove asks for confirmation.
- [ ] T024 [US1] Build the DigiDollar key card in `src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor` (or a new `src/DigiByte.Pay.Web/Components/Shared/DdKeyCard.razor`). It covers:
  - a paste box, then preview, then the first address shown large with "Open your DigiByte Wallet → Settings → DigiDollar key for DigiPay and check this address matches"
  - Confirm and save, then the fingerprint and first address shown
  - Replace and Remove
  - the toggle gated on the key
- [ ] T025 [P] [US1] Add `dd_key_invalid` (with each reason), `first_address_mismatch` and `dd_key_required` with plain messages in `src/DigiByte.Pay.Web/Components/Shared/Ui/ApiError.cs`. The `private_key` message: "That's a private key. Never share it. Paste the public key from your wallet's DigiDollar key screen."
- [ ] T026 [US1] Seed the dev store in `src/DigiByte.Pay.Api/Endpoints/DevSignInEndpoints.cs` with the testnet DD account key from the test mnemonic (`m/86'/1'/0'`) and DD on, with no payout address. Update `tests/DigiByte.Pay.Api.Tests/DevSignInTests.cs`. Derive the seeded key with `DigiDollarKeyDerivation`'s own testnet coin type rather than hard-coding `1`. That coin type is still marked "to confirm" in the code (`DigiDollarKeyDerivation.cs:72-73`), and if it changes, the seed must follow, along with any testnet keys saved before then.

**Checkpoint**: keys can be set, checked and removed, and DD can be turned on only with a key.

## Phase 4: User Story 2, DigiDollar goes straight to the merchant (P1)

**Goal**: every DD address comes from the store's key. Payments are tracked from the explorer.

**Independent test**: create a DD payment, let `FakeDdExplorer` pay it, and see it go from seen to paid to confirmed. Stores sharing a key never collide.

- [ ] T027 [P] [US2] Write tests in `tests/DigiByte.Pay.Api.Tests/DdAddressSequenceTests.cs`:
  - Sequential payments get indexes 0, 1, 2… and those addresses equal `DigiDollarAccountKey.Address(i)`.
  - Two stores with the same key get distinct addresses from one sequence.
  - Replacing a key and then putting the old key back continues its sequence.
  - 8 concurrent creations give 8 distinct indexes.
  - The donation address takes the next index once and is the same after that.
  - A creation makes no request to `FakeDdExplorer`, even when `Down` is set (FR-010).
- [ ] T028 [US2] Implement `src/DigiByte.Pay.Api/Services/DdAddressAllocator.cs` (R4).
  - `AllocateAsync(store, kind, sessionId?)` atomically increments `DdKeySequences.NextIndex`: `UPDATE … RETURNING` on Npgsql, and a serialised transaction on SQLite.
  - It derives the address with `DigiDollarAccountKey`, inserts a `DdAddress` row and returns the address.
  - Register it in `src/DigiByte.Pay.Api/Program.cs`.
- [ ] T029 [US2] Replace the node address calls with `DdAddressAllocator`:
  - `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs` (DD session, kind `payment`)
  - `src/DigiByte.Pay.Api/Services/SessionOptionService.cs` (`TryCreateAtCreationAsync` and `GetOrCreateDdOptionAsync`, kind `option`; remove the 3-second cap and the `digidollar_node_unavailable` 503)
  - `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs` (donation-address `?currency=DD`, kind `donation`, kept on `DdDonationAddress`)
- [ ] T030 [P] [US2] Write tests in `tests/DigiByte.Pay.Api.Tests/InvoiceMonitorDdExplorerTests.cs`, calling `InvoiceMonitor.RunOnceAsync` with `FakeDdExplorer`:
  - **Status**: 0-conf goes to seen, the full amount to paid, 6 confirmations to confirmed, and a partial amount to underpaid.
  - **History, not balance**: a later spend from the address doesn't lower the received total.
  - **PaidTxid**: it's the first paying transaction.
  - **Late payment**: a payment within 3 days of expiry goes to paid.
  - **First paid**: `DdAddress.FirstPaidAt` is set.
  - **Buyer choice**: a DD arrival locks the payment to DD. DD arriving on a payment locked to DGB raises `session.extra_payment`.
  - **Key replaced (FR-005)**: a DD payment opened before the store's key is replaced (S2 with a new key) still goes to paid on its original address.
  - **Reorganised out**: a paid payment whose transaction disappears from the explorer's history goes back to its earlier state, as DGB payments do.
  - **Overpaid**: more DD than due goes to paid, with the full received amount recorded.
  - **Key removed (FR-004)**: a DD payment opened before the key is removed (S3) still goes to paid and confirmed, while `digiDollarEnabled` is false.
- [ ] T031 [US2] Rewrite the DD paths in `src/DigiByte.Pay.Api/Services/InvoiceMonitor.cs` to use `DdChainProvider.For(network).GetHistoryAsync` (R5):
  - `InspectDdSessionAsync`
  - `ReceivedOnAsync`
  - the extra-payment check

  Received is the sum of `DdOutputs` to the address. Confirmations come from the transaction that completed the amount due. Set `FirstPaidAt`.
- [ ] T032 [US2] Update the existing DD tests to the allocator and explorer fakes:
  - `tests/DigiByte.Pay.Api.Tests/DdOptionEndpointTests.cs`: replace the `DdNode.Addresses` and `address/new` asserts with derived-address asserts, and replace `A_down_node_is_503_and_makes_no_option` with the `dd_service_unavailable` case (C1).
  - `BuyerChoiceSessionTests.cs`, `InvoiceMonitorBuyerChoiceTests.cs`, `DonationOptionsTests.cs`, `DdSurfacesGateTests.cs`, `SessionsCsvDdTests.cs` and `PosShiftTotalsDdTests.cs`.
- [ ] T033 [P] [US2] Replace `tests/DigiByte.Pay.E2E/scripts/fake-nodeapi.mjs` with `tests/DigiByte.Pay.E2E/scripts/fake-esplora.mjs`. It serves the T008 paths, plus `/coingecko` and `/fx`. Point `tests/DigiByte.Pay.E2E/servers.mjs` and `playwright.config.mjs` at it, passing `--DigiPay:DigiDollar:EsploraTestnetUrl`.
- [ ] T034 [US2] Update `tests/DigiByte.Pay.E2E/tests/pos-dd.spec.mjs` so the DD address shown matches `/^TD1/` and is a valid DD address, and update its header comment (no payout address). Run it with `PW_CHANNEL=msedge`.

**Checkpoint**: DigiDollar works end to end on the merchant's key.

## Phase 5: User Story 3, show the DigiDollar key in the wallet (P2)

**Goal**: the wallet shows and copies the DD account key and its first address, and can look further for DD.

**Independent test**: the key screen's key, pasted into DigiPay's preview, gives the same first address. The deep scan finds DD at receive index 300.

- [ ] T035 [P] [US3] Write tests in `tests/DigiByte.Wallet.Tests/DigiDollarKeyExportTests.cs`:
  - For a seed wallet, `WalletService.GetDigiDollarAccountKeyAsync()` returns the key and first address, matching `DigiDollarAccountKey.Parse(key).Address(0)`, on mainnet and testnet.
  - It returns null for watch-only and multisig wallets.
- [ ] T036 [US3] Implement `GetDigiDollarAccountKeyAsync()` in `src/DigiByte.Wallet/Services/WalletService.cs` using `DigiDollarKeyDerivation.GetAccountExtPubKey()` from the unlocked session's DD derivation (W2).
- [ ] T037 [P] [US3] Write tests in `tests/DigiByte.Wallet.Tests/DigiDollarDeepScanTests.cs`:
  - With DD paid only at receive index 300, a normal scan (gap 20) misses it and `ScanAsync(receiveGapLimit: 500)` finds it.
  - After the deep scan, a normal scan keeps the found address.
- [ ] T038 [US3] Add the `receiveGapLimit` parameter (default 20) to the scan in `src/DigiByte.Wallet/Services/DigiDollarService.cs`, used for the receive chain only. Persist the highest used receive index found so normal scans start from it (W3).
- [ ] T039 [P] [US3] Create `src/DigiByte.Web/Pages/ExportDigiDollarKey.razor` at `/settings/digidollar-key`, mirroring `src/DigiByte.Web/Pages/ExportXpub.razor`. It shows the key with Copy, the first DD address, the W1 text and a link to the deeper scan. It shows the watch-only message when the key is null.
- [ ] T040 [US3] Link "DigiDollar key for DigiPay" from `src/DigiByte.Web/Pages/Settings.razor`, next to the xpub item.
- [ ] T041 [US3] Add the "Look further for DigiDollar" card to `src/DigiByte.Web/Pages/Recovery.razor`. It runs the deep scan with progress and the result text (W3), and leaves the DGB receive search unchanged.
- [ ] T042 [P] [US3] The wallet web app (`DigiByte.Web`) has no bUnit test project, so its pages are covered by the service tests in T035 and T037 and checked by hand. Add the hand checks to [quickstart.md](quickstart.md) "Manual, on testnet":
  - The seed wallet shows the key and first address, and Copy works.
  - A watch-only wallet shows the "only available in a wallet restored from its recovery phrase" message.
  - "Look further for DigiDollar" on `/recovery` shows its progress and result.

**Checkpoint**: merchants can get their key from the wallet, and the wallet finds DD paid to far-off addresses.

## Phase 6: User Story 4, when the explorer can't be reached (P2)

**Goal**: DD waits during outages and catches up afterwards. DGB is unaffected.

**Independent test**: set `FakeDdExplorer.Down`, pay a DD payment, clear `Down`, and the payment goes to paid. DGB payments still work while it's down.

- [ ] T043 [P] [US4] Write tests in `tests/DigiByte.Pay.Api.Tests/DdExplorerOutageTests.cs`:
  - While down, `GET /v1/pay/sessions/{id}` gives `ddAvailable: false` and `ddReason: "dd_service_unavailable"`.
  - `options/dd` gives 409 `dd_option_unavailable` / `dd_service_unavailable`.
  - Creating an explicit DD payment still succeeds.
  - DD payments stay pending without errors.
  - A DGB payment on the same store still goes to paid.
  - After recovery, a DD payment paid during the outage, including one past its expiry within 3 days, goes to paid on the next tick.
- [ ] T044 [US4] Change `src/DigiByte.Pay.Api/Services/DdAvailability.cs` to use `DdChainProvider.For(network).GetHealthAsync`. DD is available only if `Available && DeploymentActive`. Keep the cache (30 s) and the 3-second timeout, and make it per network. Rename the reason `node_unavailable` to `dd_service_unavailable` in `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs` and `src/DigiByte.Pay.Api/Services/SessionOptionService.cs`.
- [ ] T045 [US4] In `src/DigiByte.Pay.Api/Services/InvoiceMonitor.cs`, skip DD addresses for a network whose `DdAvailability` is false during that tick, with no status change and no error log per address, logging one warning per outage instead. Make sure DGB processing never awaits DD health.

  Then delete `src/DigiByte.Pay.Api/Services/DigiDollarNodeClient.cs`. Its last callers are gone after T029, T031 and T044. Also remove its DI and the `DdNode` options from `src/DigiByte.Pay.Api/Program.cs`, and the `NODEAPI_BASE_URL` / `NODEAPI_WRITE_KEY` handling.
- [ ] T046 [P] [US4] Update the unavailable copy in `src/DigiByte.Pay.Web/Components/Pages/Checkout.razor`, `Donate.razor` and `Pos.razor` (and their tests in `tests/DigiByte.Pay.Web.Tests`) to "DigiDollar is temporarily unavailable". Show the merchant-side note "DigiDollar confirmations may be delayed" on the Dashboard DD card when DD is unavailable (US4 scenario 3).

**Checkpoint**: outages are handled as the spec describes.

## Phase 7: User Story 5, no custody and no payouts (P3)

**Goal**: no wording or API surface suggests DigiPay holds DD.

**Independent test**: searching the code and docs for payout and custody text finds nothing about DD (SC-006). A store with DD on deletes normally.

- [ ] T047 [P] [US5] Write tests in `tests/DigiByte.Pay.Api.Tests/NoCustodyTests.cs`:
  - Deleting a store with DD on returns 204 (no `dd_held`).
  - `/v1/pay/payouts`, `/v1/pay/stores/{id}/payout-address` and `/v1/pay/operator/payouts/pause` return 404.
  - The webhook event list has no `payout.*` events.
- [ ] T048 [P] [US5] Rewrite `src/DigiByte.Pay.Web/Components/Pages/Merchant/DigiDollarSettingText.cs` (and its tests) to say:
  - DD goes straight to your wallet, like DGB
  - you need your DigiDollar key
  - no node and no payouts
- [ ] T049 [P] [US5] Update the in-app docs in these files:
  - `src/DigiByte.Pay.Web/Components/Pages/Docs.razor`: a "Taking DigiDollar" section covering the key, the first-address check, shared keys, the deeper scan and refunds from your own wallet.
  - `ApiDocs.razor`: the dd-key routes and errors, with the payout routes and events removed.
  - `Features.razor` and `Home.razor`: "non-custodial" now includes DD.
- [ ] T050 [P] [US5] Revert spec 034's custody wording, where it was added, in `SELF_CUSTODY.md`, `docs/SECURITY.md` and `docs/RAILWAY_NODE.md`. In `docs/RAILWAY_NODE.md`, say that DigiPay no longer needs NodeApi for DD.
- [ ] T051 [P] [US5] Update the webhook samples (express, flask and dotnet handlers and their READMEs under `samples/`) and both Postman collections:
  - remove payout and operator requests and events
  - add the dd-key preview, set and remove requests
- [ ] T052 [US5] Search `src/`, `docs/`, `samples/` and `README.md` for these terms and fix any DD custody text left:
  - "payout", "pays out", "holds your DigiDollar"
  - "DigiPay holds", "operator key", "step-up", "DdNode", "NODEAPI_"

## Phase 8: Polish and cross-cutting

- [ ] T053 (This belongs with US2 but sits here because it needs `FirstPaidAt` from T031. It completes the spec's long-unpaid-run edge case and the `unpaidRun` fields of contract S4.) Compute `unpaidRun` for the key and the `unpaidRunWarning` (> 500) in the store DTO (R11) in `src/DigiByte.Pay.Api/Endpoints/MerchantMeEndpoints.cs`. Show the warning on the DD key card with "Run 'Look further for DigiDollar' in your wallet after your next DigiDollar payment". Test it in `tests/DigiByte.Pay.Api.Tests/DdKeyTests.cs`.
- [ ] T054 [P] Update `docs/ARCHITECTURE.md` and `docs/PROCESS_FLOWS.md`: the DD flow is the merchant key, then the derived address, then the explorer, with no node, ledger or payouts.

  Also do FR-020: rewrite the class comment in `src/DigiByte.Pay.Api/Services/DdSurfacesGate.cs`, which says the switch waits for spec 034's payouts. The switch now waits only for this feature to be live. Add a deploy note to `docs/ARCHITECTURE.md` (DigiPay section) to set `DIGIPAY_DD_SURFACES=true` in production once spec 035 ships.
- [ ] T055 [P] Update `CHANGELOG.md`: DigiDollar on your own key, the wallet DigiDollar key screen, the deeper DD scan, and payouts removed (never released).
- [ ] T056 Run `dotnet build` and `dotnet test` for Crypto, Wallet, Pay.Api and Pay.Web tests, the JS tests and the E2E suite. Fix any failures. Note the known unrelated `DigiByte.Wallet.Tests` explorer/timing flakes in the PR if they recur.
- [ ] T057 Mark the 033 and 034 tasks made moot by this spec in `specs/033-digipay-dd-everywhere/tasks.md` (T072 testnet now covered by spec 035's quickstart) and `specs/034-digipay-dd-payouts/tasks.md` (superseded).
- [ ] T058 Testnet walk-through of [quickstart.md](quickstart.md) steps 1–9 (needs a testnet DD wallet and a running deployment, so it's done outside this session).

## Dependencies

- **Setup** (T001–T003) can start at once. T003 comes before T007.
- **Foundational**:
  - T004 → T005 → T006
  - T007, T008, T010 and T011 can run in parallel
  - T009 → T012 → T013 → T014 → T015 → T016 → T017 → T018

  It blocks every story.
- **US1** (T019–T026) only needs Foundational.
- **US2** (T027–T034) needs US1's set-key route (T020) for its test setup (`DdStore`).
- **US3** (T035–T042) needs only T005 and T006, so it can run in parallel with US1, US2 and US4.
- **US4** (T043–T046) needs US2's monitor rewrite (T031).
- **US5** (T047–T052) needs Foundational. Its doc tasks can start any time after US1's routes are final.
- **Polish**: T053 needs T031 (`FirstPaidAt`). T056 comes last before T058.

```text
Setup ─▶ Foundational ─┬─▶ US1 ─▶ US2 ─▶ US4 ─┐
                       ├─▶ US3 ───────────────┼─▶ Polish
                       └─▶ US5 (docs) ────────┘
```

## Parallel examples

- **Foundational**: T004 (Crypto tests), T007 (DdChainProvider), T008 (FakeDdExplorer), T010 and T011 (entities).
- **US1**: T019 (API tests), T023 (bUnit tests) and T025 (ApiError) together, then T020–T022 and T024.
- **US2**: T027 and T030 (tests) and T033 (fake explorer) together.
- **US3**: the wallet work, T035–T042, all runs alongside US1/US2. It touches only `DigiByte.Crypto`, `DigiByte.Wallet` and `DigiByte.Web`.
- **US5**: T048–T051 are separate files.

## Implementation strategy

1. **MVP**: Setup, Foundational and US1. The branch builds without spec 034, and keys can be set and checked.
2. **Taking DD**: add US2. #131 is now non-custodial and node-free.
3. **Merchant tools and resilience**: US3 (wallet) and US4 (outages), which can be done in parallel.
4. **Clean-up**: US5 and Polish, then re-run CI on #131 and update its description to describe spec 033 plus spec 035.
