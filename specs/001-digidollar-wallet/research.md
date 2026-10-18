# Research: DigiDollar in the DGB Wallet

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-09-27

Sources: existing repo code (see plan.md Project Structure), DigiByte-Core `feature/digidollar-v1`
branch docs (`digidollar/TECHNICAL_SPECIFICATION.md`), repo memory
`/memories/repo/digidollar-integration-plan.md`.

> **Caveat on sources.** `TECHNICAL_SPECIFICATION.md` is a design document written for v8.26 and
> it has drifted from what shipped. For example, it lists 8-of-15 oracles where mainnet runs
> 7-of-35, and its example address version bytes (`0x1F`/`0x7F`/`0x6F`) differ from the
> two-byte versions (`0x52 0x85` etc.) that the existing validator uses and that match real
> `DD…` addresses. **Before any consensus-critical code is merged, verify every byte-level
> decision below against the v9.26.5 source and against test vectors captured from a
> testnet26 node** (R8).

---

## R1 — DD transfer transaction format  (RESOLVED 2026-10-19, T001/T003)

**Source**: DigiByte-Core `feature/digidollar-v1`, `src/primitives/transaction.h`,
`src/digidollar/txbuilder.cpp` (`BuildTransferTransaction`), `src/digidollar/validation.cpp`
(`ValidateTransferTransaction`), `src/digidollar/scripts.cpp` (`CreateDigiDollarP2TR`). The v9.26.6rc1
release notes ("Thaw Day") state no change to the transfer format, the $1 minimum output, or the
$100,000 limit.

**Decision (corrects the earlier draft of R1, which described the MINT collateral leaf, not a transfer)**:

- **Version**: `nVersion = (type << 24) | (flags << 16) | 0x0770` where `DD_TX_VERSION = 0x0D1D0770`
  and `DD_VERSION_MASK = 0x0000FFFF`. For a TRANSFER (type 2, flags 0) this is **`0x02000770`**. A
  tx is identified as DD by `(nVersion & 0xFFFF) == 0x0770`.
- **DD outputs**: `nValue = 0`, scriptPubKey = canonical P2TR, i.e. `OP_1 <32-byte key>` (34 bytes,
  witness v1). **There is no script tree and no amount in the script.**
- **Amounts**: carried in exactly one `OP_RETURN` output: `OP_RETURN "DD" CScriptNum(2) CScriptNum(cents)...`,
  one `CScriptNum` per DD output, recipient outputs first, then the DD change amount if positive. The
  builder places it last (consensus does not require a position). The OP_RETURN amount count must match the
  number of canonical zero-value P2TR outputs.
- **Inputs**: DD inputs first, then DGB fee inputs. DD input amounts are read by the node from the
  creating transaction's OP_RETURN (txindex). **Unconfirmed DD inputs are rejected**
  (`dd-input-amounts-unknown`), so only DD UTXOs with at least 1 confirmation can be spent.
- **Conservation**: sum(DD in) must equal sum(DD out) (`transfer-dd-conservation-violation`).
- **Limits**: each DD output amount must be `> 0`, at least the protocol minimum (`minOutputAmount`, $1.00
  per the spec and release notes) and at most 10,000,000 cents ($100,000). This applies to the change
  output too (R15).
- **DGB fee inputs**: the builder does not check script type (P2WPKH is fine), and DGB change defaults to
  P2WPKH when it is at least 1000 sats.
- **Fee**: builder policy `fee = max(vsize × feeRate / 1000, MIN_DD_TX_FEE)` with
  `MIN_DD_TX_FEE = 10,000,000 sat` (0.1 DGB). No consensus fee check was found in the transfer validator.
  vsize estimate: 110 witness bytes per input plus a 35% margin (our estimate may be tighter, but never
  below the 0.1 DGB floor).
- **Signing**: key-path Schnorr spend of each DD input with the tweaked key, `SigHash.Default`. The
  transfer validator's witness check is currently a placeholder, so signing correctness is verified
  against a real node (T002 vectors, quickstart step 4).

**Closed verification items**: (1) amounts are `CScriptNum` cents in the OP_RETURN, not in any script; (2)
the metadata OP_RETURN IS required on TRANSFER; (3) there is no leaf: DD token outputs are key-path only;
(4) DGB fee inputs may be P2WPKH; (5) minimum fee is the 0.1 DGB builder policy.

**Still to confirm on testnet26 (T002)**: that a transaction built this way is accepted by a v9.26.5 node
and that the sighash type is `Default`. The source is a development branch, not the v9.26.5 tag.

**Thaw Day**: v9.26.6rc1 activates new DD block rules at testnet26 block 432,100 and mainnet block
24,490,000 (about 1 November 2026). Nodes, NodeApi, oracle services and wallet backends must run the
final v9.26.6 build before that height (docker image tag in `docker-compose.testnet.yml`). It does not change
the wallet's transaction format.

**Alternatives considered**: Node-side `senddigidollar` (custodial, violates FR-026).

---

## R2 — DD key derivation from the existing seed

**Decision**: Derive DD keys from the user's existing BIP39 seed on a BIP86 Taproot path:

- Receive: `m/86'/{coin}'/0'/0/i`
- Change: `m/86'/{coin}'/0'/1/i`

with `coin = 20` on mainnet. For testnet the wallet's existing `HdKeyDerivation` uses coin type 20 for every
network, whereas DigiByte Core descriptor wallets use coin type 1 on testnet (`tr(.../86h/1h/0h/0/*)`, as in
quickstart step 2). **Open decision, gated by T002**: the DD derivation takes the coin type as an explicit
parameter, defaulting to Core's convention (20 mainnet, 1 testnet) so keys restore into Core; confirm against a
Core-generated vector before shipping.

**The address encodes the final output key.** Per `CreateDigiDollarP2TR`, the DD output key is the BIP86
tweak of the internal key with no script tree (`CreateTapTweak(nullptr)`), and a recipient's DD address
decodes to that output key, which is used as-is in `OP_1 <key>`. So: internal key from the BIP86 path →
output key = `internal + H_TapTweak(internal) · G` (BIP341 key-path tweak, no merkle root) → DD address =
Base58Check(version bytes ‖ output key). Signing uses the tweaked private key.

The standard gap limit of 20 is used for the restore scan (R12).

**Rationale**: One seed backs up both DGB and DD, so no new backup phrase is needed and Principle I holds.
BIP86 is the standard single-key Taproot path and is verifiable against the published BIP86 test vectors
(independent of a node).

**Alternatives considered**: A private DD-specific path such as `m/7700'/…` (breaks interoperability and
restore in other wallets). Reusing BIP84 keys as x-only keys (mixes script types on one key).

**Consequence**: WIF-imported, watch-only (xpub), and multisig wallets have no seed to derive from.
**DigiDollar is available only for seed-based wallets in v1** (FR-030).

---

## R3 — Addresses, balances, and the lookup index

**Finding (corrects the earlier draft)**: Because a DD address encodes the final output key and DD outputs are
plain `OP_1 <key>` P2TR scripts, **the scriptPubKey is a pure function of the address**
(`5120 ‖ key`). The amount is NOT in the script: it is in the creating transaction's OP_RETURN. So a plain
script lookup finds DD outputs, but a node cannot report DD *amounts* or *history* by address without parsing
OP_RETURNs, and `scantxoutset` cannot see the mempool or spent history.

**T001 outcome**: no non-wallet node RPC that returns DD balances or history by address was found in the
source, so the NodeApi keeps a **DD output index**. It is simpler than first planned: it decodes zero-value
canonical P2TR outputs plus the single DD OP_RETURN (no leaf-script decoding).

**Decision**: A `BackgroundService` in `DigiByte.NodeApi` walks blocks from DD activation (mainnet
23,869,440), selects txs where `(nVersion & 0xFFFF) == 0x0770`, pairs each zero-value canonical P2TR output
with its amount from the OP_RETURN (recipient order, then change), records outpoint, output key, amount in
cents and creating txid, marks spends, tracks the mempool, and keeps this in SQLite keyed by output key.
Read endpoints serve UTXOs, balances, history, and tx status by DD address (contracts:
[nodeapi-digidollar-read.md](contracts/nodeapi-digidollar-read.md)). MINT and REDEEM txs are indexed only far
enough to keep spend tracking correct (their DD outputs follow the same OP_RETURN rule).

**Rationale**: The index holds only public chain data and needs no keys.

**Alternatives considered**:
- Import each user's descriptor as watch-only into the node: rejected (builds a server-side grouping of one
  user's addresses, per-user wallet management, shared node wallet).
- `scantxoutset` per address plus `getrawtransaction` for amounts: rejected for history and mempool.
- Client-side block scanning in the browser: rejected (bandwidth and battery on mobile).

---

## R4 — Mixed-input signing (Schnorr DD inputs + ECDSA DGB inputs)

**Decision**: Build the transaction manually with NBitcoin 9.0.5 and sign per input:

- Each DD input is a key-path Taproot spend of `OP_1 <outputKey>`, signed with `SigHash.Default` using the
  BIP86-tweaked private key (R2). No script path, no leaf.
- Each DGB input is signed with the existing P2WPKH path.
- The BIP341 sighash commits to **all** prevouts (amount and scriptPubKey), so the UTXO response MUST return
  `scriptPubKey` (`5120 ‖ outputKey`) and `valueSats` (0 for DD) for every input, DD and DGB.
- Outputs: recipient DD outputs (value 0), DD change output (value 0, only if change is at least 100 cents),
  DGB change output (only if at least 1000 sats), and the DD OP_RETURN (R1).

**Rationale**: NBitcoin handles BIP341 sighash and Schnorr signing. The DD-specific parts are only the
version, the zero-value P2TR outputs, and the OP_RETURN.

**Alternatives considered**: A separate secp256k1 WASM library (extra dependency; NBitcoin is already used).

**Risk**: `TransactionBuilder` knows nothing about DD conservation or zero-value P2TR outputs, and the repo
disables its dust prevention, so **build DD txs manually**.

---

## R5 — Fee handling

**Decision**: The fee is paid from the user's existing DGB (BIP84) UTXOs, selected with the
existing `UtxoSelector.SelectLargestFirst`. The fee is `max(policyMinDdFee, feeRate × vsize)`,
where `policyMinDdFee = 0.1 DGB` (`MIN_DD_TX_FEE = 10,000,000 sat`, confirmed in R1) is hardcoded alongside the FR-012 limits. vsize is estimated at
roughly 58 vB per P2TR key-path input, 68 per P2WPKH input, and 43 per P2TR output.

**User control (FR-014, clarified 2026-10-19)**: The fee is automatic only. The Send screen has no
fee presets or custom entry, so the user can never choose a fee below the protocol minimum. The
`feeRate` input comes from the wallet's existing fee estimation, and the result is clamped to
`policyMinDdFee` as a floor.

**Rationale**: Satisfies FR-013 and FR-014 (block the send and show the DGB fee). One computed
fee removes a class of rejected-at-broadcast failures.

---

## R6 — Numeric thresholds left open by the spec

| Threshold | Decision | Rationale |
|---|---|---|
| Oracle stale (FR-019) | Price older than 20 blocks (~5 min) | Matches the protocol's `priceValidBlocks = 20` |
| Node lag (FR-020) | Node tip > 6 blocks behind the best-known height | Same as the confirmation depth; ~90 s |
| Send rate limit (FR-016) | 5 sends per rolling 60 s per unlocked session | Client-side guard; resets on lock/unlock |
| Degraded state (FR-027) | ERR active, volatility freeze, or stale oracle | Taken from `getprotectionstatus` / `getdigidollarstatus` |
| Poll interval (FR-010) | 15 s in foreground (1 block), paused when hidden | Meets SC-002 with margin |

---

## R7 — Existing custodial WIP and the write key

**Findings**:
- `DigiByte.Web/wwwroot/appsettings.json` has `DigiDollar.WriteKey`. Anything in `wwwroot` is
  **publicly downloadable**, so a real key placed there would expose the NodeApi write endpoints
  (OWASP A01/A05).
- `DigiByte.Web/Services/DigiDollarService.cs` calls `/send`, `/mint`, `/redeem`, and
  `/address/new`, all of which move funds on the node's shared wallet.

**Decision**:
- Remove `WriteKey` from the wallet config and `AttachWriteKey` from the wallet client.
- Replace the wallet's DD service with the non-custodial `DigiByte.Wallet` service.
- Delete the Mint, Redeem, and Positions pages from the wallet (FR-028).
- Keep the NodeApi wallet-scoped endpoints, but only for DigiPay's merchant hot wallet, behind
  `NODEAPI_WRITE_KEY`, which stays server-side in `DigiByte.Pay.Api`.
- Add a NodeApi CORS rule so the wallet origin can't call the write group at all.

---

## R8 — Test strategy (Constitution III)

**Crypto (unit, xUnit)**:
- Address: encode ↔ decode round trip on mainnet/testnet; reject a DGB bech32 address, a wrong
  network, a bad checksum, the wrong length, surrounding whitespace, and an empty string.
- Derivation: fixed BIP39 test mnemonic → expected DD addresses at `/0/0`, `/0/1`, `/1/0`, with
  expected values **captured from a testnet26 node** that imported the matching `tr()`
  descriptor.
- Transfer builder: a golden-vector tx (captured from a testnet26 DD send whose inputs came from
  the test mnemonic) must match byte for byte before signing. Also check sighash, conservation
  violation → throws, below $1.00 → throws, above $100,000.00 → throws, insufficient DGB for
  fee → throws, and that the change output goes to a fresh `/1/i` key.

**Wallet (unit)**: `DigiDollarService` against a mocked NodeApi covers balance aggregation
across addresses, the pending-send record lifecycle, degraded-state mapping, the rate limiter,
and the unavailable (503 / timeout) state.

**NodeApi (unit / integration)**: The indexer decodes a captured DD block and marks spends. The
read endpoints return 503 when disabled and 400 on an invalid address.

**E2E**: Manual testnet26 run per [quickstart.md](quickstart.md). Do this before enabling
`SendEnabled` anywhere.

---

## R9 — Feature flags and network gating

**Decision**: `DigiDollar:ViewEnabled` and `DigiDollar:SendEnabled` live in
`wwwroot/appsettings.json` (public, non-secret). Effective visibility =
`ViewEnabled && network ∈ {mainnet, testnet} && walletHasSeed`, and send additionally requires
`SendEnabled`. Regtest always hides DD (FR-029). A NodeApi `503` or an `enabled:false` health
response puts the UI into the FR-003 unavailable state at runtime.

---

## R11 — On-device storage protection (FR-008)

**Decision**: Persist the DD address list, cached history/balance snapshot, derivation indexes, and
`PendingDdSend` records as one JSON blob per wallet, encrypted under a store secret derived from the unlocked seed (HKDF-SHA256, `DigiDollarStore.DeriveStoreSecret`) through the
existing `ICryptoService` (same primitive that protects the seed). Blobs are readable only
while the wallet is unlocked. They contain addresses, txids, amounts, and raw signed tx hex, and
never keys or seed material.

The one exception is a plain, non-sensitive boolean `dd.used` per wallet (set the first time DD
is opened) that drives the neutral "DigiDollar is temporarily unavailable" home line when the
view flag is off (FR-002). It is readable while locked, and it reveals only that the device
opened DD at least once.

**Rationale**: The address graph is derived from the same seed as the keys, and encryption costs
little. The marker is separate because the flag-off line must render without decrypting.

**Alternatives considered**: Plaintext storage (leaks the user's address set to anyone with
device access). Re-scanning every session (slow on mobile and loses pending sends across reload).

---

## R12 — Restore scan (FR-007)

**Decision**: When a seed-based wallet has no saved DD address list (restore, new device, or
cleared storage), run `ScanAsync` on the first DD open:

1. For each chain (receive `/0/i`, change `/1/i`), derive a window of 20 addresses starting at
   the next unchecked index.
2. Query the NodeApi batch history endpoint (limit 200 addresses/request, so one call covers both
   chains' windows).
3. If any address in the window has activity, advance past the highest active index and repeat.
   Stop after a full window of 20 with no activity.
4. Persist the discovered addresses and set `NextReceiveIndex` / `NextChangeIndex` to
   `highestActive + 1`, then compute balance and history from the results.

The UI shows a `Scanning` state with progress ("Searching for your DigiDollar…"). The scan is
non-blocking (DGB flows are unaffected, Principle V), is cancellable by navigating away, resumes
from the last checked index, and is re-run by the manual refresh control. If the backend is
unreachable mid-scan, show the unavailable state and do **not** persist a partial result as final
(keep `ScanComplete=false` so the next open retries).

**Rationale**: A scan avoids the "$0.00 but funds are on-chain" failure after restore, using the
same gap-limit convention the wallet already applies to DGB.

**Privacy note**: The scan submits batches of derived addresses to the NodeApi, which can link
them as one user for that request. That is the same exposure as the regular batch balance call
and is acceptable under the relay model (R3).

---

## R13 — Pending-send lifecycle (FR-017)

**Decision**: Persist a `PendingDdSend` at broadcast with its spent outpoints. Each refresh calls
the new tx status endpoint (contracts) for the txid:

- `mempool` or `confirmed` → stay `Pending` (until 6 confirmations, then `Confirmed`).
- `unknown` → accumulate **observed** absence in `MissingObserved`: each successful `unknown` check adds
  the time since the previous successful check, **capped at 2 minutes**. `FirstMissingAt` records the first
  miss. Reset everything if the tx is seen again. When `MissingObserved ≥ 30 min`, mark `Failed`, release the
  reserved outpoints, and show a notification (then drop the record once the user was told).
- NodeApi unreachable (or the status call failing) → nothing changes and `LastCheckAt` does not move, so
  the next successful check can credit at most 2 minutes: an outage or a closed app never counts as absence.
- Rebroadcast `RawTxHex` once when first seen `unknown`, since it may just be a node mempool
  restart.

Available DD balance excludes outpoints reserved by non-`Failed` pending sends.

**Rationale**: Reserving funds while a tx may still confirm prevents conflicting double-spends.
The absence-based timer avoids failing a slow but valid tx, and ignoring offline time prevents
false failures.

---

## R14 — Offline and unreachable behavior (FR-003)

**Decision**: On NodeApi `503`, timeout, or network failure, the DD UI enters `UnavailableCached`:
the last cached encrypted balance and history snapshot are shown with a "cached, as of
{time}" label, Send is disabled ("cannot reach network"), and nothing is queued. Receive still
works because addresses are derived locally. Recovery is automatic on the next successful
refresh.

**Alternatives considered**: Offline queue or a manual-broadcast fallback (rejected in
clarification: risk of stale fee or price and unclear user intent).

---

## R15 — Change minimum and "Send all" (FR-012)

**Decision**: The $1.00 (100 cents) output minimum applies to the DD change output as well as the
recipient output. At Draft → Confirming, compute `changeCents = selectedInputsCents − amountCents`
(inputs chosen to cover the amount). The send is valid only if `changeCents == 0` or
`changeCents ≥ 100`. If `0 < changeCents < 100`, block with `dd.send.err.changeBelowMin`
(shown inline: send the full balance or leave at least $1.00) and show a **Send all** action.

**Send all** sets the amount to the entire spendable DD balance (confirmed DD minus outpoints
reserved by pending sends), selects all spendable DD UTXOs, and builds the tx with **no DD change
output**. It is still subject to the $1.00 minimum and the $100,000.00 maximum; if the balance
exceeds the maximum, the standard `aboveMax` message applies (the user sends a smaller amount).
The DGB fee is still paid from DGB UTXOs and checked per FR-013.

**Coin selection note**: choose DD inputs largest-first, but prefer a combination that yields
zero change or change ≥ 100 where one exists, to avoid avoidable blocks.

**Alternatives considered**: Silently adding the remainder to the recipient (surprising,
changes what the user typed). Emitting a below-minimum change output (rejected by the network).

---

## R16 — Price-unavailable and network-not-active states (FR-003, FR-019)

**Decision**:
- `OraclePriceAvailable == false` when `/health` returns `oracle: null` (or a zero price). The UI
  hides the home DGB USD equivalent, shows `dd.price.unavailable` ("Price unavailable") in the
  price slot on DD pages, and raises a non-blocking `priceUnavailable` banner. Balance, receive,
  and send are unaffected, since DD amounts do not depend on the DGB price. This state **is**
  `IsDegraded`: the confirm dialog adds a "price unavailable" warning line (FR-027), and sending
  stays allowed.
- `DeploymentActive == false` when `/health` returns `deploymentActive: false`. The UI enters
  `NotActive`: it shows `dd.notActive` ("DigiDollar is not active on this network yet"),
  hides receive and send actions, and rechecks on every refresh. It is distinct from `Unavailable`
  (backend unreachable, R14) and from the flag-off state.

**Rationale**: Showing "$0.00" for a missing price or letting users share addresses before the
network can deliver funds would look like bugs or cause stuck payments.

---

## R17 — Anonymous usage counts for SC-005 (FR-023)

**Decision**: SC-005 is measured with a first-party, cookieless aggregate counter on the NodeApi, not
with Google Analytics. The wallet already loads GA through `wwwroot/js/analytics.js`, but GA assigns a
per-device client ID, which conflicts with SC-005 and FR-023 ("no per-user or per-device identifier").

- `POST /api/metrics/event` accepts only an allowlisted event name (`dd_section_view`, `home_view`,
  `dd_send_attempted`) with an empty body. It increments a per-day, per-event count and stores and logs
  nothing else (no IP, user agent, cookie, or identifier).
- `GET /api/metrics/summary` returns the daily counts. SC-005 is `dd_section_view / home_view` over
  the first 30 days.
- Abuse controls: the POST is rate-limited per client IP with the IP held in memory only (never stored or logged), and CORS allows only the wallet origin. The summary endpoint requires `X-NodeApi-Key` (operator only) and fails closed when no key is configured.
- The wallet sends events fire-and-forget (`UsageCounter`) and never blocks on them. If the NodeApi is
  unreachable the event is dropped, not queued.

**Rationale**: Satisfies FR-023 and SC-005 together, keeps the wallet a static host plus public-chain
relay (Constitution II), and adds no third-party dependency.

**Alternatives considered**: GA custom events (per-device client ID). A hosted privacy-first analytics
service (new third-party dependency and CSP change).

**Note**: counts are aggregate, so they cannot de-duplicate repeat views by one user. SC-005 therefore
measures views as a share of home views, as the spec words it, not unique users.

---

## R10 — Explorer links (FR-009)

**Decision**: Link to the txid on the configured explorer (`digiexplorer.info` for mainnet,
the testnet explorer for testnet). DD txs are ordinary chain txs, so explorers render them even
if they don't decode the DD amounts.
