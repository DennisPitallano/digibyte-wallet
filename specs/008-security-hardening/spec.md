# Feature Specification: Security Hardening from External Review

**Created**: 2026-11-06

**Status**: Draft. **Do not push or publish until DGB-01 to DGB-03 are deployed.** It describes open production issues.

**Input**: External review by JhonnyLaw (2026-11-05, against `60c9248`), tracked privately as GHSA-j88j-g3gf-jmr2.

## Triage (against `main` at e6c9031)

| ID | Status | Priority |
|---|---|---|
| DGB-01 Directory mutation without ownership proof | Open | P0 |
| DGB-02 Digi-ID callback request to a caller-chosen URL | Open | P0 |
| DGB-03 DigiPay webhook URL not constrained (with response read-back) | Open | P0 |
| DGB-06 No HTTP frame/nosniff headers on the wallet | Open | P1 |
| DGB-05 Third-party gtag.js in the wallet origin | Known gap | P1 |
| DGB-04 Six-digit PIN protects exported ciphertext | Known gap | P2 |
| DGB-07 Vulnerable locked packages | NuGet advisories fixed (b581f82, 88818fe). Remaining: dev-only npm advisories and the embedded SQLite version check (tasks T055), then re-run audits (SC-006) | Done / small follow-up |

## Clarifications

### Session 2026-11-06

- Q: Which client address do the rate limits count behind Railway's proxy? → A: The real client address from ASP.NET forwarded-headers handling that trusts only Railway's proxy: the last `X-Forwarded-For` value Railway adds. Values the caller sent earlier in the header are ignored, so the limit can't be dodged by spoofing.
- Q: Is the wallet's confirm-the-resolved-address step for directory lookups part of this hotfix? → A: No. It moves to the later directory-registration feature (FR-002). The hotfix only removes the register and delete routes. The only caller is the unlinked `/remittance` page, which reads lookups only.
- Q: What should happen to already-stored webhook URLs that fail the new rules? → A: Skip the call at dispatch, record a failed delivery with a visible reason, log it, and leave the stored URL untouched.
- Q: May any other site frame the wallet? → A: No. Block all framing (`frame-ancestors 'none'`, `X-Frame-Options: DENY`). No embed of the wallet was found in the repo. A specific origin can be added later if a legitimate embed appears.
- Q: Should the dashboard still show a preview of the webhook receiver's reply? → A: Yes, for any status, limited to the first 512 characters as plain text (markup escaped or stripped). The destination has already passed the connect-time IP check, so the preview is no longer a read-back path.
- Q: How does the wallet tell a proxy refusal apart from the login site's own reply? → A: The proxy keeps relaying the site's status and truncated body as today. Its own refusals use `400` or `429` with `{"source":"proxy","reason":"<short category>"}`, and the wallet shows a proxy-specific message when `source` is `proxy`.
- Q: What request limits apply? → A: Digi-ID callback: 10 requests per minute per IP. DigiPay merchant registration: 5 per hour per IP. Both return `429` when exceeded.
- Q: Which ports may outbound destinations use? → A: Webhooks: HTTPS on 443, 8443, 8080 or 8000. Digi-ID callbacks: 443 only. Before deploy, run a read-only query on production to list stored webhook URLs that would fail the rules (non-HTTPS, private or loopback host, other ports), so affected merchants are known.
- Q: When may the fix and spec go public relative to the advisory? → A: Push the fix as soon as it is deployed to production. Publish the advisory after the reviewer has rechecked production. Nothing is pushed before deploy.
- Q: How much detail should a refused-destination error give? → A: A short category only ("must use https", "address not allowed", "redirects not allowed"). Resolved IPs and DNS details go to server logs, never the response.
- Q: Should the Digi-ID proxy still forward to plain `http://` callbacks (`u=1`)? → A: No. Refuse them always, and show a clear "the site must use https" message in the wallet.
- Q: Remove Google Analytics, or replace it? → A: Remove it entirely for now. A first-party analytics replacement gets its own spec in a later session.

## Requirements

### P0 hotfix (ship first, no migrations)

**DGB-01 Directory** (`DigiByte.Api/Endpoints/DirectoryEndpoints.cs`)
- FR-001: Until ownership proof exists, `POST /register` and `DELETE /{username}` MUST NOT be reachable publicly (remove the routes or return 410/403). Lookups stay.
- FR-002: Before re-enabling mutations: registration, update and deletion require a Digi-ID style signature from the entry's address; the address and network are validated; usernames are normalized and format-limited; storage is durable with a unique index; mutations are rate-limited and logged.
- FR-003 (deferred to the directory-registration feature, not in this hotfix): when registration returns, the wallet treats any directory result as untrusted and shows the resolved address for confirmation before a payment.

**DGB-02 Digi-ID callback** (`DigiByte.Api/Endpoints/DigiIdEndpoints.cs`)
- FR-004: The server derives the callback URL from the signed `digiid://` URI. A client-supplied `callbackUrl`, if sent, must equal the derived URL, otherwise the request is refused. The proxy stays (many relying parties lack CORS).
- FR-005: The Digi-ID signature is verified before anything is forwarded.
- FR-006: The destination MUST be HTTPS (a `u=1` / `http://` callback is refused and the wallet shows "the site must use https"), the resolved IP MUST NOT be private, loopback, link-local, reserved or a metadata address (checked after DNS resolution, at connect time), redirects MUST be disabled, and requests MUST have a 10-second timeout and a 64 KB response cap. Digi-ID callbacks use port 443 only. If the site cannot be reached (timeout, DNS failure, connection error, oversize reply) the proxy returns `502` with `{"source":"proxy","reason":"site unreachable"}`, never an unhandled error. The site's reply is relayed as its status code plus a truncated plain-text body, as today.
- FR-007: Calls are rate-limited to 10 per minute per IP (`429` when exceeded), and egress from the service is restricted where the host allows it.

**DGB-03 Webhooks** (`PaymentsEndpoints.cs`, `StoresEndpoints.cs`, `WebhookDispatcher.cs`)
- FR-008: One shared validator is used by merchant registration, store update and dispatch. It requires public HTTPS on port 443, 8443, 8080 or 8000 for webhooks, applies the FR-006 IP rules at connect time, and disables redirects. The localhost allowance is development-only (config flag, off in production).
- FR-009: Registration is validated, not just updates. Existing stored URLs are re-validated at dispatch. An invalid one is not called: the delivery is recorded as failed with a clear reason ("destination not allowed") that appears in the merchant dashboard's delivery list, and it is logged. The stored URL and secret are left untouched so the merchant can fix them.
- FR-010: `ResponseSnippet` keeps the first 512 characters of the receiver's reply, as plain text with markup escaped or stripped, for any status (merchants use it to debug). It was 2 KB before. Because the destination has already passed the connect-time IP check, the preview is no longer a read-back path to internal addresses.
- FR-011: Self-service registration is limited to 5 per hour per IP (`429` when exceeded).

### P1

- FR-012 (DGB-06): The wallet host sends `Content-Security-Policy: frame-ancestors 'none'`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` as response headers. The existing script restrictions stay in the meta CSP (unchanged apart from FR-013); only `frame-ancestors` is added as a header, since a meta tag cannot enforce it.
- FR-013 (DGB-05): Remove Google Analytics entirely for now: delete the gtag script and `js/analytics.js`, and drop `googletagmanager.com` and the Google entries from `script-src`, `connect-src` and `img-src`. No replacement ships in this feature.
  - **Follow-up (separate spec, separate session)**: first-party, cookie-less usage analytics so the team can still see whether the app is used. Open choices recorded for that spec: a stats page on the API, a protected JSON endpoint, or a self-hosted tool (Plausible/Umami). No wallet addresses, IPs or user IDs. Until it exists, the team has no usage data.

- FR-015 (review "Coverage" note: deployed binaries may not match the reviewed commit): make deployed builds verifiable. The API `commit` field ships with the P0 hotfix; the wallet manifest check follows the P1 deploy.
  - **Wallet**: after the P1 PR is deployed, cut a `wallet-v*` tag so the existing release workflow publishes a per-file sha256 manifest, and compare what `dgbwallet.app` serves against it (process in `release-hashes/README.md`).
  - **APIs** (`digibyte-api`, `digipay-api`, `node-api`): each `/api/health` response includes a `commit` field with the commit the running build came from (from Railway's build commit variable, `unknown` if absent), so a reviewer can confirm the fix is live. A reproducible Docker image is out of scope.

### P2

- FR-014 (DGB-04): Offer a passphrase option and Argon2id with a versioned encryption envelope and a migration for existing wallets. Until then, the PIN limitation is stated plainly in the UI and in `docs/SECURITY.md`.

## Release and disclosure order

1. Build and test the P0 fix locally (commit without pushing).
2. Run a read-only query on production listing stored webhook URLs that would fail the new rules (non-HTTPS, private or loopback host, ports outside 443/8443/8080/8000), then deploy.
3. Push the fix branch, spec and tasks to the public repo.
4. Confirm the deployed commit through each API's `/api/health` `commit` field (FR-015). Reviewer rechecks DGB-01 and DGB-02 on production (DGB-03 on staging with controlled destinations).
5. Publish the advisory (GHSA-j88j-g3gf-jmr2) and credit the reviewer.

## Success Criteria

- SC-001: Anonymous calls can no longer create or delete directory entries, and a regression test covers it.
- SC-002: Tests show Digi-ID callback and webhook URLs pointing at loopback, private, link-local, metadata IPs or a redirect are refused, including a DNS-rebinding case.
- SC-003: A regression test runs from self-service registration through the webhook test with a hostile URL and confirms no request is sent.
- SC-004: A header scan of the wallet shows the FR-012 headers.
- SC-005: Reviewer rechecks DGB-01 and DGB-02 in production after deploy, and DGB-03 in staging with controlled destinations.
- SC-006: NuGet and npm audits are re-run and show no unresolved production advisories.
- SC-007: A test sends a spoofed `X-Forwarded-For` header and confirms the rate limit still counts the real client address, and that the 11th Digi-ID call in a minute and the 6th registration in an hour each return `429`.
- SC-008: A test shows that when the login site times out, cannot be resolved, drops the connection or returns an oversize reply, the Digi-ID proxy returns `502` with `{"source":"proxy","reason":"site unreachable"}` and never an unhandled error.
- SC-009: Each API's `/api/health` returns a `commit` field (covered by a test), and after the P1 deploy the wallet files served by `dgbwallet.app` match the published release manifest for the tagged commit.

## Open questions

- Open: confirm Railway exposes the commit variable (`RAILWAY_GIT_COMMIT_SHA`) at runtime for Dockerfile deployments, so FR-015 can read it. Check while doing T033; if absent, pass it as a Docker build argument instead.
- Resolved: the wallet is served by Nginx (`docker/web/nginx.conf`, Railway service `digibyte-web`), so FR-012 headers are set there. See research.md R5 for the per-location `add_header` caveat.
- Resolved: the wallet only calls directory lookups (`Remittance.razor`), so removing register and delete (FR-001) has no client impact.
- Resolved: the proxy keeps relaying the site's status and truncated body (see Clarifications), so relying parties see no change.
