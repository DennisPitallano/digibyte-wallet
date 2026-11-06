# Implementation Plan: Security Hardening from External Review

**Branch**: `feature/security-hardening` (not yet created; spec directory is `008-security-hardening`) | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: [spec.md](spec.md). Tracked privately as GHSA-j88j-g3gf-jmr2. **Do not push the branch or this folder until the P0 fixes are deployed.**

## Summary

Close three open production holes first, in one small hotfix: (1) remove the public directory mutation routes, (2) make the Digi-ID callback proxy derive its destination from a signature-verified `digiid://` URI and use an SSRF-safe HTTP client, (3) validate DigiPay webhook URLs everywhere with the same SSRF-safe client. Then add response headers (P1), remove gtag.js (P1), and plan the PIN KDF migration (P2). Details and decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`), Blazor WebAssembly wallet, vanilla JS for `crypto.js`

**Primary Dependencies**: ASP.NET Core minimal APIs, `HttpClientFactory`, EF Core + SQLite (Pay.Api), `DigiByte.Crypto` (message-signature verification for Digi-ID)

**Storage**: Pay.Api uses PostgreSQL in production (Railway addon; SQLite locally and in tests), so the pre-deploy query must run on Postgres. Directory store becomes read-only for now (no migration)

**Testing**: xUnit. `tests/DigiByte.Api.Tests` exists. Pay.Api has **no test project**, so `tests/DigiByte.Pay.Api.Tests` is added.

**Target Platform**: Railway-hosted APIs, static PWA at `dgbwallet.app`

**Project Type**: Multi-service monorepo (wallet PWA + 3 independently deployable APIs)

**Performance Goals**: Guard adds one DNS resolution per outbound call; no throughput target

**Constraints**: Hotfix must be backward compatible with the shipped wallet client (`Identity.razor` posts `callbackUrl`, `address`, `uri`, `signature`; `Remittance.razor` only calls directory lookups)

**Scale/Scope**: 3 endpoints, 1 shared guard, 1 header config, 1 CSP edit; P2 is separate. Out of this plan: FR-003 (wallet confirm-address screen, deferred to the directory-registration feature), first-party analytics (own spec), the PIN KDF (FR-014, own spec).

## Constitution Check

| Gate | Result |
|---|---|
| I. Self-custody | Pass. No key or seed handling changes. |
| II. In-browser crypto | Pass. Server only verifies a public signature (no signing). The callback relay is a public-chain relay role. |
| III. Test-first for Crypto/Wallet | Applies if the guard or verifier lands in `DigiByte.Wallet`/`Crypto`. Tests written first and must fail before each fix. |
| IV. Boundary discipline | See Complexity Tracking. The shared guard needs a home. |
| V. Resilience | Pass. The wallet's core flows do not depend on the directory mutations or the callback proxy. Digi-ID login failure messages stay user-visible. |
| CSP: no third-party CDN in wallet origin | **Currently violated by gtag.js** (DGB-05). FR-013 restores compliance. |
| Auth boundary | Pass after FR-002 (mutations authenticated or removed). |
| Vulnerability reporting | Pass. Report was filed as a private advisory, not a public issue. |

Post-design re-check: pass, with the one justified item below.

## Project Structure

### Documentation

```text
specs/008-security-hardening/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── digiid-callback.md
│   ├── directory.md
│   ├── health.md
│   └── webhook-url-policy.md
└── tasks.md        # /speckit-tasks
```

### Source Code

```text
src/
├── DigiByte.Server.Shared/             # NEW leaf library, referenced by Api and Pay.Api only
│   └── OutboundUrlGuard.cs             # URL + resolved-IP policy, SocketsHttpHandler.ConnectCallback
├── DigiByte.Api/
│   ├── Endpoints/DirectoryEndpoints.cs # remove POST/DELETE
│   ├── Endpoints/DigiIdEndpoints.cs    # derive URL from uri, verify signature, guarded client, `source:"proxy"` refusals (incl. `site unreachable`)
│   └── Program.cs                      # DigiIdProxy client uses the guard, no redirects; forwarded headers (Railway proxy only) + rate limiter (10/min/IP); binds `Outbound` options
├── DigiByte.Pay.Api/
│   ├── Endpoints/PaymentsEndpoints.cs  # validate WebhookUrl at registration
│   ├── Endpoints/StoresEndpoints.cs    # use shared validator, dev-only localhost
│   ├── Services/WebhookDispatcher.cs   # re-validate at dispatch, trim ResponseSnippet
│   └── Program.cs                      # DigiPayWebhook client uses the guard; forwarded headers (Railway proxy only) + rate limiter (5/hour/IP on merchant registration)
├── DigiByte.NodeApi/
│   └── Program.cs                      # `commit` field on /api/health (FR-015); no other change
├── DigiByte.Web/
│   ├── Pages/Identity.razor            # show a proxy-specific message when `source` is `proxy`
│   └── wwwroot/
│       ├── index.html                  # remove gtag script and Google CSP entries (P1)
│       └── js/analytics.js             # delete, no replacement (P1)
scripts/
└── webhook-url-audit.sql               # NEW: read-only PostgreSQL query listing stored webhook URLs that fail the new rules (run by the maintainer before deploy)
docker/ (api, pay-api, node-api Dockerfiles)  # only if Railway does not expose the commit variable at runtime: pass it as a build argument (FR-015)
docker/web/
├── nginx.conf                          # include the security headers in server and every location with add_header (P1)
├── security-headers.conf               # NEW: frame-ancestors 'none', X-Frame-Options DENY, nosniff, no-referrer
└── Dockerfile                          # copy the snippet

tests/
├── DigiByte.Api.Tests/                 # directory + digiid tests
├── DigiByte.Pay.Api.Tests/             # NEW: registration -> webhook test regression
├── DigiByte.NodeApi.Tests/             # `commit` field test on /api/health (FR-015)
└── DigiByte.Server.Shared.Tests/       # NEW: guard unit tests (private, loopback, link-local, metadata, rebinding, redirect)
```

**Structure Decision**: One new leaf library for the guard, two new test projects. Headers go in the wallet's Nginx config (hosting question resolved in research.md R5).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| New `DigiByte.Server.Shared` project (Principle IV) | Api and Pay.Api both need an identical SSRF policy, and a bug in one copy is the same class of bug found here | Duplicating the guard in each service invites drift. Putting it in `DigiByte.Wallet` ships server-only code in the WASM bundle. It is a leaf referenced only by the two services, so it inverts no dependency direction; note it in the PR as security-relevant. |

## Also in scope (polish tasks T048 to T056)

- Interim PIN-limitation wording in the wallet UI (`en.json`, `es.json`) until the Argon2id spec ships (DGB-04).
- Dev-only npm updates in `src/DigiByte.Web` and `sdk/node`, and a check of the embedded SQLite version (DGB-07).
- Deploy-time note to restrict outbound network access for the two APIs where Railway allows it (DGB-02).
- Docs: `docs/SECURITY.md`, `CHANGELOG.md`, `docs/ARCHITECTURE.md`.
- Build verification (FR-015): a `commit` field on each API's `/api/health` (T033) and, after the P1 deploy, a `wallet-v*` tag whose release manifest is compared with what `dgbwallet.app` serves (T056, process in `release-hashes/README.md`).
- Observability of refused outbound attempts (T052, decided): refusals are logged at **Warning** with the destination host and the short reason (`DigiIdProxy` and `DigiPayWebhook` categories, and `WebhookDispatcher` for stored URLs), never returned to the client. No alert for now; a spike in "address not allowed" in the logs is the signal to look. Revisit if abuse shows up.

## Delivery order

1. **Hotfix PR (P0)**: FR-001, FR-004 to FR-011. Deploy, then reviewer rechecks production (SC-005).
2. **P1 PR**: FR-012 headers, FR-013 gtag removal.
3. **P2 spec**: FR-014 gets its own spec (versioned envelope, Argon2id, migration). It touches `crypto.js` and stored wallets and is not a hotfix.
