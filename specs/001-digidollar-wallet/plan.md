# Implementation Plan: DigiDollar in the DGB Wallet

**Branch**: `001-digidollar-wallet` | **Date**: 2026-09-27 (revised 2026-10-19 for the full clarification session 2026-10-19, incl. change-minimum, automatic fee, no-price, and not-active states) | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-digidollar-wallet/spec.md`

## Summary

Add non-custodial DigiDollar (DD) hold / receive / send to the Blazor WASM wallet on mainnet and
testnet26. DD keys are derived in-browser from the user's existing BIP39 seed on a dedicated
Taproot (BIP86-style) path, DD addresses are encoded with the DD Base58Check version bytes, and
DD transfer transactions (`nVersion = 0x0D1D0770 | (DD_TX_TRANSFER << 16)`) are built and
Schnorr-signed in the browser with the same `DigiByte.Crypto` library that signs DGB today. The
node is only a public-chain relay: `DigiByte.NodeApi` gains **address-scoped, read-only** DD
endpoints (DD UTXOs / balance / history by address, oracle price, network health) plus reuse of
the existing raw-tx broadcast. The existing custodial WIP (node-wallet `senddigidollar`, mint,
redeem, positions pages) is removed from the wallet surface because it conflicts with FR-026,
FR-028, and Constitution Principle I.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), Blazor WebAssembly for the wallet PWA

**Primary Dependencies**: NBitcoin 9.0.5 (already used; has Taproot / Schnorr / `TaprootBuilder`
primitives), ASP.NET Core Minimal APIs (NodeApi), `Microsoft.Extensions.Http.Resilience`
(Polly) for the `NodeApi` named client, Tailwind CSS (self-hosted)

**Storage**: Browser IndexedDB via existing `ISecureStorage` / `IndexedDbStorage` (DD address
list, DD derivation index, cached history, pending-send records). All of these are encrypted with
a key derived from the unlocked seed (HKDF) through the existing `ICryptoService` (FR-008, research R11). No server-side
storage added.

**Testing**: xUnit 2.9.3 in `tests/DigiByte.Crypto.Tests`, `tests/DigiByte.Wallet.Tests`,
`tests/DigiByte.NodeApi.Tests`; testnet26 end-to-end runs per [quickstart.md](quickstart.md)

**Target Platform**: Modern browsers (WASM) for the wallet; Linux container for NodeApi +
`digibyted` v9.26.5 with `digidollar=1`, `txindex=1`

**Project Type**: Web application — static PWA frontend + backend relay services (existing
monorepo layout)

**Performance Goals**: DD balance/history refresh ≤ 2 s p95 wallet→NodeApi→node (supports
SC-002 one-minute visibility); DD send build + sign ≤ 1 s in-browser on a mid-range phone
(supports SC-003 sixty-second send)

**Constraints**: Keys never leave the browser (Constitution I/II); strict CSP, no new CDNs;
DGB flows MUST keep working when DD endpoints are unavailable (Principle V, SC-008); DD network
limits hardcoded ($1.00 min output, $100,000.00 max per tx — FR-012)

**Scale/Scope**: One new wallet section (3 pages: dashboard, receive, send) + home-screen card;
3 new NodeApi read endpoints (batch UTXOs, batch history, tx status; addresses only in POST bodies) plus an extended health endpoint and 2 anonymous metrics endpoints; 1 new crypto module (Taproot derivation + DD tx builder)

**Plan-level defaults for thresholds the spec left numeric** (see research.md R6):

- Oracle stale banner (FR-019): oracle price older than **20 blocks (~5 min)**, matching the
  protocol's `priceValidBlocks`.
- Node lag banner (FR-020): node tip **> 6 blocks** behind the best-known network height.
- Send rate limit (FR-016): **5 sends / rolling 60 s**, counted per unlocked wallet session;
  resets on lock/unlock.
- Restore scan (FR-007): **gap limit 20** per chain (receive and change), run on restore and on
  first DD open with no saved address list (research R2, R12).
- Change minimum (FR-012): remaining DD change must be exactly $0.00 or ≥ $1.00; otherwise block,
  explain, and offer "Send all" (research R15).
- Fee (FR-014): automatic only, `max(policyMinDdFee, feeRate × vsize)`, no fee control (R5).
- Pending-send expiry (FR-017): **30 min** absent from both mempool and chain, then `Failed` and
  funds released (research R13).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Pre-design | Post-design |
|---|---|---|---|
| I. Self-Custody | No DD key, seed, or signing capability held by operator | **FAIL on existing WIP** (node-wallet `senddigidollar`, `/address/new` on shared `digipay-hot` wallet). **PASS for this plan**: DD keys derived from user seed in-browser; NodeApi is read-only + broadcast for the wallet. | PASS — contracts expose no wallet-scoped DD endpoints to the wallet |
| II. In-Browser Crypto | Signing happens in WASM; server never participates in signing | PASS — DD transfer built and Schnorr-signed in `DigiByte.Crypto` | PASS |
| III. Test-First (Crypto/Wallet) | Negative-path tests for derivation, address encode/decode, DD tx build/sign | PASS — test list in research.md R8; vectors captured from a v9.26.5 testnet26 node | PASS |
| IV. Boundary Discipline | Concepts live in lowest owning layer | PASS — DD address/derivation/tx in `DigiByte.Crypto`; DD service in `DigiByte.Wallet`; UI in `DigiByte.Web`; NodeApi stays independently deployable | PASS — current WIP `DigiByte.Web/Services/DigiDollarService.cs` moves to `DigiByte.Wallet/Services/` |
| V. Resilience | DD failure cannot block DGB flows; degraded states defined | PASS — DD client isolated behind its own service + feature flag; FR-003 unavailable state | PASS |
| Security: Address handling | Client-side network-aware validation | PASS — reuse `DigiDollarAddressValidator` | PASS |
| Security: Auth boundary | Mutating wallet→backend calls authenticated | PASS — the wallet's mutating calls are raw-tx broadcast (existing, public by nature) and the anonymous usage-counter event (unauthenticated by design: allowlisted name, empty body, rate-limited, no identifiers stored; research R17); the custodial write endpoints with `X-NodeApi-Key` are not called by the wallet | PASS |

**Gate result**: PASS for this plan, with one required corrective action: remove the custodial
wallet-facing WIP (see Complexity Tracking). DigiPay's custodial `digipay-hot` usage of
wallet-scoped endpoints is **out of scope** here and is not a wallet-constitution violation
(merchant hot wallet, merchant-operated); it is tracked by the DigiPay feature.

## Project Structure

### Documentation (this feature)

```text
specs/001-digidollar-wallet/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── nodeapi-digidollar-read.md   # Address-scoped read + broadcast endpoints
│   └── wallet-ui-contract.md        # Pages, routes, feature flags, UI states
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/
├── DigiByte.Crypto/
│   ├── Addresses/
│   │   └── DigiDollarAddressValidator.cs        # existing WIP; add Encode(XOnlyPubKey, network)
│   ├── KeyGeneration/
│   │   └── DigiDollarKeyDerivation.cs           # NEW: taproot path from existing seed
│   └── Transactions/
│       ├── DigiDollarScripts.cs                 # NEW: DD output script / taproot tree
│       └── DigiDollarTransferBuilder.cs         # NEW: build + Schnorr-sign DD transfer
├── DigiByte.Wallet/
│   ├── Models/
│   │   └── DigiDollarModels.cs                  # NEW: balance, utxo, tx, health records
│   ├── Storage/
│   │   └── DigiDollarStore.cs                   # NEW: seed-key-encrypted address list, cache, pending sends
│   └── Services/
│       ├── IDigiDollarService.cs                # NEW
│       ├── DigiDollarNodeApiClient.cs           # NEW: read client (health, utxos, history, tx status, broadcast)
│       └── DigiDollarService.cs                 # MOVED from DigiByte.Web, rewritten non-custodial
├── DigiByte.NodeApi/
│   ├── Endpoints/
│   │   ├── DigiDollarEndpoints.cs               # existing WIP; add address-scoped read group
│   │   └── MetricsEndpoints.cs                  # NEW: anonymous aggregate counters (research R17)
│   ├── Services/
│   │   └── DdOutputIndexer.cs                   # NEW: DD output index (research R3)
│   ├── Middleware/
│   │   └── WriteKeyAuthMiddleware.cs            # existing; fail closed for the wallet-scoped group
│   └── RpcClient/
│       └── DigiByteRpcClient.cs                 # existing; no wallet-scoped calls for wallet
└── DigiByte.Web/
    ├── Pages/
    │   ├── DigiDollar.razor                     # existing WIP; rework to non-custodial data
    │   ├── DigiDollarReceive.razor              # existing WIP; derive address locally
    │   ├── DigiDollarSend.razor                 # existing WIP; build/sign locally
    │   ├── DigiDollarMint.razor                 # REMOVE from v1 (FR-028)
    │   ├── DigiDollarRedeem.razor               # REMOVE from v1 (FR-028)
    │   └── DigiDollarPositions.razor            # REMOVE from v1 (FR-028)
    ├── Components/
    │   └── DigiDollar/                          # NEW: HomeCard, UnavailableLine, HealthBanner, SendForm, ConfirmDialog
    ├── Services/
    │   ├── DigiDollarService.cs                 # REMOVE (moved to DigiByte.Wallet)
    │   ├── DigiDollarFeatureFlag.cs             # existing; split into View / Send flags
    │   └── UsageCounter.cs                      # NEW: anonymous aggregate counters (research R17)
    ├── wwwroot/appsettings.json                 # DigiDollar:{ViewEnabled, SendEnabled}; drop WriteKey
    └── wwwroot/locales/{en,es}.json             # new dd.* keys (FR-025); other locales fall back to English

tests/
├── DigiByte.Crypto.Tests/
│   ├── DigiDollarAddressTests.cs                # NEW
│   ├── DigiDollarKeyDerivationTests.cs          # NEW
│   └── DigiDollarTransferBuilderTests.cs        # NEW (vectors from testnet26)
├── DigiByte.Wallet.Tests/
│   └── DigiDollarServiceTests.cs                # NEW (mocked NodeApi)
└── DigiByte.NodeApi.Tests/
    └── DigiDollarReadEndpointsTests.cs          # NEW
```

**Structure Decision**: Keep the existing monorepo layout. DD protocol concepts go in
`DigiByte.Crypto`, DD orchestration in `DigiByte.Wallet`, UI in `DigiByte.Web`, and chain reads
in `DigiByte.NodeApi` — matching Constitution Principle IV. No new projects.

## Complexity Tracking

| Violation / deviation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Removing existing custodial DD wallet WIP (service, 3 pages, `WriteKey` in wallet config) | Existing code signs on the node's shared wallet, which violates FR-026 and Constitution Principle I; mint/redeem/positions violate FR-028 | Keeping it behind a flag leaves a custodial code path shipped in a self-custody wallet, and it publishes a write key in public `wwwroot/appsettings.json` |
| New Taproot/Schnorr code path in `DigiByte.Crypto` | DD outputs are P2TR; non-custodial send is impossible without it | Node-side signing is the rejected custodial alternative |
| New address-scoped NodeApi read endpoints | Existing DD RPCs are wallet-scoped; a node can't report balances for keys it doesn't hold | Importing user descriptors as watch-only on the node was rejected: it builds a server-side map of which addresses belong to one user (privacy regression), and the node is shared (research R3) |
