---

description: "Task list for DigiDollar payouts to merchants"
---

# Tasks: DigiDollar payouts to merchants

**Input**: Design documents in `specs/034-digipay-dd-payouts/`: plan.md, spec.md, research.md, data-model.md, contracts/ (C1–C5), quickstart.md

**Tests**: Included. These services move merchant money, plan.md defines test classes in three test projects, and the constitution's merge gate requires `dotnet test` on every affected project. Tests are written before the code they cover and must fail first.

**Organization**: Tasks are grouped by user story (US1–US6 from spec.md). Shared ledger, node-client and worker scaffolding is in Phase 2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: The user story the task belongs to
- Paths are relative to the repository root. "Pay.Api" means `src/DigiByte.Pay.Api`, and "Pay.Web" means `src/DigiByte.Pay.Web`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm what the node actually returns, and add test scaffolding.

- [ ] T001 Run quickstart Q0 against a testnet node with the `digipay-hot` wallet: call NodeApi `POST /api/digidollar/send` (1.00 DD, comment `test-q0`) and `GET /api/digidollar/transactions?category=send`. Record the raw `senddigidollar` result shape, and whether send entries include `comment`, `address`, `amount` and `time`, in `specs/034-digipay-dd-payouts/research.md` under R1 "Verified". Also record the RPC error code and message `senddigidollar` returns when the wallet's only DD is unconfirmed.
- [ ] T002 [P] Create `tests/DigiByte.Pay.Api.Tests/FakeDdNode.cs`: an `HttpMessageHandler` with scriptable responses and a call log for `/api/digidollar/health`, `/address/new`, `/balance`, `/balance/{addr}`, `/transactions`, `/send`, `/sends` and `/tx/{txid}/status`. Add a `PayFactory.Create` overload in `tests/DigiByte.Pay.Api.Tests/PayTestHost.cs` that registers it with `AddHttpClient<DigiDollarNodeClient>().ConfigurePrimaryHttpMessageHandler(() => fake)`, and sets `NODEAPI_WRITE_KEY` and `DIGIPAY_OPERATOR_KEY` test values (research R13).
- [ ] T003 [P] Draft the separate constitution amendment noted in research R12 (a MINOR change adding a DigiPay custodial-DD clause: hold DD only until payout, publish balances, keep full coverage, protect payout-address changes) as its own PR description in `specs/034-digipay-dd-payouts/constitution-amendment.md`. Don't edit `.specify/memory/constitution.md` in this feature. It belongs in Setup so the amendment can be reviewed before the payout code merges (Principle I asks for custody questions to be escalated before design).
- [ ] T004 [P] Add helpers to `tests/DigiByte.Pay.Api.Tests/PayTestHost.cs`:
  - `CreateSessionToken(merchant)`, which returns a `dps_` token.
  - `GrantStepUp(merchant, sessionId)`, which inserts a valid `StepUpGrant`.
  - `SeedDdSession(store, amountCents, address, status)`.
  - `RunPayoutCycleAsync()`, which resolves `DdPayoutWorker` and calls `RunOnceAsync`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Data model, node client, ledger scanner, worker loop, alerts and generic webhooks. Every story needs these.

**⚠️ CRITICAL**: No user story work can start until this phase is complete.

### NodeApi (contract C4)

- [ ] T005 [P] Write `tests/DigiByte.NodeApi.Tests/DigiDollarSendLookupTests.cs`. It covers:
  - `POST /send` returns `{txid}` whether the RPC returns a bare string or an object with `txid`.
  - It returns 502 `send_result_unparsed` for anything else.
  - `GET /sends?since=&count=` (count ≤ 200) maps `listdigidollartxs` with category `send` to `{items:[{txid,address,amountCents,comment,time,confirmations}]}`.
  - Both routes return 401 without `X-NodeApi-Key`.
  Also extend `tests/DigiByte.NodeApi.Tests/DigiDollarWalletScopedAuthTests.cs` so `/sends` must be classified as wallet-scoped.
- [ ] T006 In `src/DigiByte.NodeApi/Endpoints/DigiDollarEndpoints.cs`, parse the `senddigidollar` result in `POST /send` to `{ "txid": "…" }`. Add `GET /sends` as in contracts/nodeapi.md, and register it in `src/DigiByte.NodeApi/Middleware/WalletScopedRoutes.cs`.

### Data model (data-model.md)

- [ ] T007 [P] Create `Pay.Api/Data/DdAddress.cs`.
  - Fields: `Address` (PK), `MerchantId` (required), `StoreId?`, `Kind`, `SessionId?`, `Network`, `CreatedAt`.
  - `Kind` is `"payment" | "donation"`.
- [ ] T008 [P] Create `Pay.Api/Data/DdLedgerEntry.cs`.
  - Fields: `Id` (`dle_…`), `MerchantId`, `StoreId?`, `Type`, `Source?`, `AmountCents` (long, positive; the sign comes from `Type`), `Txid?`, `Address?`, `SessionId?`, `PayoutId?`, `Confirmations`, `ConfirmedAt?`, `CreatedAt`.
  - `Type`: `credit | debit | debit_reversal | transfer_out | transfer_in | reversal_absorbed`.
  - `Source`: `payment | overpayment | late | extra_refund | donation`.
- [ ] T009 [P] Create `Pay.Api/Data/DdPayout.cs` and `Pay.Api/Data/DdPayoutItem.cs`.
  - `DdPayout` fields: `Id` (`dpo_…`), `MerchantId`, `StoreId?`, `Destination`, `AmountCents` ("At least 100"), `Status`, `WaitReason?`, `FailureCode?`, `FailureReason?`, `Trigger`, `Txid?`, `Confirmations`, `AttemptCount`, `LastAttemptAt?`, `CreatedAt`, `SentAt?`, `ConfirmedAt?`, `FailedAt?`.
  - `Status`: `waiting|sending|checking|sent|confirmed|failed`.
  - `WaitReason`: `node_unavailable|fee_reserve|coverage|operator_paused`.
  - `Trigger`: `auto|daily|threshold|threshold_age|manual|retry|store_delete`.
  - `DdPayoutItem` fields: `PayoutId`, `LedgerEntryId`, `AmountCents`.
- [ ] T010 [P] Create `Pay.Api/Data/PayoutAddressChange.cs`.
  - Fields: `Id` (`pac_…`), `StoreId?`, `MerchantId`, `OldAddress?`, `NewAddress`, `Status`, `RequestedAt`, `EffectiveAt`, `RequestedBySessionId`, `CancelledAt?`, `CancelledBySessionId?`, `AppliedAt?`, `AcknowledgedAt?`.
  - `Status`: `pending → applied | cancelled`.
- [ ] T011 [P] Create `Pay.Api/Data/StepUpGrant.cs` and `Pay.Api/Data/OperatorSetting.cs`.
  - `StepUpGrant` fields: `Id` (`sug_…`), `MerchantId`, `SessionId`, `Purpose` (`payout-address`), `DigiIdAddress`, `CreatedAt`, `ExpiresAt` ("5-minute lifetime"), `ConsumedAt?` ("Single use").
  - `OperatorSetting` is a key/value row (`Key` PK, `Value?`, `UpdatedAt`).
  - Also create `Pay.Api/Data/MerchantDdSettings.cs`, the payout settings for a merchant's DD from deleted stores. `MerchantId` is the PK. It has the same payout fields as `PayStore` (`DdPayoutAddress`, `DdPayoutAddressSetAt`, `DdPayoutSchedule`, `DdPayoutDailyTime`, `DdPayoutTimeZone`, `DdPayoutThresholdCents`, `DdPayoutsAutoPausedAt`, `DdPayoutConsecutiveFailures`, `DdPayoutsPausedByOperatorAt`, `DdPayoutRequestedAt`), with the same rules, and `Network` (string, required), taken from the deleted stores' DD addresses.
- [ ] T012 Extend `Pay.Api/Data/PayStore.cs` with:
  - `DdPayoutAddress` (string?, ≤ 128)
  - `DdPayoutAddressSetAt?`
  - `DdPayoutSchedule` (string ≤ 16, default `"confirmed"`; `confirmed | daily | threshold`)
  - `DdPayoutDailyTime` (TimeOnly?)
  - `DdPayoutTimeZone` (IANA, default `UTC`)
  - `DdPayoutThresholdCents` (long?, "At least 100 and at most 10,000,000")
  - `DdPayoutsAutoPausedAt?`
  - `DdPayoutConsecutiveFailures` (int)
  - `DdPayoutsPausedByOperatorAt?`
  - `DdPayoutRequestedAt?`
  - `DdPayoutAddressRequiredExempt` (bool), which grandfathers stores that already have DD on (FR-031)

  Extend `Pay.Api/Data/WebhookDelivery.cs` with `PayloadJson` (string?) and `PayoutId` (string?, ≤ 32).
- [ ] T013 Register every new entity in `Pay.Api/Data/DigiPayDbContext.cs`:
  - Keys and max lengths as in data-model.md.
  - A unique index `UX_DdLedger_Txid_Address` on (`Txid`, `Address`) where `Type = credit`.
  - Indexes on `DdLedgerEntry(MerchantId, StoreId)`, `DdPayout(StoreId, Status)` and `PayoutAddressChange(StoreId, Status)`.
  - A filtered unique index allowing one `pending` change per owner.
- [ ] T014 Add the Postgres migration `Pay.Api/Migrations/<yyyyMMddHHmmss>_AddDdPayouts.cs` (with its Designer file and an updated `DigiPayDbContextModelSnapshot.cs`). It sets `DdPayoutAddressRequiredExempt = true` for existing stores where `DigiDollarEnabled = true`. Add matching `CREATE TABLE`/`ALTER TABLE` SQLite shims in `Pay.Api/Program.cs`, next to the existing shims.

### Node client and address ownership

- [ ] T015 Extend `Pay.Api/Services/DigiDollarNodeClient.cs` with:
  - `GetHealthAsync()` → `DdNodeHealth(reachable, ddEnabled, feeReserveDgb, reserveHealthy)`. It never throws; when unreachable it returns `reachable=false`.
  - `GetWalletBalanceAsync(minConf: 0)`, for the coverage check in research R4
  - `ListReceivesAsync(count, skip)` → items `{txid, address, amountCents, confirmations, time}`
  - `SendAsync(address, amountCents, comment)` → `DdSendResult`, one of `Sent(txid)`, `Rejected(code, message)` for HTTP 400, or `Ambiguous(reason)` for a timeout, 5xx, 502 or a dropped connection
  - `ListSendsAsync(sinceUnix, count)`
  - `GetTxStatusAsync(txid)` → `{status, confirmations}`
- [ ] T016 Register DD addresses as they're created. In `Pay.Api/Endpoints/PaymentsEndpoints.cs`, insert a `DdAddress` (Kind `payment`, owner from the store and merchant, `SessionId`, `Network`) in the same save as the DD session. Do the same anywhere else `GenerateAddressAsync` is called.

### Ledger, worker loop, alerts, webhooks

- [ ] T017 [P] Write `tests/DigiByte.Pay.Api.Tests/DdLedgerScannerTests.cs`. It covers:
  - A receive at a known address writes one credit, and a second scan doesn't duplicate it.
  - Confirmations update until ≥ 6, and `ConfirmedAt` is then set.
  - An unknown address writes no credit and raises the `unattributed_receive` alert.
  - Classification: before `ExpiresAt` up to the amount due → `payment`, above it → `overpayment`, after `ExpiresAt` → `late`, donation address → `donation`.
  - A credit dropping to 0 confirmations before payout is removed from the balance.
- [ ] T018 Implement `Pay.Api/Services/DdLedgerScanner.cs`:
  - Pages `ListReceivesAsync` from a stored cursor (`OperatorSetting` `ledger.scan_cursor`).
  - Writes credits idempotently and classifies them as in data-model.md.
  - Updates confirmations.
  - Stores the last successful scan time (`OperatorSetting` `ledger.last_scan_at`).
- [ ] T019 Implement `Pay.Api/Services/DdLedgerService.cs`:
  - Owner balances (confirmed held, still confirming, by source) as in data-model.md "Balance views".
  - The total liability across all owners plus `sending` and `checking` payouts.
  - `HoldReason` and `NextPayoutAt` for an owner.
- [ ] T020 [P] Implement `Pay.Api/Services/PayoutAlerts.cs`. It logs `ILogger` entries with `EventId` name `DdPayoutAlert` and a structured `alert` property for `fee_reserve_low`, `coverage_short`, `node_unavailable_6h`, `reversal_absorbed`, `unattributed_receive`, `store_auto_paused` and `checking_stuck`, at the levels in contracts/operator-api.md. Repeats are limited to once per alert per owner per hour.
- [ ] T021 Add `DispatchEventAsync(PayStore store, string eventName, object data, string? payoutId)` to `Pay.Api/Services/WebhookDispatcher.cs`. It builds the envelope `{event, timestamp, <subject>}` with camelCase and nulls omitted, uses the same HMAC headers, and saves the exact body to `WebhookDelivery.PayloadJson`. Update `Pay.Api/Services/WebhookRetrier.cs` to replay `PayloadJson` rows byte for byte; today it skips rows with no `SessionId`. Add a case to `tests/DigiByte.Pay.Api.Tests/WebhookDispatchTests.cs` covering signature and retry of a `PayloadJson` event.
- [ ] T022 Create `Pay.Api/Services/DdPayoutWorker.cs`, a `BackgroundService` with a 30-second `PeriodicTimer`, a DI scope per tick and a catch-and-log around each tick. Expose `RunOnceAsync(ct)`, which runs these steps in order, with each step a separate method:
  1. node health
  2. scan
  3. reconcile
  4. apply changes
  5. plan
  6. pre-send checks
  7. send

  US1 and US3 fill in the steps. When the node reports DD disabled, log one warning and stay idle (research R11). Register it in `Pay.Api/Program.cs` with `AddHostedService`, honouring `DIGIPAY_PAYOUTS_PAUSED`.
- [ ] T023 Add a startup backfill to `Pay.Api/Services/DdAddressBackfill.cs`, invoked once from `Pay.Api/Program.cs` after the migration. It creates a `DdAddress` for every `PaySession` with `Currency = "DD"` that doesn't have one, and sets `StoreId = null` when the store no longer exists (research R5).

**Checkpoint**: The ledger fills from the node and balances can be computed. The worker runs but sends nothing yet.

---

## Phase 3: User Story 1 - Set a DD payout address and receive DD automatically (Priority: P1) 🎯 MVP

**Goal**: A merchant sets a validated DD payout address with a Digi-ID re-sign-in, and confirmed DD is forwarded automatically, safely and in full.

**Independent Test**: quickstart Q1–Q3. A testnet store with nothing held sets an address, and a paid 5.00 DD session reaches the merchant wallet as exactly 5.00 DD, shown as sent and then confirmed.

### Tests for User Story 1 ⚠️

- [ ] T024 [P] [US1] Write `tests/DigiByte.Pay.Api.Tests/PayoutAddressValidationTests.cs` for `PUT /v1/pay/stores/{id}/payout-address`:
  - 400 `payout_address_invalid` with each `reason`: `format`, `checksum`, `wrong_network` (mainnet address on a testnet store), `dgb_address`, `digipay_address` (an address in `DdAddresses`).
  - 403 `session_required` for a `dgp_` key.
  - 403 `stepup_required` with no grant, an expired grant, a used grant, or another session's grant.
  - A first address on a store with nothing held applies at once (202, `effectiveAt = now`).
  - The audit entries `payout_address.requested` and `.applied` are written.
- [ ] T025 [P] [US1] Write `tests/DigiByte.Pay.Api.Tests/StepUpTests.cs`:
  - The challenge is session-only and purpose-bound.
  - A callback signed by an address other than `merchant.DigiIdAddress` returns 403 `stepup_address_mismatch` and writes the audit entry `payout.stepup_failed`.
  - The callback never creates a merchant.
  - Polling returns `stepUpGrant`, not a session token.
  - The dev sign-in endpoints can't mint grants.
  - A `payout-address` challenge that expires unsigned writes `payout.stepup_failed` with `reason = abandoned` (FR-009).
- [ ] T026 [P] [US1] Write `tests/DigiByte.Pay.Api.Tests/PayoutSendTests.cs` (FakeDdNode):
  - Confirmed credits are paid in one payout with comment = payout id, and the debit is written before the send.
  - `Sent` → `sent`, then `confirmed` at ≥ 6 confirmations.
  - `Rejected` → `failed`, with a `debit_reversal`.
  - `Ambiguous` → `checking`, and no second `/send` call happens on any later cycle while `checking`.
  - `checking` resolves to `sent` when `/sends` lists the comment, or when the fallback (address, amount, time, unclaimed txid) matches.
  - `checking` → `failed` when nothing is found after 10 minutes with the node healthy.
  - At most one `sending` or `checking` payout per store.
  - No payout under 100 cents.
  - The amount received equals the amount held, with no fee deducted (FR-017).
  - A payout whose destination has since become a `DdAddresses` entry, or no longer fits the store's network, is never sent (FR-002).
- [ ] T027 [P] [US1] Write `tests/DigiByte.Pay.Api.Tests/PayoutSafetyTests.cs`:
  - Node unreachable → `waiting` with `node_unavailable`, never `failed` (FR-034).
  - `reserve_healthy=false` → `waiting` with `fee_reserve`, plus the `fee_reserve_low` alert (FR-018).
  - Wallet available DD (research R4) below the liability → every payout waits with `coverage`, plus the `coverage_short` alert.
  - Right after a `sent` payout whose change has 0 confirmations, the coverage check passes and no `coverage_short` alert fires.
  - An insufficient-funds rejection gives `waiting:coverage`, not `failed`, and 3 of them in a row don't pause the store.
  - The global env pause and the operator pause → `operator_paused`.
  - 3 rejections in a row set `DdPayoutsAutoPausedAt` and raise the `store_auto_paused` alert (FR-022).
  - A reversed credit already covered by a payout writes `reversal_absorbed`, doesn't lower the store balance, and raises the `reversal_absorbed` alert (FR-023).
  - Node unreachable for more than 6 hours raises `node_unavailable_6h` (FR-037).
  - With the node unreachable, `PUT …/payout-address` still validates, saves the change and starts the 48-hour wait (FR-035).
  - With the node unreachable and the payout worker failing every cycle, a DGB session still goes from pending to confirmed through `InvoiceMonitor` (FR-036).
- [ ] T028 [P] [US1] Write `tests/DigiByte.Pay.Api.Tests/OperatorPayoutTests.cs` (contract C3):
  - 503 `operator_key_not_configured` when the key isn't set.
  - 401 with a wrong key, and for `dps_`/`dgp_` tokens.
  - Global and per-store pause and resume, with the audit entries `payouts.paused` and `payouts.resumed`.
  - `GET /health` shows `liabilityCents` and `coverageOk`.

### Implementation for User Story 1

- [ ] T029 [US1] Extend `Pay.Api/Auth/AuthChallengeStore.cs` challenges with `Purpose`, `MerchantId` and `SessionId`. Implement `Pay.Api/Services/StepUpService.cs` (issue a challenge, mint a grant, validate and consume it inside the caller's database transaction). In `Pay.Api/Endpoints/AuthEndpoints.cs`:
  - When the purpose is `payout-address`, the `/callback` checks the signing address equals `merchant.DigiIdAddress`, never creates a merchant, and mints a `StepUpGrant` instead of a session.
  - `/poll/{nonce}` returns `{status:"signed", stepUpGrant}`.
  - The dev sign-in paths can't create grants.
  - When a `payout-address` challenge expires unsigned, write the audit entry `payout.stepup_failed` with `reason = abandoned` (once, on the challenge store's expiry sweep). An address mismatch writes it with `reason = address_mismatch`.
- [ ] T030 [US1] Implement `Pay.Api/Services/PayoutAddressService.cs`:
  - Validates an address in the order in contracts/merchant-api.md C1: trim and letters/digits only; `DigiDollarAddressValidator.TryDecode(addr, null, …)` → `format`/`checksum`; DGB `AddressValidator.IsValid` → `dgb_address`; network ≠ `store.Network` → `wrong_network`; found in `DdAddresses` → `digipay_address`.
  - Creates a change: `EffectiveAt = now` when the store has no address and both held and still-confirming DD are 0, otherwise `now + 48h`.
  - Applies due changes.
  - Writes the audit entries (`payout_address.requested`, `.applied`), the webhook `DispatchEventAsync` (`payout_address.change_requested`, `payout_address.changed`), and the banner state.
- [ ] T031 [US1] Create `Pay.Api/Endpoints/PayoutsEndpoints.cs` with these routes from contracts/merchant-api.md:
  - `GET /v1/pay/payouts/step-up/challenge`
  - `PUT /v1/pay/stores/{storeId}/payout-address`
  - `POST /v1/pay/stores/{storeId}/payout-address/acknowledge`
  - `POST /v1/pay/stores/{storeId}/payout-address/confirm`

  Address routes refuse any caller whose `ApiCaller.KeyKind != null` with 403 `session_required`. Ownership is checked with `LoadOwnedAsync`. Map the group in `Pay.Api/Program.cs` under the `authenticated` rate-limit policy.
- [ ] T032 [US1] Implement `Pay.Api/Services/DdPayoutPlanner.cs` for the default `confirmed` schedule plus explicit requests (`DdPayoutRequestedAt`). It skips a store when it has no address, a pending change, an operator pause (global or store), an automatic pause, a `sending`/`checking` payout, or confirmed held DD under 100 cents. Exception: when `DdPayoutRequestedAt` was set by a `retry` (T043), one payout is allowed despite an automatic pause, but never despite an operator pause. Otherwise it creates a `DdPayout` (`Destination` copied from `DdPayoutAddress`) with its `DdPayoutItem` rows covering the oldest confirmed credits first.
- [ ] T033 [US1] Implement `Pay.Api/Services/DdPayoutSender.cs` as in research R3:
  - Pre-send checks: node health, `reserve_healthy`, and wallet available DD ≥ liability (research R4 coverage rule) → otherwise `waiting` with its reason and the matching alerts.
  - Address checks, just before claiming:
    - Check `Destination` again with `PayoutAddressService.Validate(Destination, store.Network)`, using the same checks as T030, including "not one of DigiPay's own DD addresses". If it fails, mark the payout `failed` with `FailureCode = payout_address_invalid`, set `DdPayoutsAutoPausedAt`, raise `store_auto_paused`, and don't send (FR-002).
    - Require no `pending` `PayoutAddressChange` for the store. If one is pending, leave the payout in `waiting` with hold reason `change_pending` (FR-006).
    - Require `Destination == store.DdPayoutAddress`. If the destination is no longer current, mark the waiting payout `failed` with `FailureCode = destination_superseded`. It doesn't count toward `DdPayoutConsecutiveFailures` and isn't shown to the merchant as a failure. The planner creates a new payout to the current address next cycle (FR-020).
  - A claim in one database transaction: status `sending`, a `debit` entry, the attempt count and time.
  - `SendAsync` with comment = payout id, mapping `Sent`/`Rejected`/`Ambiguous` to `sent`/`failed`+`debit_reversal`/`checking`. A `Rejected` result whose error is insufficient or unconfirmed funds (the code and message recorded by T001) goes back to `waiting` with `WaitReason = coverage`. The debit is reversed and the attempt doesn't count toward `DdPayoutConsecutiveFailures`. Raise `coverage_short` only if this lasts more than 10 minutes.
  - Reconciling `checking` through `ListSendsAsync(since=LastAttemptAt-5min)`, matching by comment first, then (address, amount, time, txid not claimed by another payout). Not found for 10 minutes with the node healthy → `failed`+`debit_reversal`. Over 1 hour → the `checking_stuck` alert.
  - Confirming `sent` payouts at ≥ 6 confirmations.
  - Keeping `DdPayoutConsecutiveFailures` and the automatic pause after 3, which writes the audit entry `payouts.paused` (actor `System`, `metadata.reason = auto_failures`). When a payout reaches `sent`, reset the count to 0 and, if the store was automatically paused, clear `DdPayoutsAutoPausedAt` and write `payouts.resumed` (FR-009, FR-022).
  - Audit entries `payout.sent`, `.confirmed`, `.failed`, `.checking`.
- [ ] T034 [US1] Handle reversals in `Pay.Api/Services/DdLedgerScanner.cs`. When a credit's transaction reports 0 confirmations or `unknown` after it was confirmed, remove an unpaid credit from the balance. When the credit is already in a `DdPayoutItem`, write `reversal_absorbed` (store balance unchanged) and raise the `reversal_absorbed` alert (FR-023).
- [ ] T035 [US1] Fill in the steps of `DdPayoutWorker.RunOnceAsync` in `Pay.Api/Services/DdPayoutWorker.cs`:
  1. health, keeping when the node was first seen unreachable and raising `node_unavailable_6h` after 6 hours
  2. `DdLedgerScanner`
  3. `DdPayoutSender` reconcile and confirm
  4. `PayoutAddressService` applying due changes
  5. `DdPayoutPlanner`
  6. `DdPayoutSender` send, at most one per store per cycle
- [ ] T036 [US1] Create `Pay.Api/Endpoints/OperatorEndpoints.cs` with `POST /v1/pay/operator/payouts/pause`, `POST /v1/pay/operator/payouts/resume` and `GET /v1/pay/operator/payouts/health`, as in contracts/operator-api.md. The `X-DigiPay-Operator-Key` header is compared in constant time against `DIGIPAY_OPERATOR_KEY`. The endpoints return 503 when it isn't configured, use the `auth` rate-limit policy, and write the audit entries `payouts.paused`/`payouts.resumed`. Map them in `Pay.Api/Program.cs`.
- [ ] T037 [US1] Require a payout address to turn DD on, in `Pay.Api/Endpoints/StoresEndpoints.cs` `PATCH /v1/pay/stores/{id}`:
  - `digiDollarEnabled` false → true with no `DdPayoutAddress`, no pending change and `DdPayoutAddressRequiredExempt == false` returns 409 `payout_address_required` (FR-031).
  - A `network` change the current payout address doesn't fit returns 409 `payout_address_network_mismatch`.
  - Add `ddPayoutAddress`, `ddPayoutPending`, `ddPayoutSchedule` and `ddPayoutsPaused` to `StoreDto` in `Pay.Api/Endpoints/MerchantMeEndpoints.cs`.
- [ ] T038 [P] [US1] Create `Pay.Web/Components/Pages/Merchant/StepUpDialog.razor`. It shows the Digi-ID QR code and link from `GET /v1/pay/payouts/step-up/challenge`, polls `/v1/pay/auth/poll/{nonce}` until it gets `stepUpGrant` (or the challenge expires, or the merchant cancels), and returns the grant to the caller. Reuse the sign-in page's QR rendering.
- [ ] T039 [US1] Create `Pay.Web/Components/Pages/Merchant/DigiDollarSection.razor` (route section `digidollar`, added to `Pay.Web/Components/Pages/Merchant/DashboardSections.cs`) with the payout-address form:
  - Validates as the merchant types with `DigiDollarAddressValidator.IsValid(addr, store.Network)`, plus a DGB-address check, showing specific messages.
  - Shows the address back with its first and last 6 characters highlighted, through `ConfirmDialog.razor` (FR-003).
  - Runs `StepUpDialog`, then calls `PUT …/payout-address`, and maps API error codes to messages in `Pay.Web/Components/Shared/Ui/ApiError.cs`.
- [ ] T040 [US1] In `Pay.Web/Components/Pages/Dashboard.razor` settings:
  - Change the DigiDollar toggle so turning DD on without a payout address opens `DigiDollarSection` first (FR-031).
  - Replace the copy at the DD toggle with text saying DigiPay holds DD only until it's paid out to the store's payout address, and that DigiPay pays the network fee (FR-040).
  - Show the persistent "add a payout address" prompt for stores with DD on and no address (FR-030).
- [ ] T041 [P] [US1] Write `tests/DigiByte.Pay.Web.Tests/DigiDollarSectionTests.cs` (bUnit):
  - Invalid, DGB and wrong-network addresses show their messages and never call the API.
  - A valid address opens the confirmation dialog with the start and end highlighted.
  - Confirming starts the step-up.
  - The settings toggle routes to the section when there is no address.

**Checkpoint**: US1 works end to end (quickstart Q1–Q3, Q9). This is the MVP: merchants can get their DD out.

---

## Phase 4: User Story 2 - See DD balance, payouts and failures in the Dashboard (Priority: P1)

**Goal**: Merchants see what DigiPay holds for them, why it's held, every payout with its status, and failures with a plain reason and a retry button.

**Independent Test**: quickstart Q4. Stop NodeApi with DD held, and the Dashboard shows the stored balance marked as possibly out of date and "waiting for DigiDollar service", with no failed payouts. Restart it and the payout goes out.

### Tests for User Story 2 ⚠️

- [ ] T042 [P] [US2] Write `tests/DigiByte.Pay.Api.Tests/PayoutReadTests.cs`:
  - `GET /v1/pay/stores/{id}/dd-balance` returns `confirmedCents`, `confirmingCents`, `bySource`, `lastCheckedAt`, `stale` (true when the last scan was more than 5 minutes ago) and each `holdReason`.
  - `GET /v1/pay/stores/{id}/payouts?status=&cursor=` returns newest first, 50 per page, with the status filter applied.
  - `GET /v1/pay/payouts/{id}` returns its `items`.
  - All three work with a `dps_` session and a `dgp_` merchant key, and return 404 for another merchant's store.
  - `POST /v1/pay/stores/{id}/payouts {reason:"retry"}` → 202 and a new payout on the next cycle, or 409 `below_minimum`/`no_address`/`change_pending`/`payout_in_progress`/`operator_paused`.
  - The session DTO includes `ddPayout` and `ddExtraRefundCents`.

### Implementation for User Story 2

- [ ] T043 [US2] Add `GET /v1/pay/stores/{storeId}/dd-balance`, `GET /v1/pay/stores/{storeId}/payouts`, `GET /v1/pay/payouts/{payoutId}` and `POST /v1/pay/stores/{storeId}/payouts` (`manual|retry` → sets `DdPayoutRequestedAt`, writes the audit entry `payout.manual`/`payout.retry`. For `retry` on an automatically paused store, the planner makes one attempt despite the pause, and `DdPayoutsAutoPausedAt` is cleared only if that payout reaches `sent` (T033, FR-022)) to `Pay.Api/Endpoints/PayoutsEndpoints.cs`, using `DdLedgerService` and the `PayoutDto` shape in contracts/merchant-api.md C2. Leave payouts with `FailureCode = destination_superseded` out of the payouts list. Include `willRetry` (true unless automatically or operator paused) and a plain-language `failureReason` mapped from `FailureCode`.
- [ ] T044 [US2] Add `ddPayout: {payoutId, status} | null` and `ddExtraRefundCents` to the session DTO (`ToDto` in `Pay.Api/Endpoints/PaymentsEndpoints.cs`) by joining `DdLedgerEntry`/`DdPayoutItem` on `SessionId`. The `session.*` webhook bodies built by `Pay.Api/Services/WebhookDispatcher.cs` get the same fields.
- [ ] T045 [US2] Extend `Pay.Web/Components/Pages/Merchant/DigiDollarSection.razor` with:
  - A balance panel: confirmed and still confirming, the source breakdown, "last checked …", a "may be out of date" note when `stale`, a hold-reason sentence for every `holdReason` value (for `waiting_confirmations`: "Ready to pay out after 6 network confirmations (about 90 seconds)."), and `nextPayoutAt`.
  - A payouts table: amount, shortened destination, date, a status chip (sending, sent, confirmed, failed, checking, waiting) and a transaction link, with a status filter and paging.
  - "Pay out now" and "Retry now" buttons.
- [ ] T046 [P] [US2] Create `Pay.Web/Components/Pages/Merchant/PayoutDetail.razor` at `/merchant/payouts/{id}`. It shows amount, destination, the covered sessions with their source labels (`extra_refund` labelled "extra payment, refund the buyer"), sent time, status, transaction link, and for failures the reason, "this DD is still held for you", and whether DigiPay will retry, with a "Retry now" button.
- [ ] T047 [US2] In `Pay.Web/Components/Pages/Dashboard.razor`, add `Currency`/`AmountCentsDd`/`DdPayout` to the `SessionRow` record and show DD amounts in DD with a paid-out link in the payments list. In `Pay.Web/Components/Pages/SessionDetail.razor`, show the paid-out status with a link to the payout, the "extra payment, refund the buyer" label, and a note when DigiPay absorbed a reversal.
- [ ] T048 [P] [US2] Extend `tests/DigiByte.Pay.Web.Tests/DigiDollarSectionTests.cs` and add `tests/DigiByte.Pay.Web.Tests/PayoutDetailTests.cs`. They check the hold-reason text for each value, the stale note, the failed-payout view with retry, and the DD amount and payout link in the payments list.

**Checkpoint**: US1 and US2 together make DD payouts visible and checkable (quickstart Q3–Q4).

---

## Phase 5: User Story 3 - Change the payout address safely (Priority: P1)

**Goal**: Changing the address needs a fresh Digi-ID sign-in, waits 48 hours with payouts held, can be cancelled from any session, and is announced through the audit log, a banner and a webhook.

**Independent Test**: quickstart Q5–Q6.

### Tests for User Story 3 ⚠️

- [ ] T049 [P] [US3] Write `tests/DigiByte.Pay.Api.Tests/PayoutAddressChangeTests.cs`:
  - A change on a store that has an address → `EffectiveAt = RequestedAt + 48h`.
  - A first address on a store already holding DD → 48 hours as well.
  - While it is pending, the planner creates no payout and `holdReason = change_pending`.
  - A second request → 409 `payout_change_pending`.
  - `DELETE …/payout-address/pending` from another `dps_` session with no grant cancels it, keeps the old address, writes the audit entry `payout_address.cancelled` and sends the webhook `payout_address.change_cancelled`, and payouts resume to the old address.
  - Once `EffectiveAt` passes, the worker applies it (`payout_address.applied`, `payout_address.changed`) and the next payout goes to the new address.
  - A payout `waiting` on `fee_reserve` when a change is requested, then applied, is sent to the new address, never the old one (FR-020).
  - A `dgp_` key gets 403 on every address route.
  - The banner state stays until acknowledged.

### Implementation for User Story 3

- [ ] T050 [US3] Add `DELETE /v1/pay/stores/{storeId}/payout-address/pending` (session-only, no grant) to `Pay.Api/Endpoints/PayoutsEndpoints.cs`, and cancellation, which writes the audit entry and sends the webhook, to `Pay.Api/Services/PayoutAddressService.cs`. Return 404 `no_pending_change` when there's nothing pending.
- [ ] T051 [US3] Add `GET /v1/pay/me/payout-alerts` to `Pay.Api/Endpoints/MerchantMeEndpoints.cs`. It returns the merchant's unacknowledged `PayoutAddressChange` events (requested, cancelled, applied), for the banner.
- [ ] T052 [US3] In `Pay.Web/Components/Pages/Dashboard.razor`, show a banner on every Dashboard section for each unacknowledged address event. The banner shows the store, the old and new addresses, and the effective time with a countdown, a "Cancel change" button while pending, and an acknowledge action that calls `…/payout-address/acknowledge`.
- [ ] T053 [US3] Extend `Pay.Web/Components/Pages/Merchant/DigiDollarSection.razor` for an existing address. It shows the current address, a "Change address" flow (the same validation, confirmation and `StepUpDialog`), and a pending change with its new address, effective time and "Cancel change" button.
- [ ] T054 [P] [US3] Add bUnit cases to `tests/DigiByte.Pay.Web.Tests/DigiDollarSectionTests.cs` and `tests/DigiByte.Pay.Web.Tests/DashboardTests.cs`: the pending-change view and cancel, the banner on another section, and acknowledging it.

**Checkpoint**: All P1 stories are done. Payouts can safely be switched on in production once the release checklist is met.

---

## Phase 6: User Story 4 - DD received before payouts existed is paid out (Priority: P2)

**Goal**: DD held from before this feature, including late, overpaid, extra-refund, donation and deleted-store DD, is shown by source and paid out under the same rules.

**Independent Test**: quickstart Q7 and Q10.

### Tests for User Story 4 ⚠️

- [ ] T055 [P] [US4] Write `tests/DigiByte.Pay.Api.Tests/DdBackfillTests.cs`:
  - The backfill creates a `DdAddress` for every old DD session, and is safe to run again.
  - A session of a deleted store becomes a merchant-level owner.
  - The first scan credits the full receive history with the correct sources: overpaid, after expiry, and DD arriving after a session was already `Confirmed` or `Underpaid`.
  - A first address on a store holding backfilled DD gets the 48-hour hold.
  - The first payout covers every source and labels `extra_refund`.
- [ ] T056 [P] [US4] Write `tests/DigiByte.Pay.Api.Tests/StoreDeleteDdTests.cs`:
  - `DELETE /v1/pay/stores/{id}` → 409 `dd_held` with `{confirmedCents, confirmingCents}` while 100 or more cents are held or a payout is in progress.
  - `?finalPayout=true` queues a `store_delete` payout.
  - A remainder under 100 cents moves through `transfer_out`/`transfer_in` to the merchant-level owner, and the delete goes ahead.
  - With no address and 100 or more cents held, the delete is blocked with the reason.

### Implementation for User Story 4

- [ ] T057 [US4] In `Pay.Api/Services/DdLedgerScanner.cs`, classify `extra_refund` for buyer-choice sessions (033 FR-025) when that session data exists: DD received after the session locked to DGB is `extra_refund`. Classify `donation` for `DdAddress.Kind == "donation"`. Keep this a no-op until 033 adds those fields, guarded by a null check on the 033 fields.
- [ ] T058 [US4] Add a guard and a final payout to `DELETE /v1/pay/stores/{storeId}` in `Pay.Api/Endpoints/StoresEndpoints.cs`, as in contracts/merchant-api.md "Changes to existing endpoints". On delete, set `DdAddress.StoreId = null` and move the remainder to the merchant-level owner with a pair of `transfer_out`/`transfer_in` entries (FR-015, research R2).
- [ ] T059 [US4] Add the merchant-level DD routes to `Pay.Api/Endpoints/PayoutsEndpoints.cs`: `GET /v1/pay/me/dd-balance`, `PUT /v1/pay/me/dd-balance/payout-address`, `DELETE /v1/pay/me/dd-balance/payout-address/pending` and `GET /v1/pay/me/dd-balance/payouts`. They have the same step-up and 48-hour rules (FR-039). Make `DdPayoutPlanner`, `DdPayoutSender` and `PayoutAddressService` handle owners with `StoreId = null` by reading and writing payout settings through one `IPayoutOwnerSettings` view, backed by `PayStore` for stores and `MerchantDdSettings` (T011) for the merchant-level owner. Create the `MerchantDdSettings` row the first time a store holding DD is deleted.
- [ ] T060 [US4] In `Pay.Web/Components/Pages/Merchant/DigiDollarSection.razor`, show a "Held from before payouts" source breakdown and a merchant-level "DD from deleted stores" panel when that balance isn't zero, with its own payout address flow. In the store-delete confirmation in `Pay.Web/Components/Pages/Dashboard.razor`, explain the `dd_held` block and offer a final payout.

**Checkpoint**: Every DD DigiPay held before release can be paid out (SC-009).

---

## Phase 7: User Story 5 - Choose when payouts go out (Priority: P2)

**Goal**: Once-a-day and threshold schedules, with the 7-day cap. "Pay out now" already exists from US2.

**Independent Test**: quickstart Q8.

### Tests for User Story 5 ⚠️

- [ ] T061 [P] [US5] Write `tests/DigiByte.Pay.Api.Tests/PayoutScheduleTests.cs`:
  - `PATCH /v1/pay/stores/{id}/payout-settings` validation: `dailyTime` required for `daily`, a valid IANA `timeZone`, `thresholdCents` "At least 100 and at most 10,000,000" for `threshold`; otherwise 400 `invalid_schedule` with the field.
  - The audit entry `payout_settings.update`, with no step-up needed.
  - `daily` pays everything confirmed once the local time passes `dailyTime`, once per day.
  - `threshold` holds below the amount (`holdReason = threshold`), pays everything when it's reached, and pays anyway once the oldest held credit is 7 days old (`Trigger = threshold_age`).
  - "Pay out now" overrides the schedule.

### Implementation for User Story 5

- [ ] T062 [US5] Add `PATCH /v1/pay/stores/{storeId}/payout-settings` (session) to `Pay.Api/Endpoints/PayoutsEndpoints.cs`, with the validation above, saving to `PayStore` and writing the audit entry `payout_settings.update`.
- [ ] T063 [US5] Extend `Pay.Api/Services/DdPayoutPlanner.cs` with `daily` (keep the last run per store and work out the next local slot with `TimeZoneInfo.FindSystemTimeZoneById`) and `threshold` (including the 7-day oldest-credit cap). Return `NextPayoutAt` through `DdLedgerService` for the balance endpoint.
- [ ] T064 [US5] Add a "When payouts go out" control to `Pay.Web/Components/Pages/Merchant/DigiDollarSection.razor`: radio buttons for as soon as confirmed (default), once a day (time and time zone picker, defaulting to the browser's zone), and when the balance reaches (DD amount, 1.00–100,000.00). It saves through `PATCH …/payout-settings`.

**Checkpoint**: Every timing option works (quickstart Q8).

---

## Phase 8: User Story 6 - Get told about payouts in the merchant's own systems (Priority: P3)

**Goal**: Signed payout webhooks and the read-only payout API for integrations.

**Independent Test**: With a webhook URL set, a payout and an address change deliver signed events with the shapes in contracts/webhooks.md.

### Tests for User Story 6 ⚠️

- [ ] T065 [P] [US6] Write `tests/DigiByte.Pay.Api.Tests/PayoutWebhookTests.cs`:
  - `payout.sent`, `payout.confirmed` and `payout.failed` bodies match contracts/webhooks.md, with `items`, `failureCode`/`failureReason`/`willRetry` on failure and no `txid` on failure.
  - The `X-DigiPay-Signature` verifies.
  - Nothing is sent for `waiting` or `checking`.
  - A failed delivery is retried with the identical body from `PayloadJson`.
  - A first address that applies at once sends `change_requested` then `changed`.

### Implementation for User Story 6

- [ ] T066 [US6] In `Pay.Api/Services/DdPayoutSender.cs`, send `payout.sent`, `payout.confirmed` and `payout.failed` through `WebhookDispatcher.DispatchEventAsync(store, event, new { payout = … }, payoutId)` using the payload in contracts/webhooks.md. Skip stores with no `WebhookUrl`. Don't send `payout.failed` for a payout closed with `FailureCode = destination_superseded`.
- [ ] T067 [US6] Add the new events to the webhook event list in `Pay.Web/Components/Pages/Dashboard.razor` (webhook section) and to the webhook test sender in `Pay.Api/Endpoints/StoresEndpoints.cs`, if it lists event types.

**Checkpoint**: All user stories work.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T068 [P] Update the in-app docs in `Pay.Web/Components/Pages/ApiDocs.razor` (payout endpoints, operator note left out, error codes, session DTO fields), `Pay.Web/Components/Pages/Docs.razor` (payout webhooks and verification), `Pay.Web/Components/Pages/Features.razor` and `Pay.Web/Components/Pages/Home.razor`. The "Non-custodial" wording must say DD is held by DigiPay until it's paid out (FR-041, plan Docs row).
- [ ] T069 [P] Update the `sdk/dotnet/README.md`, `sdk/node/README.md` and `sdk/python/README.md` webhook event lists, the `samples/*/README.md` webhook handlers, and both `digipay.postman_collection.json` copies (root and `Pay.Web/wwwroot/`) with the payout routes.
- [ ] T070 [P] Update `docs/RAILWAY_NODE.md` (production NodeApi needs `DIGIDOLLAR_WALLET=digipay-hot`, a funded DGB fee reserve, and a write key shared with Pay.Api), `docs/ARCHITECTURE.md` (the payout worker, the ledger and how they flow), `docs/SECURITY.md` (step-up grants and the operator key) and `SELF_CUSTODY.md` (DigiPay holds DD until payout).
- [ ] T071 [P] Add a `CHANGELOG.md` entry under Unreleased: DD payouts to merchants (beta).
- [ ] T072 Run `dotnet build` and `dotnet test` for `tests/DigiByte.Pay.Api.Tests`, `tests/DigiByte.Pay.Web.Tests` and `tests/DigiByte.NodeApi.Tests`. Fix any failures.
- [ ] T073 Run quickstart Q0–Q10 on testnet and fill in its release checklist. Record the results in `specs/034-digipay-dd-payouts/quickstart.md`.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none. T001 should come first, because its findings can change T006 and T033. T003 (constitution amendment draft) runs in parallel, and its PR should be merged or agreed before US1 merges.
- **Foundational (Phase 2)**: depends on Setup. It blocks every story.
- **US1 (Phase 3)**: depends on Phase 2. It's the MVP and everything else builds on it.
- **US2 (Phase 4)**: depends on US1's payouts existing (T032–T035). Its UI can be built alongside US1's backend.
- **US3 (Phase 5)**: depends on US1 (T029–T031 step-up and address service).
- **US4 (Phase 6)**: depends on Phase 2 (backfill, scanner) and US1. T059 also depends on US3's change flow.
- **US5 (Phase 7)**: depends on US1's planner (T032). Independent of US2–US4.
- **US6 (Phase 8)**: depends on US1's sender (T033) and T021. Independent of US2–US5.
- **Polish (Phase 9)**: after the chosen stories.

### Within each story

Tests first, and they must fail. Then services, then endpoints, then the Dashboard UI.

### Release gate

Payouts can be switched on in production only after US1, US2 and US3 are done, along with T070 and the quickstart release checklist. All three are P1, and US3's protection must exist from the first payout (spec US3 "Why this priority"). Spec 033's surfaces stay blocked until then (033 FR-032).

## Parallel Example: User Story 1

```text
# All US1 tests together (different files):
T024 PayoutAddressValidationTests.cs
T025 StepUpTests.cs
T026 PayoutSendTests.cs
T027 PayoutSafetyTests.cs
T028 OperatorPayoutTests.cs
T041 DigiDollarSectionTests.cs (bUnit)

# Then, in parallel with backend T029–T037:
T038 StepUpDialog.razor
```

Other parallel groups:
- Phase 2 entities T007–T011.
- NodeApi T005/T006 alongside the Pay.Api data model.
- US5 (T061–T064) and US6 (T065–T067) in parallel once US1 is done.
- Docs T068–T071.

## Implementation Strategy

### MVP first

1. Phase 1, then Phase 2.
2. Phase 3 (US1): automatic payouts to a validated address, with every safety rule (no double sends, coverage, fee reserve, node-down waits, operator pause).
3. Check it with quickstart Q1–Q3 and Q9 on testnet. Don't release on its own: US2 and US3 are also P1 and needed before production (see Release gate).

### Incremental delivery

1. US1, then US2, then US3. That makes a production-ready release once the release checklist is met, and it unblocks spec 033.
2. US4: pay out DD held from before payouts. Ideally in the same release, since that DD is already owed.
3. US5 and US6 in any order.
4. Polish and the testnet run.
