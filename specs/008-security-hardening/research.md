# Research: Security Hardening

## R1. Directory mutations (FR-001)

- **Finding**: `Remittance.razor` only calls `GET api/directory/lookup/...` and `lookup/phone/...`. No shipped client calls `POST /register` or `DELETE /{username}`. The store is an in-memory dictionary that resets on restart.
- **Decision**: Remove `POST /register` and `DELETE /{username}` outright in the hotfix. Keep lookups. Re-introducing registration is a separate feature (FR-002) with its own spec.
- **Rationale**: Zero client impact, smallest possible fix, and nothing is lost on restart anyway.
- **Alternatives**: Return 403/410 (keeps dead routes, no benefit); add an API key (a key shipped in a static client is public).
- **Note**: Lookups of an empty store return 404. Confirm `Remittance.razor` handles that, since all entries vanish on restart today anyway.

## R2. Digi-ID callback (FR-004 to FR-007)

- **Finding**: `Identity.razor` posts `{callbackUrl, address, uri, signature}` where `callbackUrl` comes from the parsed `digiid://` URI. The server trusts it.
- **Decision**: The server ignores any `callbackUrl` and derives the callback from `uri`: `digiid://host[:port]/path?x=nonce[&u=1]` becomes `https://host[:port]/path?x=nonce` (`u=1` means http, which we refuse). It verifies the message signature for `uri` and `address` with `DigiByte.Crypto` before forwarding. If `callbackUrl` is present and differs from the derived URL, return 400.
- **Rationale**: The URI is what the user signed, so binding to it removes caller choice without breaking the existing client contract.
- **Alternatives**: Remove the proxy and let the browser POST directly. Rejected for the hotfix because many relying parties lack CORS, which is why the proxy exists.
- **Residual risk**: A user can still be tricked into signing a malicious `digiid://` URI. That is a client-side phishing concern, out of scope here, and the guard still blocks internal targets.

## R3. SSRF guard (FR-006, FR-008)

- **Decision**: One `OutboundUrlGuard` that (a) checks the URL is absolute HTTPS with no userinfo, standard ports only (443; allow-list configurable), (b) resolves DNS and rejects private, loopback, link-local, multicast, reserved, CGNAT and cloud-metadata ranges for IPv4 and IPv6 (including IPv4-mapped IPv6), and (c) enforces the check at connect time through `SocketsHttpHandler.ConnectCallback` so DNS rebinding cannot swap the IP after validation. `AllowAutoRedirect = false`. Timeout 5 to 10 s, response read capped (e.g. 64 KB).
- **Rationale**: Validating only the URL string at registration is bypassable via DNS and redirects. Connect-time checking is the reliable control.
- **Alternatives**: Hostname deny-lists (bypassable); egress firewall only (not available on every host; kept as defense in depth).
- **Dev exception**: Config flag `Outbound:AllowLoopback` (default false, only set in Development) for local webhook testing.

## R4. Webhooks (FR-008 to FR-011)

- **Finding**: `PaymentsEndpoints.cs:70` stores `WebhookUrl` unvalidated at registration. `StoresEndpoints.cs` validates scheme only and allows `http://localhost`. The dispatcher also stores up to 2 KB of the response in `ResponseSnippet`, shown to the merchant, which makes a blind SSRF into a read-back SSRF.
- **Decision**: Validate with the guard's URL check at registration and update. Re-check at dispatch (the stored value may predate the fix), skipping and logging invalid rows. Keep a 512-character, plain-text preview for any status (maintainer decision), since the connect-time IP check makes the read-back path unreachable.
- **Existing data**: Add a one-off query or startup check listing stored webhook URLs that fail the policy, so operators can see them. No destructive migration.
- **Rate limit**: ASP.NET Core `AddRateLimiter` fixed window on `POST /v1/pay/merchants` and `POST /api/digiid/callback`, keyed by the real client address. Configure `ForwardedHeadersOptions` to trust only Railway's proxy and use the last `X-Forwarded-For` hop. Verify the exact proxy trust setting on Railway before relying on it, since it is easy to get wrong (a wrong setting lets callers spoof the key).

## R5. Headers (FR-012)

- **Finding**: No `frame-ancestors`, `X-Frame-Options` or `nosniff` anywhere in the repo; the only CSP is a `<meta>` tag. The repo shows no host config for `dgbwallet.app`.
- **Resolved**: the wallet is the `digibyte-web` Railway service, static Blazor WASM served by Nginx using [docker/web/nginx.conf](../../docker/web/nginx.conf) (`railway.toml`, service 2). Headers are set there.
- **Decision**: Add the headers in `docker/web/nginx.conf` with `always`. Keep the meta CSP as a fallback. A service worker can't set response headers, so it is not an option.
- **Nginx gotcha**: a `location` block that has its own `add_header` does not inherit the server-level `add_header` lines. Four locations in the file set `Cache-Control` (`/config.json`, the asset regex, `/service-worker.js`, `/service-worker-assets.js`). Put the security headers in one snippet (`docker/web/security-headers.conf`, copied in the Dockerfile) and `include` it in the `server` block and in each of those locations. Test every location, including a 404 and the SPA fallback.
- **Header values**: `Content-Security-Policy: frame-ancestors 'none'` as a header (the full policy stays in the meta tag, so there is one place to maintain it for now), `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`.
- **Out of scope but noted**: `digipay-web` (ASP.NET Core) and the APIs have no such headers either. Same fix would apply there; not part of this review's finding.

## R6. Analytics (FR-013)

- **Finding**: The constitution forbids third-party scripts in the wallet origin, so gtag.js is a compliance violation, not only a review finding.
- **Decision (maintainer, 2026-11-06)**: Remove gtag, `js/analytics.js` and the Google CSP entries. No replacement in this feature. First-party analytics (stats page, JSON endpoint, or self-hosted Plausible/Umami) is a separate spec in a later session.

## R7. PIN KDF (FR-014)

- **Decision**: Separate spec. Needs a versioned envelope (`v2` with Argon2id parameters), a passphrase option, and silent migration on next unlock. Argon2id in WASM needs a vetted implementation (no custom crypto). Not part of the hotfix.

## R8. Build verification (FR-015)

- **Finding**: The wallet already has a verifiable-build process: a `wallet-v*` tag triggers `.github/workflows/wallet-release-manifest.yml`, which publishes a per-file sha256 manifest, and `release-hashes/README.md` describes how to compare it with what `dgbwallet.app` serves. The APIs deploy as Docker builds on Railway and have nothing equivalent.
- **Decision**: For the wallet, cut a `wallet-v*` tag after the P1 deploy and run the documented comparison (T056). For the APIs, add a `commit` field to each `/api/health` (digibyte-api line ~132, digipay-api line ~381, node-api line ~175 in their `Program.cs`) read from `RAILWAY_GIT_COMMIT_SHA`, `unknown` when absent (T055). The repo is open source, so exposing the commit leaks nothing.
- **Rationale**: Cheap, and answers the practical question "is the fixed commit live?" for the reviewer's recheck.
- **Alternatives considered**: Recording Railway image digests by hand (manual, easy to forget); reproducible Docker images (heavy, and hard with Docker layers and NuGet restore, so out of scope).
- **Open**: confirm Railway exposes `RAILWAY_GIT_COMMIT_SHA` at runtime for Dockerfile deployments. If not, pass it as a Docker build argument in `docker/api/Dockerfile`, `docker/pay-api/Dockerfile` and `docker/node-api/Dockerfile`.
