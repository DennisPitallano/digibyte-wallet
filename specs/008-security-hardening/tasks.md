---

description: "Task list for the security hardening feature (external review findings DGB-01 to DGB-06)"
---

# Tasks: Security Hardening from External Review

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. Spec success criteria SC-001 to SC-009 are test-driven, and the constitution (Principle III) wants tests that fail before the fix. Write each test first and confirm it fails.

**Do not push** the branch, this folder or any commit until the P0 fix is deployed (spec "Release and disclosure order"). Commit locally only.

**Organization**: One phase per finding. The three P0 findings (US1 to US3) form the hotfix. US4 and US5 are the P1 follow-up PR.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 Directory (DGB-01), US2 Digi-ID callback (DGB-02), US3 Webhooks (DGB-03), US4 Headers (DGB-06), US5 Remove analytics (DGB-05)

## Phase 1: Setup

**Purpose**: branch and projects

- [X] T001 Create the local branch `feature/security-hardening` from `main` (do not push). The constitution requires `feature/<slug>` names.
- [X] T002 Create the leaf library `src/DigiByte.Server.Shared/DigiByte.Server.Shared.csproj` (net10.0, no dependency on other repo projects) and add it to `DigiByte.slnx`.
- [X] T003 [P] Create the test project `tests/DigiByte.Server.Shared.Tests/DigiByte.Server.Shared.Tests.csproj` (xunit, same package versions as `tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj`) and add it to `DigiByte.slnx`.
- [X] T004 [P] Create the test project `tests/DigiByte.Pay.Api.Tests/DigiByte.Pay.Api.Tests.csproj` (xunit, `Microsoft.AspNetCore.Mvc.Testing`, reference `src/DigiByte.Pay.Api`) and add it to `DigiByte.slnx`. DigiPay has no test project today.
- [X] T005 Add a `ProjectReference` to `DigiByte.Server.Shared` in `src/DigiByte.Api/DigiByte.Api.csproj` and `src/DigiByte.Pay.Api/DigiByte.Pay.Api.csproj` only (no other project may reference it).

---

## Phase 2: Foundational (blocks US2 and US3)

**Purpose**: the shared outbound-URL guard and the rate-limit plumbing. US1 does not depend on this phase and can start in parallel.

**Contract**: [contracts/webhook-url-policy.md](contracts/webhook-url-policy.md)

### Tests first (must fail before T009)

- [X] T006 [P] Write `tests/DigiByte.Server.Shared.Tests/OutboundUrlGuardTests.cs` covering: non-HTTPS refused; userinfo in URL refused; URL longer than 2048 chars refused; ports outside the allow-list refused (webhooks allow `443, 8443, 8080, 8000`; Digi-ID allows `443` only); IP literals and resolved addresses refused for loopback (`127/8`, `::1`), private (`10/8`, `172.16/12`, `192.168/16`, `fc00::/7`), link-local (`169.254/16`, `fe80::/10`, including `169.254.169.254`), CGNAT (`100.64/10`), unspecified (`0.0.0.0`, `::`), multicast, reserved, and IPv4-mapped IPv6 forms of all of these; `AllowLoopback=true` permits loopback only and nothing else.
- [X] T007 [P] Write `tests/DigiByte.Server.Shared.Tests/OutboundHandlerTests.cs` covering: DNS rebinding (a host that first resolves public then private is refused at connect time, not only at validation); a 3xx response is returned as a failed delivery and not followed; response body is read only up to `MaxResponseBytes` (65536); request timeout applies (`TimeoutSeconds` 10). Use a local test server and a fake resolver.

### Implementation

- [X] T008 Implement `src/DigiByte.Server.Shared/OutboundUrlPolicy.cs` (options: `AllowLoopback` bool default false, `AllowedPorts` int[] per caller, `TimeoutSeconds` int default 10, `MaxResponseBytes` int default 65536) per [data-model.md](data-model.md).
- [X] T009 Implement `src/DigiByte.Server.Shared/OutboundUrlGuard.cs` with `Validate(url, policy)` returning a short category (`must use https`, `address not allowed`, `redirects not allowed`, or ok) and a connect-time check usable from `SocketsHttpHandler.ConnectCallback`. Resolved IPs and DNS details are logged by the caller and never returned.
- [X] T010 Implement `src/DigiByte.Server.Shared/GuardedHttpClientExtensions.cs`: an `AddGuardedHttpClient(name, policy)` helper that sets `AllowAutoRedirect = false`, the connect-time check, the timeout, and a capped response read. Make T006 and T007 pass. Bind `OutboundUrlPolicy` from an `Outbound` configuration section (key `Outbound:AllowLoopback`, default false) in both services' `Program.cs`, so development can enable loopback and production cannot by accident.
- [X] T011 [P] Add forwarded-headers handling to `src/DigiByte.Api/Program.cs` that trusts only Railway's proxy and takes the last `X-Forwarded-For` hop as the client address (see [research.md](research.md) R4 and the clarification on rate-limit keying). Verify the exact trust setting against Railway before relying on it. _Done as: rightmost `X-Forwarded-For` entry captured before the forwarded-headers middleware (`ClientAddress`, already the rule for the sponsor limiter in `DigiByte.Api`); verifying Railway appends the client address last is a deploy-time check in quickstart step 9._
- [X] T012 [P] Add the same forwarded-headers handling to `src/DigiByte.Pay.Api/Program.cs`. _Done with `ClientAddress.Capture` in `DigiByte.Pay.Api`; all `PerIp` limiter policies now use it._

**Checkpoint**: guard and plumbing ready; US2 and US3 can proceed.

---

## Phase 3: User Story 1 - Remove public directory mutations (DGB-01, P0) MVP

**Goal**: nobody can create or delete directory entries; lookups keep working.

**Independent Test**: `POST /api/directory/register` and `DELETE /api/directory/x` return 404 or 405 and `GET /api/directory/lookup/anything` returns 404 without error. See [contracts/directory.md](contracts/directory.md). Covers FR-001 and SC-001.

- [X] T013 [US1] Write `tests/DigiByte.Api.Tests/DirectoryEndpointsTests.cs` asserting: register and delete are not routable (404/405); lookup of an unknown username and an unknown phone return 404 with the existing error body. Confirm the register and delete assertions fail first.
- [X] T014 [US1] In `src/DigiByte.Api/Endpoints/DirectoryEndpoints.cs`, remove `Register` and `Unregister` and their `MapPost`/`MapDelete` lines. Keep both lookups. Remove the now-unused static `Entries` writes (keep the dictionary read-only and empty).
- [X] T015 [US1] Confirm `src/DigiByte.Web/Pages/Remittance.razor` (lines ~140-141) already handles a 404 lookup with a clear message; if not, fix it. Do not add the confirm-address screen (FR-003 is deferred).

**Checkpoint**: US1 done and testable alone.

---

## Phase 4: User Story 2 - Digi-ID callback is bound to the signed URI (DGB-02, P0)

**Goal**: the proxy only contacts the site named in a signature-verified `digiid://` URI, through the guard.

**Independent Test**: a bad signature, mismatched `callbackUrl`, `http` callback, blocked destination or redirect each fail with no outbound request, and a valid signed request to a public HTTPS test endpoint still works. See [contracts/digiid-callback.md](contracts/digiid-callback.md). Covers FR-004 to FR-007, SC-002, SC-007 and SC-008.

Depends on: Phase 2.

### Tests first

- [X] T016 [P] [US2] Write `tests/DigiByte.Api.Tests/DigiIdCallbackTests.cs` covering: the target is derived from `uri` (`digiid://host/path?x=nonce` becomes `https://host/path?x=nonce`); `u=1` (http) is refused with `{"source":"proxy","reason":"must use https"}`; a `callbackUrl` that differs from the derived URL returns `400`; a bad signature returns `400` with reason `bad signature` (use `DigiIdService.Verify` from `src/DigiByte.Crypto/DigiId/DigiIdService.cs:109`); a loopback, private or metadata target returns `400` with reason `address not allowed` and sends no request; a redirecting target returns reason `redirects not allowed`; a non-443 port is refused; a site that times out, fails DNS, drops the connection or returns more than `MaxResponseBytes` returns `502` with `{"source":"proxy","reason":"site unreachable"}`; a valid request relays the site's status and truncated plain-text body; errors never contain IPs or resolver details.
- [X] T017 [US2] Write a rate-limit test in the same file (so not parallel with T016): the 11th call within a minute from one client returns `429` with `{"source":"proxy","reason":"rate limited"}`, and a spoofed `X-Forwarded-For` does not reset the count (SC-007).

### Implementation

- [X] T018 [US2] In `src/DigiByte.Api/Endpoints/DigiIdEndpoints.cs`, replace the `callbackUrl` trust: parse `uri` with `DigiIdService.ParseUri`, derive the HTTPS target, refuse `u=1`, verify the signature with `DigiIdService.Verify(address, uri, signature)`, and if `callbackUrl` was sent require it to equal the derived URL. Return proxy refusals as `400` with `{"source":"proxy","reason":"<category>"}`, and upstream failures (timeout, DNS failure, connection error, oversize body) as `502` with reason `site unreachable`, never an unhandled 500.
- [X] T019 [US2] Register the `DigiIdProxy` client in `src/DigiByte.Api/Program.cs` with `AddGuardedHttpClient` (Digi-ID `AllowedPorts` = `[443]`, `TimeoutSeconds` 10, `MaxResponseBytes` 65536, no redirects). Relay the site's status and a truncated plain-text body.
- [X] T020 [US2] Add the rate limiter in `src/DigiByte.Api/Program.cs`: fixed window of 10 requests per minute per client address on `POST /api/digiid/callback`, rejecting with `429` and the `source:"proxy"` body.
- [X] T021 [US2] In `src/DigiByte.Web/Pages/Identity.razor` (around line 519), read `source` from the proxy's reply and show a proxy-specific message ("couldn't reach the site securely"; for reason `must use https`, "the site must use https"; for `site unreachable`, "the site didn't respond, try again") instead of the "site rejected" messages. Keep the existing messages for real site replies.

**Checkpoint**: US2 done and testable alone.

---

## Phase 5: User Story 3 - Webhook destinations are validated (DGB-03, P0)

**Goal**: DigiPay never sends a request to an internal or disallowed destination, from registration through dispatch.

**Independent Test**: registering or updating with a hostile URL returns `400`; a hostile stored URL is not called and shows a failed delivery with a reason in the dashboard; `POST /v1/pay/stores/{storeId}/webhook/test` with a hostile URL sends nothing. See [contracts/webhook-url-policy.md](contracts/webhook-url-policy.md). Covers FR-008 to FR-011, SC-002, SC-003.

Depends on: Phase 2.

### Tests first

- [X] T022 [P] [US3] Write `tests/DigiByte.Pay.Api.Tests/WebhookRegistrationTests.cs`: `POST /v1/pay/merchants` with `http://127.0.0.1`, a private IP, `169.254.169.254`, a non-allowed port, an `http://` URL, and a hostname that resolves to a private address each return `400` with only a short category (`must use https` or `address not allowed`); a valid public HTTPS URL on 443, 8443, 8080 and 8000 is accepted.
- [X] T023 [P] [US3] Write `tests/DigiByte.Pay.Api.Tests/WebhookStoreUpdateTests.cs`: the store update route (`StoresEndpoints.cs`) applies the same validator; `http://localhost` is refused unless the development flag `Outbound:AllowLoopback` is true.
- [X] T024 [P] [US3] Write `tests/DigiByte.Pay.Api.Tests/WebhookDispatchTests.cs`: a stored hostile URL is not called, and the delivery row has `ErrorMessage` = "destination not allowed", is logged, and leaves `WebhookUrl` and `WebhookSecret` untouched; `ResponseSnippet` keeps the first 512 characters as plain text (markup escaped or stripped) for any status; the regression from SC-003 (register with a hostile URL, then call the webhook test route) sends no request.
- [X] T025 [P] [US3] Write `tests/DigiByte.Pay.Api.Tests/RegistrationRateLimitTests.cs`: the 6th `POST /v1/pay/merchants` in an hour from one client returns `429`; a spoofed `X-Forwarded-For` does not reset the count.

### Implementation

- [X] T026 [US3] In `src/DigiByte.Pay.Api/Endpoints/PaymentsEndpoints.cs` (around line 70), validate `body.WebhookUrl` with `OutboundUrlGuard.Validate` before storing; return `400` with the short category on failure.
- [X] T027 [US3] In `src/DigiByte.Pay.Api/Endpoints/StoresEndpoints.cs` (around lines 114-142), replace the ad-hoc scheme check and the `http://localhost` allowance with the shared validator. Webhook `AllowedPorts` = `[443, 8443, 8080, 8000]`. Loopback only when `Outbound:AllowLoopback` is true (development only; default false).
- [X] T028 [US3] In `src/DigiByte.Pay.Api/Services/WebhookDispatcher.cs`, re-validate `store.WebhookUrl` before sending. On failure make no request, record the delivery with `ErrorMessage` = "destination not allowed", log the detail server-side, and leave the stored URL and secret untouched. Cap `ResponseSnippet` at 512 characters (was 2048) and store it as plain text with markup escaped or stripped for any status.
- [X] T029 [US3] Register the `DigiPayWebhook` client in `src/DigiByte.Pay.Api/Program.cs` (around line 214) with `AddGuardedHttpClient` (webhook `AllowedPorts`, `TimeoutSeconds` 10, `MaxResponseBytes` 65536, no redirects).
- [X] T030 [US3] Add the rate limiter in `src/DigiByte.Pay.Api/Program.cs`: fixed window of 5 requests per hour per client address on `POST /v1/pay/merchants`, rejecting with `429`.
- [X] T031 [US3] Confirm the dashboard (`src/DigiByte.Pay.Web/Components/Pages/Dashboard.razor` around line 592 and `SessionDetail.razor` around line 217) already shows `ErrorMessage`, so "destination not allowed" appears with no UI change. Adjust only if it doesn't.
- [X] T032 [P] [US3] Write the read-only production query as `scripts/webhook-url-audit.sql` (PostgreSQL): list `PayStore` rows whose `WebhookUrl` is not `https`, has a port outside `443, 8443, 8080, 8000`, or has a private, loopback or IP-literal host. Document how to run it in [quickstart.md](quickstart.md) step 8. Do not run it against production from here; the maintainer runs it before deploy. _Not run against a live PostgreSQL database._

**Checkpoint**: US3 done and testable alone. **The P0 hotfix is complete after this phase.**

---

## Phase 6: Hotfix verification and release (P0)

- [X] T033 (FR-015, APIs) Add a `commit` field to `/api/health` in `src/DigiByte.Api/Program.cs` (line ~132, currently `{status, timestamp}`), `src/DigiByte.Pay.Api/Program.cs` (line ~381) and `src/DigiByte.NodeApi/Program.cs` (line ~175, via `NodeHealthService`), read from the `RAILWAY_GIT_COMMIT_SHA` environment variable and `unknown` when absent. First confirm Railway exposes that variable at runtime for Dockerfile deployments; if not, pass it as a Docker build argument in `docker/api/Dockerfile`, `docker/pay-api/Dockerfile` and `docker/node-api/Dockerfile`. Add a test per API (`tests/DigiByte.Api.Tests`, `tests/DigiByte.Pay.Api.Tests`, `tests/DigiByte.NodeApi.Tests`) asserting the health response has a `commit` field. Do this after T011, T012, T019, T020, T029 and T030 (same `Program.cs` files). It is part of the hotfix PR so the reviewer can confirm the fix is live (release order step 4).
- [X] T034 Run `dotnet build` and `dotnet test` for `tests/DigiByte.Server.Shared.Tests`, `tests/DigiByte.Api.Tests`, `tests/DigiByte.Pay.Api.Tests`, plus the existing Crypto, NodeApi and Wallet test projects, and fix regressions. _Result: Server.Shared 116, Api 125+, Pay.Api 47, Crypto 350, NodeApi 382 pass. Wallet: 1191 pass, 6 `LiveExplorerBenchmarks` fail because they call live explorers (unrelated, untouched)._
- [X] T035 Walk through [quickstart.md](quickstart.md) scenarios 1 to 5 (directory, Digi-ID, webhooks, rate limits, regression) against a local instance. _Covered by the automated tests for scenarios 1 to 5 (in-process hosts with fake receivers and resolvers); no separate manual run against a real public HTTPS Digi-ID site._
- [X] T036 Commit locally with Conventional Commits and a PR description that flags the changes as security-relevant (auth boundary, outbound requests). Do not push.
- [X] T037 (DGB-02, deploy-time) Add a release-step note to [quickstart.md](quickstart.md) and the `docs/` Railway runbook: restrict outbound network access for the `digibyte-api` and `digipay-api` services where Railway allows it (FR-007 "egress restricted where the host allows it"), and record whether Railway supports this for these services. This is a platform setting, not code. Do it before T038. _Note added to quickstart step 9 and `docs/SECURITY.md`; whether Railway supports egress restriction is recorded there as not yet confirmed._
- [ ] T038 (maintainer) Run the production audit query from T032, review affected merchants, deploy, then follow the release order in [spec.md](spec.md): push, reviewer recheck, publish advisory GHSA-j88j-g3gf-jmr2.

---

## Phase 7: User Story 4 - Frame and content-type headers (DGB-06, P1)

**Goal**: the wallet cannot be framed and sends `nosniff` and a no-referrer policy as response headers on every route.

**Independent Test**: `curl -sI` on `/`, `/config.json`, a `.js` file, `/service-worker.js`, `/service-worker-assets.js`, an unknown route and a 404 each show `content-security-policy` with `frame-ancestors 'none'`, `x-frame-options: DENY`, `x-content-type-options: nosniff`, `referrer-policy: no-referrer`. Covers FR-012 and SC-004. See [research.md](research.md) R5.

- [X] T039 [US4] Create `docker/web/security-headers.conf` with `add_header Content-Security-Policy "frame-ancestors 'none'" always;`, `add_header X-Frame-Options "DENY" always;`, `add_header X-Content-Type-Options "nosniff" always;`, `add_header Referrer-Policy "no-referrer" always;`.
- [X] T040 [US4] In `docker/web/nginx.conf`, `include` the snippet in the `server` block and inside each `location` that sets its own `add_header` (`= /config.json`, the asset regex, `= /service-worker.js`, `= /service-worker-assets.js`), because Nginx drops inherited `add_header` lines in a location that defines its own.
- [X] T041 [US4] In `docker/web/Dockerfile`, copy `docker/web/security-headers.conf` into the image next to the Nginx config (alongside the existing `nginx.conf` copy at about lines 45-56).
- [X] T042 [US4] Build and run the image locally (`docker build -f docker/web/Dockerfile .`) and verify all seven routes in the independent test with `curl -sI`. Confirm the wallet still loads and the service worker still registers. In the PR description for this P1 change, flag the header and CSP changes as security-relevant (constitution merge gates). _Verified with the real `nginx:alpine` image and `docker/web/nginx.conf` plus `security-headers.conf` on all seven routes (each header once). The full `docker build -f docker/web/Dockerfile .` was not run; the published wallet loaded through that nginx config to the Welcome screen. The preview pane cannot register service workers (same failure with the old config), so service-worker registration was not confirmed._

---

## Phase 8: User Story 5 - Remove Google Analytics (DGB-05, P1)

**Goal**: no third-party script runs in the wallet origin. No replacement ships (first-party analytics gets its own spec).

**Independent Test**: loading the wallet makes no request to `googletagmanager.com` or `google-analytics.com`, and the CSP lists neither. Covers FR-013.

- [X] T043 [P] [US5] In `src/DigiByte.Web/wwwroot/index.html`, remove the gtag `<script async src=".../gtag/js?id=G-NR6R1MNL9X">` (line ~62) and its comment, and any `analytics.js` script reference.
- [X] T044 [US5] Delete `src/DigiByte.Web/wwwroot/js/analytics.js` and remove any references to it (including service-worker asset lists and the CSP script hash if one exists solely for it).
- [X] T045 [US5] In the meta CSP in `src/DigiByte.Web/wwwroot/index.html` (lines ~40-52), remove `https://www.googletagmanager.com` from `script-src` and `img-src`, and `https://www.google-analytics.com`, `https://www.googletagmanager.com` and `https://analytics.google.com` from `connect-src`. Keep everything else unchanged.
- [X] T046 [US5] Search the repo for other uses of `gtag` or `dataLayer` (for example Razor pages calling analytics) and remove or no-op them so nothing throws.
- [X] T047 [US5] Run the wallet locally and confirm in the browser network panel and console that no Google request is made and there are no CSP or script errors. _Verified on a local publish served by nginx: Welcome screen loads, zero requests to Google hosts. Note: a local Windows publish writes CRLF, so the injected importmap CSP hash does not match what the browser computes (same on untouched `main`); I added the LF hash to a temp copy only. Docker builds on Linux are not affected._

---

## Phase 9: Polish and cross-cutting

- [X] T048 [P] Update `docs/SECURITY.md` and the WalletScrutiny note to remove the third-party script gap and record the new outbound-request rules, the PIN limitation (FR-014 stays a known gap), and the response headers.
- [X] T049 [P] Add a CHANGELOG entry under a security heading in `CHANGELOG.md` without exploit detail until the advisory is published.
- [X] T050 [P] Update `docs/ARCHITECTURE.md` (and `docs/PROCESS_FLOWS.md` if the Digi-ID flow is described there) for the signed-URI-derived callback and the guarded outbound client.
- [X] T051 Re-run the NuGet and npm audits (`dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev` in the web and Node SDK folders) and record the result for SC-006. _Result: `dotnet list package --vulnerable --include-transitive` clean for Api, Pay.Api, NodeApi, Web; `npm audit` 0 for web (prod and dev) and SDK._
- [X] T052 Decide on observability for refused outbound attempts (log level and any alert), the one low-impact open item from clarification, and note it in `plan.md`. _Decided: logged at Warning with host and short reason; no alert (see plan.md)._
- [ ] T053 Create follow-up specs, each in its own session: first-party analytics, directory registration with ownership proof (FR-002, FR-003), and PIN KDF / Argon2id (FR-014).
- [X] T054 [P] (DGB-04, interim) State the PIN limitation plainly in the wallet UI: find the screen where the user creates or changes the six-digit PIN (search `src/DigiByte.Web` Razor pages for the PIN entry used at wallet creation), and add a short note that a copied browser profile lets someone guess a six-digit PIN offline, with a suggestion to protect the device. Add the strings to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`. This is the interim step in FR-014 until the Argon2id spec ships.
- [X] T055 [P] (DGB-07) Update the dev-only npm packages the review listed: in `src/DigiByte.Web/package.json` (`nanoid`, `postcss`, `postcss-selector-parser`, `source-map-js`) and `sdk/node/package.json` (`brace-expansion`, `glob`, `minimatch`, `rimraf`), regenerate the lockfiles, rebuild (including the Tailwind output) and re-run `npm audit`. Also confirm the embedded SQLite version in `src/DigiByte.Pay.Api` and `src/DigiByte.NodeApi` (the review's `SQLitePCLRaw.lib.e_sqlite3` advisory GHSA-2m69-gcr7-jv3q) with `dotnet list package --include-transitive`, and record the result for SC-006. _`npm audit fix` in `src/DigiByte.Web`; `rimraf` 5 to 6 in `sdk/node`; both `npm audit` clean, SDK build and webhook tests pass. NuGet: no vulnerable packages in Api, Pay.Api, NodeApi, Web; `SQLitePCLRaw.lib.e_sqlite3` is 2.1.12 in Pay.Api and NodeApi._
- [ ] T056 (FR-015, wallet, maintainer) After the P1 PR is deployed, cut a `wallet-v*` tag (the workflow checks the tag against the csproj version, see `.github/workflows/wallet-release-manifest.yml`), let it publish the manifest, then run the comparison from `release-hashes/README.md` against `https://dgbwallet.app` (at least `_framework/dotnet.wasm`, `index.html` and the JS files) and record the result for SC-009.

---

## Dependencies and order

- Phase 1 first. Phase 2 blocks US2 and US3. US1 has no dependency on Phase 2 and can run in parallel.
- US1, US2 and US3 are independent of each other after Phase 2. Together with Phase 6 they form the hotfix PR.
- US4 and US5 are independent of the hotfix and of each other. They go in the P1 PR after the hotfix is deployed.
- T054 and T055 are polish tasks, independent of the stories. T037 is a deploy-time step and sits in Phase 6 before T038. T033 (commit stamp on the APIs) edits the same `Program.cs` files as the hotfix, so it comes after T011, T012, T019, T020, T029 and T030; it is part of the hotfix PR and runs first in Phase 6, before the test run and the local commit. T056 (wallet manifest check) is a maintainer step after the P1 PR is deployed.
- Within a story: tests first (confirm they fail), then implementation, then checkpoint.

## Parallel examples

- After T005: T006 and T007 (guard tests), T011 and T012 (forwarded headers) can run together.
- US1 (T013 to T015) can run alongside Phase 2.
- Within US3: T022 to T025 (tests in separate files) can be written together.
- US4 and US5 can be split between two people.

## Implementation strategy

1. MVP is US1: it removes the simplest attack in minutes and has no dependency on the guard. Ship it with the others rather than alone if the guard is ready the same day.
2. Complete Phase 2, then US2 and US3 in parallel, then Phase 6, and deploy as one hotfix.
3. Push, get the reviewer's recheck, publish the advisory (release order in the spec).
4. Follow with the P1 PR (US4, US5), then the separate follow-up specs (T053).
