# Quickstart: Validate the hotfix

Prerequisites: .NET 10 SDK, repo built. Run against a local instance, never production.

```bash
dotnet test tests/DigiByte.Server.Shared.Tests
dotnet test tests/DigiByte.Api.Tests
dotnet test tests/DigiByte.Pay.Api.Tests
```

## Scenarios

1. **Directory**: `POST /api/directory/register` and `DELETE /api/directory/x` return 404/405. `GET /api/directory/lookup/anything` returns 404 without error. See [contracts/directory.md](contracts/directory.md).
2. **Digi-ID**: a callback with a mismatched `callbackUrl`, a bad signature, an `http` target, a loopback or `169.254.169.254` target, or a redirecting target each fail without any outbound request. A site that times out or can't be resolved returns `502` with reason `site unreachable`. A valid signed request to a public HTTPS test endpoint still succeeds. See [contracts/digiid-callback.md](contracts/digiid-callback.md).
3. **Webhooks**: register a merchant with `http://127.0.0.1`, a private IP, a metadata IP, and a hostname that resolves to private. Each is rejected with 400. A store update with the same is rejected. A stored legacy bad URL is skipped at dispatch. See [contracts/webhook-url-policy.md](contracts/webhook-url-policy.md).
4. **Rate limits**: the 11th Digi-ID callback in a minute and the 6th merchant registration in an hour from one client return `429`. A spoofed `X-Forwarded-For` does not reset the count (SC-007). See [research.md](research.md) R4 for the proxy trust setting.
5. **Regression**: registration through `POST /{storeId}/webhook/test` with a hostile URL sends no request (SC-003).
6. **Headers (P1)**: `curl -sI` against `/`, `/config.json`, a `.js` file, `/service-worker.js`, an unknown route and a 404 each show `content-security-policy` with `frame-ancestors 'none'`, `x-frame-options: DENY`, `x-content-type-options: nosniff` and `referrer-policy: no-referrer`. Every location must show them, since Nginx locations with their own `add_header` drop inherited ones. Build the image with `docker build -f docker/web/Dockerfile .` and run it locally to check.
7. **Analytics (P1)**: the wallet page makes no request to `googletagmanager.com` or `google-analytics.com`, and the CSP no longer lists them.
8. **Pre-deploy query (production, read-only, Postgres)**: `scripts/webhook-url-audit.sql` lists stores whose `WebhookUrl` is not `https`, has a non-allowed port, has userinfo, or has a private, loopback or IP-literal host. The maintainer runs it before deploying so affected merchants are known: `psql "$DATABASE_URL" -f scripts/webhook-url-audit.sql`. It changes nothing. A hostname that only resolves to a private address cannot be found in SQL; the dispatcher catches those at send time. The script was written against the EF migration's table and column names (`"Stores"`, `"WebhookUrl"`) and has not been run against a live PostgreSQL database from this repository.
9. **Egress (deploy-time)**: restrict outbound network access for `digibyte-api` and `digipay-api` on Railway where it allows, and record whether it does (FR-007, T037). This is a platform setting, not code. Record here what Railway offers: _not yet confirmed_ (the guard in code is the control that ships; see `docs/SECURITY.md`, "Outbound requests to user-supplied URLs").
   **Proxy trust (T011/T012)**: rate limits key on the rightmost `X-Forwarded-For` entry, captured before the forwarded-headers middleware runs (`ClientAddress` in `DigiByte.Server.Shared`; the Api's sponsor limiter already used the same rule). Before relying on it, confirm on Railway that the proxy appends the real client address as the last entry, e.g. `curl -H "X-Forwarded-For: 1.2.3.4" https://<service>/api/health` and check what the service sees. If Railway ever puts something else last, the limits would key on that instead.
10. **Build verification (FR-015)**: `curl` each API's `/api/health` and confirm `commit` matches the deployed commit (T033; if the value is `unknown`, Railway does not expose `RAILWAY_GIT_COMMIT_SHA` at runtime for Dockerfile deployments, so pass it as a Docker build argument in the three Dockerfiles). After the P1 PR is deployed, tag `wallet-v*`, then compare `dgbwallet.app` files with the release manifest (T056, `release-hashes/README.md`).
11. **After deploy**: reviewer rechecks DGB-01 and DGB-02 on production and DGB-03 on staging with controlled destinations (SC-005).
