# Implementation Plan: Wallet Uses Our Own Node When Ready

**Branch**: `feature/wallet-own-node-source` (spec directory `005-wallet-own-node-source`) | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-wallet-own-node-source/spec.md`

## Summary

Teach the wallet to use the Railway node (feature 004) only when the node proves itself ready, and to fall back to the public explorers the moment it is not. A small **node readiness monitor** in `DigiByte.Wallet` polls the node API's `/api/health` on a short timeout with no retries, cross-checks the node's block height against an explorer, and runs a state machine with a stability window before promoting the node and instant demotion when it stops being ready. `FallbackBlockchainService` asks the monitor which order to use: node first only when `Active`, explorers first otherwise, and never the node at all while the feature is on and the node is not ready (a syncing node would answer with wrong balances). With the feature off, behaviour is byte-for-byte today's. The Web app gets a status indicator with an on-demand detail panel (names the active source and carries the privacy note), a banner only for real degradation, and a developer-only diagnostics and comparison page. On/off, node URL and thresholds come from a runtime `config.json` written at container start, so no rebuild is needed; the strict CSP gets one exact first-party origin for the node API.

## Technical Context

**Language/Version**: C# on .NET 10 (Blazor WebAssembly PWA); xUnit tests; Tailwind CSS for UI

**Primary Dependencies**: existing `DigiByte.Wallet` services (`FallbackBlockchainService`, `NodeApiBlockchainService`, explorer services), `IHttpClientFactory`, Blazor DI; nginx (static hosting) for the runtime config file

**Storage**: none new. Readiness state is in memory per session; last-known balances use the existing offline cache

**Testing**: `DigiByte.Wallet.Tests` (monitor state machine with a fake clock, health fetcher and explorer-height source; fallback ordering; broadcast ordering, including the existing `FallbackBroadcastTests`); a local shell test of the config-writing script and a manual check of runtime config loading (quickstart, T043; there is no Web test project); manual quickstart against the real node and against a stub health endpoint

**Target Platform**: browser (PWA); static hosting on Railway behind nginx

**Project Type**: web application (wallet client) consuming an existing HTTP service

**Performance Goals**: switch to the node within 3 minutes of it being ready (SC-002; 2 minute stable window plus the 30 second check interval); back to explorers within 35 seconds of it not being ready (SC-003; 30 s check interval plus the 3 s probe), or sooner after repeated failed node reads; no screen delayed by more than 1 second because of the node (SC-001)

**Constraints**: strict CSP must not be relaxed beyond one exact origin; no keys or signing involved with the node (FR-014); core flows never wait on the node (FR-007); the node's address is public chain data, not secret

**Scale/Scope**: one monitor per session; one health request per check interval (default 30 s) plus one explorer height lookup; a handful of new UI components

## Constitution Check

*GATE: passes before Phase 0; re-checked after Phase 1.*

| Principle / rule | Status | Notes |
|---|---|---|
| I. Self-custody | PASS | Only public chain requests and signed transactions go to the node; no secret material (FR-014). |
| II. In-browser cryptography | PASS | Nothing about signing changes; servers stay public-chain relays. |
| III. Test-first (Crypto/Wallet) | PASS (planned) | The monitor and the ordering logic live in `DigiByte.Wallet`; tests are written first in `DigiByte.Wallet.Tests`, with negative paths (malformed health, timeouts, flapping, explorer unreachable). |
| IV. Boundary discipline | PASS | Logic in `DigiByte.Wallet`, UI and config loading in `DigiByte.Web`; no new reference direction. |
| V. Resilience / graceful degradation | PASS | Spec defines every degraded state; core flows never block on the node; offline behaviour unchanged. |
| CSP | **ACCEPTED (2026-10-29)** | One exact first-party origin is added to `connect-src` (R4). No wildcard, no third party. Flag as security-relevant in the PR. |
| Secret hygiene | PASS | Runtime config holds no secrets. |
| Docs | Required | Privacy documentation line (FR-014a), runbook note for the config variables. |

Post-design re-check: only the CSP item remains, and it is accepted by the maintainer; see Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/005-wallet-own-node-source/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── runtime-config.md          # config.json keys, defaults, deploy variables
│   ├── node-readiness.md          # how the wallet interprets /api/health (ready predicate)
│   └── status-ui.md               # indicator, detail panel, banner and dev view wording and states
└── tasks.md                       # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/Services/
├── NodeSourceOptions.cs           # NEW: enabled, url, check interval, stable period, height tolerance
├── NodeReadiness.cs               # NEW: health snapshot + ready predicate (pure)
├── NodeSourceMonitor.cs           # NEW: state machine (Disabled/Unavailable/NotReady/Stabilizing/Active), injectable clock
├── INodeHealthSource.cs           # NEW: fetches /api/health (short timeout, no retry)
├── NodeHealthHttpSource.cs        # NEW: HttpClient implementation
├── INodeSourceState.cs            # NEW: read-only view used by the fallback service and the UI
├── FallbackBlockchainService.cs   # CHANGE: read/broadcast order driven by INodeSourceState
└── NodeSourceComparer.cs          # NEW: dev-only comparison of node vs explorer answers
src/DigiByte.Web/
├── Program.cs                     # CHANGE: load config.json, register monitor, short-timeout health client
├── wwwroot/index.html             # CHANGE: one exact origin in CSP connect-src
├── Services/NodeSourceStatusService.cs   # NEW: UI-facing wrapper (events, wording)
├── Components/Shared/NodeSourceIndicator.razor   # NEW: small status dot
├── Components/Shared/NodeSourceDetail.razor      # NEW: on-demand detail (source, state, privacy note)
├── Components/Shared/NodeDegradedBanner.razor    # NEW: banner for real degradation only
├── Pages/DevNode.razor            # NEW: diagnostics + comparison, only when developer view is on
└── Layout/MainLayout.razor        # CHANGE: host indicator and banner
docker/web/
├── Dockerfile                     # CHANGE: add entrypoint script
├── nginx.conf                     # CHANGE: config.json served no-cache
└── 40-write-config.sh             # NEW: writes config.json from environment at container start
tests/DigiByte.Wallet.Tests/
├── NodeReadinessTests.cs
├── NodeSourceMonitorTests.cs
├── FallbackNodePreferenceTests.cs
└── NodeSourceComparerTests.cs
docs/
├── RAILWAY_NODE.md                # CHANGE: wallet-side variables
└── (privacy doc)                  # CHANGE: one paragraph (FR-014a)
```

**Structure Decision**: Keep the decision logic in `DigiByte.Wallet` (testable without a browser), and keep the browser-specific parts (config loading, components) in `DigiByte.Web`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| CSP `connect-src` gains one first-party origin | The browser blocks calls to the node API unless the origin is listed | A wildcard such as `*.up.railway.app` would allow any Railway app; a header-based CSP injected per deployment would move a security control out of the page and is a bigger change than one reviewed origin (R4) |
