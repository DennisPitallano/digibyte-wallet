# Implementation Plan: Railway-Hosted DigiByte Core Node

**Branch**: `feature/railway-digibyte-node` (spec directory `004-railway-digibyte-node`) | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-railway-digibyte-node/spec.md`

## Summary

Stand up a mainnet DigiByte Core **v9.26.7** full node (unpruned, `txindex`) in a **new Railway project**, together with a relocated copy of the existing `DigiByte.NodeApi` service. The node's RPC port is reachable only over Railway private networking; NodeApi is the only public HTTP surface and talks to the node privately. Work is mostly deployment assets and hardening of what already exists: a new Railway-specific node image/config (checksum-verified binary, credentials injected from environment, no `changeme` defaults), a production-safe NodeApi profile, a sync-aware health signal, alerting/cost guardrails, and an operator runbook. The wallet is repointed by changing `NodeApiUrl` only. The outside-project gateway (User Story 3) is deferred.

## Technical Context

**Language/Version**: .NET 10 (existing `DigiByte.NodeApi`); Bash entrypoint; DigiByte Core v9.26.7 prebuilt Linux x86_64 binary

**Primary Dependencies**: DigiByte Core (`digibyted`), Railway (project, volume, private networking, healthchecks, usage limits), existing NodeApi RPC client

**Storage**: Railway persistent volume mounted at the node data dir; full chain + txindex (size to be measured, see research R4). Plus a small (~1 GB) volume on `node-api` for its DigiDollar index and metrics SQLite files, so they survive redeploys

**Testing**: `dotnet test` for NodeApi changes (`tests/DigiByte.NodeApi.Tests`); scripted smoke checks from [quickstart.md](quickstart.md) for deployment behaviour (version, height vs explorer, RPC not public, restart without resync)

**Target Platform**: Railway (Linux containers, Pro plan), single region, single replica for the node

**Project Type**: Infrastructure/deployment + small web-service hardening (existing services, no new project)

**Performance Goals**: initial sync inside 24 h (SC-001, risk R5); steady state within 3 blocks of explorers (SC-003); post-restart recovery < 10 min (SC-002)

**Constraints**: $40/month target incl. $20 credit, $60 hard ceiling (SC-008, risk R4); RPC never public (SC-004); credentials only in Railway variables; node holds no wallets (FR-014) unless DigiDollar wallet-scoped routes are explicitly enabled (see R7)

**Scale/Scope**: one mainnet node, one NodeApi replica, wallet PWA + DigiPay as consumers; ~2 Railway services (+ optional DigiPay later)

## Constitution Check

*GATE: passes before Phase 0; re-checked after Phase 1.*

| Principle / rule | Status | Notes |
|---|---|---|
| I. Self-custody | PASS | Node serves public chain data and relays signed txs. No user keys. Wallet-holding RPC features (DigiDollar hot wallet) stay disabled in this release (R7). |
| II. In-browser crypto | PASS | No signing moves server-side. |
| III. Test-first (Crypto/Wallet) | N/A | No change to `DigiByte.Crypto`/`DigiByte.Wallet`. Any NodeApi code change still gets tests in `DigiByte.NodeApi.Tests`. |
| IV. Boundary discipline | PASS | NodeApi stays independently deployable; only config and deployment assets change. |
| V. Resilience | PASS | Wallet already degrades to explorers; requirement FR-015 keeps that and is verified in quickstart. |
| Secret hygiene | PASS (after work) | Removes `changeme` defaults; NodeApi must fail closed in Production if the RPC password is unset/default. |
| Auth boundary | PASS (after work) | Dangerous NodeApi routes (key, wallet, mining, block invalidate/reconsider) must be off or key-gated in the public profile (R6). |
| Docs | Required | Update `railway.toml` notes, `docs/ARCHITECTURE.md`, add runbook. |

Post-design re-check: no new violations. Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/004-railway-digibyte-node/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── railway-services.md      # services, variables, networking, ports
│   └── nodeapi-health.md        # health/status signal consumed by Railway + monitor
└── tasks.md                     # created by /speckit-tasks
```

### Source Code (repository root)

```text
docker/
├── digibyted/
│   ├── Dockerfile               # bump DGB_VERSION default; add SHA256SUMS verification
│   ├── digibyte-railway.conf    # NEW: unpruned, txindex=1, no credentials in file
│   └── entrypoint-railway.sh    # NEW: writes rpcuser/rpcpassword from env, starts digibyted
└── node-api/Dockerfile          # remove changeme/testnet/faucet-on defaults
src/DigiByte.NodeApi/
├── Program.cs                   # wires the items below; fail-closed on default password in Production
├── Services/NodeConfigValidator.cs   # NEW: Production credential check
├── Services/NodeHealthService.cs     # NEW: sync-aware health model
└── Middleware/OperatorRouteGate.cs   # NEW: operator-key gate for dangerous routes (deny-list from route audit)
tests/DigiByte.NodeApi.Tests/    # tests for the above
docs/
├── RAILWAY_NODE.md              # NEW: runbook (deploy, rotate, upgrade, recover, teardown)
└── ARCHITECTURE.md              # update
railway.toml                     # update service notes for the new project
```

**Structure Decision**: Reuse the existing monorepo layout; add a Railway-specific node config/entrypoint beside the current ones rather than editing the regtest/testnet/mainnet-pruned files, so local compose setups are unaffected.

## Complexity Tracking

No constitution violations to justify.
