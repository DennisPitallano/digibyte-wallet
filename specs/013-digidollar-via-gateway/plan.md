# Plan: DigiDollar through the SPV gateway (spec 013)

> **Deferred (2026-11-16).** The goal for now is only to show DD balance and history, and the explorer backend already does that
> (`DigiDollar:Backend=esplora`, re-checked live: `EsploraDigiDollarLiveTests` passes and recent mainnet DD transactions are served).
> The build default is back to `esplora`; viewing is switched on with `WALLET_DD_VIEW=true`. This plan stays here for when DD addresses
> should stop going to a third-party explorer.

Status: proposal, nothing built. Depends on spec 011 (gateway) and spec 001 (DigiDollar wallet).

## Goal

Let the wallet read DigiDollar (DD) balances, history and transaction status, and send DD, through the SPV gateway instead of a public
explorer or the node. DD addresses then go to our own gateway, not to a third party, and the data is checked against headers the
wallet keeps.

Out of scope (same as spec 001, FR-028): minting and redeeming. Out of reach for the gateway: the oracle price, oracle height and the
volatility and error flags. They come from the node's RPC, so the wallet keeps showing "unavailable" for them, as it already does in
explorer mode.

## What I checked in the code

| Finding | Where | Consequence |
|---|---|---|
| The gateway returns the full raw transaction and a Merkle proof for each match | `specs/011-p2p-spv-gateway/contracts/gateway-api.md` | The DD amounts can be read from the raw transaction on the wallet and checked against headers. |
| The block index records the script of every output, whatever its value | `Index/BlockIndexBuilder.cs` | The value-0 DD outputs are matched like any other output. |
| DD addresses are Base58Check with a 2-byte prefix (`DD...` on mainnet), not standard addresses. The script is `OP_1 <32-byte key>` and is a pure function of the address | `DigiDollarAddressValidator.cs` | **The gateway rejects them today**: it parses addresses with `BitcoinAddress.Create` (`AddressNetworkGuard.cs`). This is the first thing to change. |
| DD amounts come from the DD `OP_RETURN` in the same transaction. The parse rules exist for explorer JSON (`EsploraDigiDollarApi.ParseDdTx`) and for the node (`DdTxParser`) | wallet and NodeApi | A third parser for raw transactions is needed. Share the rules so the three cannot drift. |
| The gateway sees confirmed transactions only. Unconfirmed ones are visible only by transaction id | gateway contract | Incoming DD shows up after the first confirmation (about 15 s per block). Own sends are tracked locally already. |
| Index starts near block 24,563,018 (about 2 days). DD activated at block 23,869,440 (about 121 days ago) | `EsploraDigiDollarApi.MainnetActivationHeight` | Most real DD history is older than the index. See "Coverage". |
| DD view is off in production; send is on; oracle is off | `config.json` | Nothing user-visible changes until DD view is switched on. |

## Design

New backend `DigiDollar:Backend=gateway`: a composite `FallbackDigiDollarApi` that asks, in order, the gateway (only while its
readiness monitor says it is active) and then the existing backend (explorer, or node). A gateway that cannot answer correctly
(history older than its index, headers behind) declines, which is not a failure, and the next backend answers.

`GatewayDigiDollarApi : IDigiDollarNodeApi` maps the interface like this:

| Interface call | Gateway source |
|---|---|
| `GetHealthAsync` | `/v1/health` plus the monitor. `DeploymentActive` is `height >= activation`. Oracle fields stay null. |
| `GetUtxosAsync` | `/v1/scan` over the range, then the wallet derives DD outputs, spends and amounts from the raw transactions and checks each proof. |
| `GetHistoryAsync` | The same scan data. Paging through the `before` cursor. |
| `GetTxStatusAsync` | `/v1/tx/{txid}`: mempool when no height, confirmed with the tip distance otherwise. |
| `BroadcastAsync` | `/v1/broadcast`. Rejections map to `DdApiException` with the reject reason. |

## Work breakdown

**Phase 0: decisions and fixtures (0.5 day)**
- Settle the open decisions below.
- Capture 5 real mainnet DD transactions as raw hex (transfers, one mint) and confirm the gateway indexes them. This also tests the gateway's matching of value-0 outputs.

**Phase 1: gateway accepts DD addresses (1 day)**
- `AddressNetworkGuard`: add a DD branch using `DigiDollarAddressValidator`, producing the `OP_1 <key>` script. Keep the network check (a testnet DD address on the mainnet gateway is `wrong_network`).
- `/v1/scan`, `/v1/utxos`, `/v1/proofs` accept them. Update the API contract and `docs`.
- Tests: valid, wrong-network and malformed DD addresses; a scan that finds a DD output.

**Phase 2: shared raw transaction parser (1 day)**
- `DdRawTxParser` (in `DigiByte.Crypto`, next to `DigiDollarScripts`): raw hex to `DdTx`, with the same rules as the explorer parser.
- Parity tests: the same transaction as JSON and as raw hex must give the same result. Reuse the Phase 0 fixtures.

**Phase 3: `GatewayDigiDollarApi` (3 days)**
- Scan from a per-wallet DD sync height, in chunks within the gateway's range cap. Keep a small DD sync state like `WalletSyncState`.
- Derive the unspent DD outputs and the history; verify proofs with `GatewayProofChecker`; mark outputs spent by transactions in the scan or by the wallet's own pending sends.
- Decline (`GatewayDeclinedException`) when the needed range starts before the index.
- Tests with fake gateway responses: balance, history, spend detection, decline, bad proof, reorg.

**Phase 4: backend selection (1 day)**
- `Backend=gateway` wiring in `Program.cs`; `FallbackDigiDollarApi` with the monitor check and a short cooldown after failures.
- Show which backend answered in the status card and dev page (a "DigiDollar data" row).

**Phase 5: product text and spec (1 day)**
- Spec 001 amendments: the new backend, incoming DD appearing at first confirmation in gateway mode, and the privacy line (DD addresses go to our gateway).
- No change to the oracle banners.

**Phase 6: coverage (1 to 2 days of work, plus fill time)**: see "Coverage".

**Phase 7: verification (1 to 2 days)**
- Live check on mainnet with a small funded DD wallet: balance, history, a send, and a restore, compared against the explorer and the node. Failure drills: gateway down, gateway declining, gateway behind.
- DD view stays off until this passes.

Rough total: 10 to 12 working days, an estimate.

## Coverage (the part that decides how useful this is)

A DD wallet needs every DD transaction since its first one, and DD has been live about 121 days. The gateway's index covers about 2 days now.
Even a 90-day window ends about a month after DD activation. Options:

- **A. Checkpoint at DD activation and a window of about 130 days.** The index then covers all DD history. Estimated storage about 3 GB (from the 2 GB per 90 days estimate) and a first fill of several hours at the measured 30 blocks per second. The index keeps growing, so the window needs a rule (a floor at activation, or a rolling window plus a DD-only index for the older part).
- **B. DD-only index for the older range.** Download the old blocks but keep only DD-marked transactions. Small on disk, still hours of download.
- **C. Decline and fall back.** Ship without extra coverage: the gateway answers only wallets whose DD history is inside its window, everything else goes to the explorer. Safe, but the gateway would rarely answer a real DD wallet at first.

I'd ship C first behind the flag, since it is correct and small, then do A.

## Open decisions

1. **Sender on received transactions.** History shows a counterparty. For a receive, that is the sender, and a raw transaction doesn't carry the spent outputs. Either fetch the parent transaction by id (extra requests, capped), or show "Unknown sender" in gateway mode. I'd start with the cap.
2. **Order for DD.** Gateway first (more private, and checked) or explorer first (more complete today). Privacy says gateway first once coverage is there.
3. **Coverage option** A, B or C above.
4. **Testnet.** The gateway is mainnet only and DD also runs on testnet26. Testnet stays on the explorer or node.

## Risks

- DD addresses could need more than an address-format fix if some DD outputs aren't the canonical `OP_1 <key>` script. Phase 0 fixtures should catch this.
- The wallet derives DD state itself, so any parser difference from the node's rules would show a wrong balance. The parity tests and the live comparison guard against it.
- Incoming pending DD is invisible until confirmed. Users may read that as "not received yet" for about a minute.
- Index size and first-fill time if coverage option A is chosen.

## Rollout

1. Phases 1 and 2 first: they change nothing for users (the gateway accepts more input; the parser is unused).
2. Phases 3 and 4 behind `DigiDollar:Backend=gateway`; the default backend stays as it is.
3. Turn DD view on in a test deployment only after Phase 7 passes.
