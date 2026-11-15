# Implementation Plan: Oracle price and DigiDollar status from digibyte.io

**Branch**: `feature/digibyte-io-oracle` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/014-digibyte-io-oracle/spec.md`

## Summary

Bring the DigiDollar oracle display back in explorer mode. A new read-only route on the project's existing data service (`DigiByte.Api`) fetches the oracle price and DigiDollar protection status from digibyte.io, checks and normalises them, keeps one shared answer for 30 seconds, drops it after 5 minutes, and serves it to wallets (clarification Q1: the browser never contacts digibyte.io, so the content policy is unchanged). In the wallet, a small client reads that route with a 5-second limit and strict validation, and a wrapper around the explorer-backed DigiDollar API merges the latest answer, without ever waiting for it, into the existing `NetworkHealth` (price, update block, update time, stale flag, volatility freeze, ERR, and two restriction flags). The existing screens, banners and confirm-step warnings then work as designed; new text adds the source note and the loading placeholder. Everything fails safe to "unavailable". Display only: nothing here feeds transaction building, signing or approval.

## Technical Context

**Language/Version**: C# on .NET 10 (Blazor WebAssembly wallet, ASP.NET Core minimal API service)

**Primary Dependencies**: Existing only: `IHttpClientFactory`, `IMemoryCache`, ASP.NET Core rate limiting, `TimeProvider`, System.Text.Json. No new packages.

**Storage**: None. Answers are cached in memory only (service and wallet), never persisted.

**Testing**: xUnit. `tests/DigiByte.Api.Tests` (in-process host with a stub for digibyte.io), `tests/DigiByte.Wallet.Tests` (client, rules, wrapper). Live check against digibyte.io is opt-in (like `DD_LIVE=1`).

**Target Platform**: Browser (WebAssembly PWA) and the Railway-hosted `DigiByte.Api` service

**Project Type**: Web application: wallet (client) plus a small relay route on an existing service

**Performance Goals**: Oracle price visible within 3 s of opening a DigiDollar screen in 95% of loads (SC-001); an oracle fetch is limited to 5 s and never holds up any screen; the service answers from its cache in milliseconds.

**Constraints**: At most one request per kind of data to digibyte.io every 30 s however many wallets ask (SC-012); no answer older than 5 minutes served as current (SC-004); no wallet data in any request (SC-005); no new site in the browser's content policy (FR-016); display only (FR-006).

**Scale/Scope**: One route, one client, one wrapper, a handful of UI strings (en, es), five small model additions. Expected load: tens of wallets polling at most twice a minute.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Status | How |
|---|---|---|
| I. Self-custody | Pass | No key, seed or signing path is touched. The oracle is display only (FR-006). |
| II. In-browser cryptography; servers are static hosts or public data relays | Pass | The new route is a public, read-only data relay with no user data, never in a signing flow (FR-016). |
| III. Tests for crypto and wallet logic | Pass (planned) | Wallet client, freshness rules, restriction mapping and the wrapper get unit tests in the same PR; the relay gets contract tests with failure cases. |
| IV. Boundary discipline | Pass | Validity and freshness rules live in `DigiByte.Wallet` (lowest layer that owns the concept). `DigiByte.Api` stays independent and shares only a JSON contract, not code. No new reference direction. The relay checks only the shape and plausibility of digibyte.io's answers; the wallet repeats the shape check of the relay's own contract (it does not trust the relay's shape) and alone owns the freshness and protection-state rules, so no rule is duplicated across consumers. |
| V. Resilience | Pass | Unavailable behaviour is defined for every failure (FR-008 to FR-010); DGB core flows never wait on the oracle; loading placeholder defined. |
| Content Security Policy | Pass | Unchanged: the browser talks only to the data service origin that is already allowed. |
| Secret hygiene | Pass | No secrets. The upstream base address is configuration, not a credential. |
| Auth boundary | Pass | Anonymous, read-only, no state change, per-visitor rate limit; it cannot publish, move funds or touch keys. A new anonymous public route is flagged as security-relevant in the pull request (tasks T038). |
| Documentation | Planned | `docs/SECURITY.md`, `docs/ARCHITECTURE.md`, Railway notes, spec 001 amendment (FR-019, FR-021). |

Re-check after Phase 1 design: no violations introduced; no entry needed under Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/014-digibyte-io-oracle/
├── plan.md              # This file
├── research.md          # Phase 0: decisions and rejected alternatives
├── data-model.md        # Phase 1: entities, mapping, states
├── quickstart.md        # Phase 1: how to run, test and verify
├── contracts/
│   ├── oracle-api.md            # The new route the wallet calls
│   └── digibyte-io-upstream.md  # What the route consumes from digibyte.io
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/
├── DigiByte.Api/
│   ├── Endpoints/OracleEndpoints.cs        # GET /api/oracle (new)
│   ├── Services/OracleRelay.cs             # fetch, validate, cache, single-flight (new)
│   ├── Services/OracleOptions.cs           # Oracle:Enabled, Oracle:BaseUrl (new)
│   └── Program.cs                          # named client, rate-limit policy, map group (edit)
├── DigiByte.Wallet/
│   ├── Models/DigiDollarModels.cs          # NetworkHealth: update time, reported stale, restrictions; freshness rule (edit)
│   ├── Models/OracleReading.cs             # the validated reading (new)
│   ├── Services/IOracleFeed.cs           # the oracle feed interface (new)
│   ├── Services/OracleDataServiceClient.cs # implements IOracleFeed: background refresh, 5 s fetch limit, 30 s spacing, strict parse (new)
│   ├── Services/OracleEnrichedDigiDollarApi.cs # wraps the explorer API; public static Merge(health, feed, isMainnet) (new)
│   ├── Services/IDigiDollarService.cs      # DdDegradedCondition: add MintingRestricted, AllOperationsRestricted (edit)
│   ├── Services/DigiDollarService.cs       # DegradedConditions: map the two restriction conditions (edit)
│   └── Services/DigiDollarFormat.cs        # check the price shows six decimals (edit only if needed)
└── DigiByte.Web/
    ├── Program.cs                          # register the shared feed and the wrapper for the explorer backend (edit)
    ├── Services/DigiDollarViewModel.cs     # re-merge the reading into the dashboard health when the feed changes (edit)
    ├── wwwroot/appsettings.json            # DigiDollar:OracleApiUrl (edit)
    ├── wwwroot/locales/{en,es}.json        # source note, loading, restriction notices (edit)
    └── Components/DigiDollar/              # OracleStatusPanel, DigiDollarPriceLine, DigiDollarHealthBanner,
                                            # DigiDollarConfirmDialog: source note, loading state, new notices (edit)

tests/
├── DigiByte.Api.Tests/
│   ├── OracleEndpointsTests.cs
│   └── OracleLiveTests.cs              # opt-in, ORACLE_LIVE=1
└── DigiByte.Wallet.Tests/
    ├── OracleDataServiceClientTests.cs
    ├── OracleFreshnessTests.cs
    ├── OracleEnrichedDigiDollarApiTests.cs   # includes the view-model re-merge
    └── OracleContentPolicyTests.cs

docs/ and specs/001-digidollar-wallet/spec.md: amendments listed above
```

**Structure Decision**: Reuse the existing wallet/service split and the data service's existing pattern for the price proxy (named client, in-memory cache, per-visitor rate limit, `WasmClient` cross-origin policy). The wrapper pattern keeps `EsploraDigiDollarApi` untouched and applies only on the explorer backend (FR-014).

## Design Decisions (summary; reasons in research.md)

1. **One combined route**, `GET /api/oracle`, returns a normalised reading that merges two upstream reads (price, protection status). Strict: if either is unusable the route answers 503 and the wallet shows "unavailable" (spec FR-008).
2. **Freshness is computed in the wallet** from the reading and the chain height, with the time fallback when the height is missing from an otherwise available answer (FR-004). The service only enforces the 5-minute age limit.
3. **Time is injected** (`TimeProvider`) in the service and the wallet so cache expiry, the 5-minute limit and the fallback rule are testable without waiting.
4. **Single-flight** upstream fetch per kind, so concurrent wallets share one request (FR-011, SC-012).
5. **Kill switches at both ends**: `Oracle:Enabled` on the service (route answers 404 when off) and the existing `DigiDollar:OracleEnabled` in the wallet (no request when off).
6. **The oracle follows the explorer's health**: the wrapper adds the reading only to an available explorer health answer; when that is unavailable the oracle area is unavailable too. The time-based freshness fallback is for a missing height inside an available answer. **The merge never waits for the oracle** (FR-010): the wrapper awaits only the explorer call, merges the latest reading it already holds, and starts a background refresh; the health carries an `OracleLoading` flag until the first reading arrives, and the screens re-read the health when the oracle feed reports a change, so the price appears within seconds without holding up anything else.
7. **Rollout order**: service first (dark), then wallet build (still off by default), then `WALLET_DD_ORACLE=true` on the web service.

## Complexity Tracking

No constitution violations to justify.
