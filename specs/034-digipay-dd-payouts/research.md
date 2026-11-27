# Research: DigiDollar payouts to merchants

Phase 0 for [plan.md](plan.md). Every open question from Technical Context is answered below.

## R1. How DigiPay sends DD

**Decision**: send through NodeApi's existing wallet-scoped `POST /api/digidollar/send`. It calls the RPC `senddigidollar addr amountCents comment` on the `digipay-hot` wallet. Each payout goes out as one `senddigidollar` to one store's address.

The comment is set to the payout id (`dpo_…`). Two NodeApi changes go with this (contract C4):
- `/send` returns a parsed `{ txid }`. Today it passes the RPC result through as raw JSON, and nothing documents whether that is a string or an object.
- A new wallet-scoped `GET /api/digidollar/sends?since=` lists recent send transactions with their comment, address, amount and txid, using `listdigidollartxs` with category `send`.

**Rationale**:
- The route, the key gate and the fee handling already exist. `senddigidollar` takes the DGB fee from the same wallet automatically.
- One payout per send keeps the "checking" reconciliation simple (R3).
- `send-many` drops the comment, so a batch couldn't be matched back to its payouts.

**Alternatives considered**:
- Building DD transactions in Pay.Api with `DigiDollarTransferBuilder`. Pay.Api would have to hold keys. Rejected.
- `send-many` batching across stores. It saves a little DGB fee but loses per-payout reconciliation. Rejected for now; it can be revisited when the comment is supported.

**To verify on testnet** (quickstart Q0):
- the return shape of `senddigidollar`
- that `listdigidollartxs` returns the comment for sends

If it doesn't return the comment, the fallback match is (address, amount, time at or after the attempt, and the txid not claimed by another payout).

## R2. DD protocol limits that shape payouts

**Decision**:
- **Spendable DD:** only DD credits with at least 6 confirmations can be paid out. The node won't spend unconfirmed DD anyway (spec 001 R1, `dd-input-amounts-unknown`).
- **Payout minimum:** 1.00 DD (100 cents), because every DD output must be at least $1.
- **Sub-1.00 DD remainder on store deletion:** it can't be sent on-chain, so it moves to the merchant's account-level DD balance (the same balance FR-039 uses for deleted stores) instead of being paid out.

**Spec updated**: FR-015 and the store-deletion edge case now say this.

**Rationale**:
- spec 001 research R1, R4 and R15: DD outputs are at least $1, and DD change is 0 or at least 100 cents. The node handles change.
- 6 confirmations take about 90 seconds on DigiByte. That keeps SC-002 (15 minutes) comfortable and makes the reversals DigiPay absorbs (FR-023) very rare.

**Alternatives considered**:
- Paying out at 1 confirmation, which is when a DD session already shows as "confirmed". That raises DigiPay's reversal risk for very little gain in speed.
- Forfeiting a sub-1.00 DD remainder. That takes merchant money. Rejected.

## R3. Never paying twice ("checking")

**Decision**: each payout is a small state machine whose rows are claimed and saved before any network call.

1. Write the `DdPayout` row with status `sending`, an attempt count and an attempt time. Write the ledger debit in the same database transaction.
2. Call `/send` with comment = payout id.
3. Based on the result:
   - **Success**: save the txid, status `sent`.
   - **Clear rejection** (the RPC answered with an error, HTTP 400): status `failed`, and the debit is reversed in the same transaction.
   - **No clear answer** (timeout, 5xx, connection lost, 503 code -1): status `checking`.
4. A `checking` payout is reconciled through `GET /sends?since=attemptAt-5min`.
   - Found: `sent`, with the txid.
   - Not found once the node is healthy and 10 minutes have passed since the attempt: `failed`, the debit is reversed, and the next cycle can try again.
5. A `sent` payout moves to `confirmed` once `/tx/{txid}/status` reports 6 or more confirmations.

**Rationale**:
- `senddigidollar` has no idempotency key.
- A database-first claim, a comment-based lookup and the rule "no retry while checking" give at most one payout per intent (FR-019, SC-004).
- The 10-minute window allows for RPC calls slower than Pay.Api's 15-second HttpClient timeout. NodeApi's RPC timeout is 5 minutes.

**Alternatives considered**:
- Retrying on timeout: risks a double send. Rejected.
- Using `gettransaction`: NodeApi has no wrapper for it, and it needs the txid we don't have.

## R4. Per-store accounting in one shared wallet

**Decision**: DigiPay keeps its own ledger.
- `DdAddress` records every DD address DigiPay creates, with its owner: store, merchant, kind (payment or donation) and session.
- `DdLedgerScanner` reads the wallet's receives incrementally (`listdigidollartxs`, category `receive`, paged) and writes one credit per (txid, address). The pair is unique, so writing a credit twice has no effect.
- Each credit is classified as payment, overpayment, late, extra-refund or donation, against the session it belongs to.
- A payout writes a debit.
- Store balance = credits with 6+ confirmations − debits (still-confirming credits are shown separately).
- Receives at addresses DigiPay has no record of raise an operator alert and are never credited.

**Coverage rule**: before any send, the worker checks that **wallet DD (all confirmations, including unconfirmed change from DigiPay's own sends) − DD from incoming payments that doesn't have 6 confirmations yet ≥ (sum of all owners' confirmed held DD) + (amounts of `sending` and `checking` payouts)**. Change from a payout is DigiPay's own money coming back, so it counts at once. Only incoming payments must reach 6 confirmations. If it isn't, every payout waits as "delayed by DigiPay" and the operator is alerted. That is how FR-023's "other stores' DD is never used" holds: a store's payout can only be sent while the wallet still covers every other store's held DD in full.

**Rationale**:
- All stores share `digipay-hot`, so coin selection spends any UTXO. Per-address balances (what `InvoiceMonitor` reads) stop meaning anything after the first payout.
- The scanner also catches DD that arrives after a session is Confirmed or Underpaid. `InvoiceMonitor` stops polling those sessions today, so late and extra DD would otherwise be missed (FR-016).

**Alternatives considered**:
- Per-address balances: break after the first payout.
- One node wallet per store: heavy to operate, and it multiplies the DGB fee reserves to watch.

## R5. Backfill of DD held before payouts (FR-038, FR-039)

**Decision**:
- The migration and a startup backfill create a `DdAddress` row for every `PaySession` with `Currency = "DD"`, with the owner taken from the session's store and merchant.
- Sessions whose store has been deleted become merchant-level owners (`StoreId = null`).
- The first scanner run reads the wallet's full receive history and credits everything.
- If 033's donation address exists by then, it is registered as kind `donation`.

**Rationale**: every DD address DigiPay ever created came from a session (and later a donation address), so ownership can always be worked out.

## R6. Re-sign-in with Digi-ID (FR-004)

**Decision**:
- `AuthChallengeStore` challenges get a `Purpose` (`payout-address`) and a `MerchantId` / `SessionId` binding.
- `GET /v1/pay/payouts/step-up/challenge` is session-only (`dps_`). It issues a purpose-bound challenge.
- The existing Digi-ID `/callback` sees the purpose and does three things:
  - checks that the signing address equals `merchant.DigiIdAddress`. A mismatch is rejected and audited (`payout.stepup_failed`).
  - never creates a merchant.
  - mints a `StepUpGrant` (single use, 5-minute lifetime, bound to purpose, merchant and session) instead of a session token.
- The Dashboard polls the existing `/poll/{nonce}` to get the grant id. The address-change request must carry it, and using it consumes it.
- Dev sign-in endpoints can't mint grants. Outside Development, step-up has no bypass.

**Rationale**: this reuses the only sign-in merchants have, binds the grant tightly, and needs no new credential.

**Alternatives considered**:
- Checking how recently the session itself signed in: `MerchantSession` doesn't record that, and the session slides for 30 days.
- Email or TOTP: DigiPay holds neither.

## R7. Address-change waiting period (FR-005 to FR-007)

**Decision**:
- A `PayoutAddressChange` row has `status: pending → applied | cancelled` and `EffectiveAt = RequestedAt + 48h`.
- The worker applies due changes at the start of each cycle.
- The planner skips any store with a pending change (FR-006).
- Cancelling is a session-only call (`dps_`) and needs no grant.
- A first address applies at once when the store's held and still-confirming DD are both 0, checked when the request is made.

## R8. Worker design and timing

**Decision**: `DdPayoutWorker` is a hosted service with a 30-second `PeriodicTimer`, using a DI scope per tick (the `WebhookRetrier` pattern). Each cycle:

1. Check node health with `GET /health`. Unreachable or not enabled → mark due payouts `waiting:node` and skip sending (FR-034).
2. Scan receives and credit the ledger.
3. Reconcile `checking` payouts, then move `sent` payouts to `confirmed`.
4. Apply due address changes.
5. Plan payouts per store:
   - **Skip the store** if it has no address, a pending change, a store pause, the global pause, or an automatic pause after 3 failures.
   - **As soon as confirmed** (default): pay whatever is confirmed and at least the minimum.
   - **Once a day** (time and time zone): pay when the slot comes.
   - **Threshold**: pay when the held amount reaches it, or once the oldest credit is 7 days old.
   - **Explicit request**: "Pay out now" or "Retry now".
6. Check the fee reserve and coverage. `reserve_healthy` false → `waiting:fee`. Coverage short (R4) → `waiting:coverage`, and alert the operator.
7. Send, at most one payout per store per cycle.

The node health status and the scanner's last success time are kept for the Dashboard's "last checked" display (FR-035).

**Rationale**: the same loop style as the existing services, and fast enough for SC-002.

## R9. Webhooks that aren't about a session (FR-032)

**Decision**:
- Add `WebhookDispatcher.DispatchEventAsync(PayStore, string eventName, object data)`, which uses the same HMAC headers.
- The serialized body is stored in a new `WebhookDelivery.PayloadJson` column, along with `PayoutId`.
- `WebhookRetrier` replays `PayloadJson` rows. Today it skips rows with no `SessionId`.
- Events: `payout.sent`, `payout.confirmed`, `payout.failed`, `payout_address.change_requested`, `payout_address.change_cancelled`, `payout_address.changed`. See [contracts/webhooks.md](contracts/webhooks.md).

## R10. Operator pause and alerts (FR-018, FR-021, FR-023, FR-037)

**Decision**:
- **Global pause**: the `DigiPay:Payouts:Paused` setting (env `DIGIPAY_PAYOUTS_PAUSED`) for incidents, plus `POST /v1/pay/operator/payouts/pause|resume`, optionally scoped to one store. The endpoints need `X-DigiPay-Operator-Key`, compared against `DIGIPAY_OPERATOR_KEY`, and return 503 when the key isn't configured (fail closed, matching NodeApi's write key).
- **Pause state** is stored in an `OperatorSetting` row and in a `PayStore.PayoutsPausedByOperatorAt` column.
- **Alerts** are structured `ILogger` warnings or errors with a stable `EventId` name (`DdPayoutAlert`) and an `alert` property:
  - `fee_reserve_low`
  - `coverage_short`
  - `node_unavailable_6h`
  - `reversal_absorbed`
  - `unattributed_receive`
  - `store_auto_paused`

  Railway alert rules match these, following the existing `DdFeeReserveMonitor` pattern.

**Alternatives considered**: an operator dashboard UI. That is out of scope here, and logs plus the endpoints are enough at today's scale.

## R11. Deployment prerequisite

**Finding**: `docs/RAILWAY_NODE.md` deploys production NodeApi with `DIGIDOLLAR_WALLET` blank, which means read routes only. Production can't create DD sessions or send payouts until a node with the `digipay-hot` wallet and a funded DGB fee reserve is running.

**Decision**: treat this as a release prerequisite. Before release:
- update `docs/RAILWAY_NODE.md` with the wallet, the write key shared with Pay.Api, and the size of the fee reserve
- add a release checklist item to quickstart
- have the payout worker log one startup warning and stay idle when the node reports DD disabled

## R12. Constitution follow-up

**Finding**: the constitution says nothing about a DigiPay server holding merchant funds. Principle I covers only wallet users' keys. Spec 001 already called node-wallet `senddigidollar` custodial.

**Decision**:
- **Not blocking**: this feature reduces existing custody and doesn't add any.
- **Constitution PR (MINOR)**: propose a separate PR adding a "DigiPay custodial DD" clause. It would allow holding only until payout and require a published balance, full coverage and payout-address protections.
- **Copy fixes in this feature**: `Home.razor` and `SELF_CUSTODY.md` must stop calling DD non-custodial.

## R13. Faking the DD node in tests

**Decision**:
- Add `FakeDdNode`, an `HttpMessageHandler` scripted per route: `/health`, `/address/new`, `/transactions`, `/send`, `/sends`, `/tx/{id}/status`. Register it with `PayFactory.Create(..., configure: s => s.AddHttpClient<DigiDollarNodeClient>().ConfigurePrimaryHttpMessageHandler(() => fake))`.
- Tests drive the worker's steps by calling `DdPayoutWorker.RunOnceAsync()` through the service provider, not by waiting on the timer.
- The NodeApi tests extend `DigiDollarWalletScopedAuthTests` so the new route is classified as wallet-scoped.
