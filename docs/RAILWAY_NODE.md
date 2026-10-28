# Railway DigiByte Node Runbook

A full DigiByte Core node (unpruned, transaction index, no wallet) and its own copy of the node API, in a **separate Railway project**. Spec and design: `specs/004-railway-digibyte-node/` (spec, plan, research, contracts, quickstart).

**Shape of the deployment**

| Service | Image | Public? | Purpose |
|---|---|---|---|
| `digibyted` | `docker/digibyted/Dockerfile` | **No** (no domain, no RPC TCP proxy) | DigiByte Core v9.26.7, RPC on port 14022 reachable only inside the project |
| `node-api` | `docker/node-api/Dockerfile` | Yes (HTTPS) | The one public surface; talks to `digibyted` over the private network |

Never paste secret values (RPC password, operator key) into the repo, chat, logs or this file. They live only in Railway variables.

## First deploy

1. Create a **new** Railway project (separate from the wallet and DigiPay projects).
2. Add service `digibyted` from `docker/digibyted/Dockerfile`:
   - Build argument `DGB_VERSION=9.26.7` (the image downloads the release and checks it against that release's `SHA256SUMS`; the build fails on a mismatch).
   - Start command `/etc/digibyte/entrypoint-railway.sh`.
   - Volume of 100 GB mounted at `/root/.digibyte`.
   - Variables:

     | Name | Value |
     |---|---|
     | `DGB_RPC_USER` | any non-trivial user name |
     | `DGB_RPC_PASSWORD` | long random secret (the node refuses `changeme`) |
     | `DGB_RPC_ALLOW_IP` | the project's private address range (not secret; comma separated for several). Find it in the project's private networking details. The node refuses to start without it, and refuses `0.0.0.0/0` |
     | `DGB_RPC_BIND` | optional; default `0.0.0.0`. Use `::` if the private network is IPv6 only |
     | `DGB_DBCACHE_MB` | optional; default 512. Raise (e.g. 1500) for the first sync, lower afterwards |

   - **Do not** add a public domain or a TCP proxy for RPC (14022). Inbound peer connections are optional: outbound-only is enough to sync and relay. If you want inbound peers, add a TCP proxy for P2P port 12024 only.
3. Add service `node-api` from `docker/node-api/Dockerfile` (build context: repo root):
   - Public domain, health check path `/api/health` (give it a generous timeout for cold start).
   - A small volume (start 1 GB) for its DigiDollar index and metrics files, mounted at `/data`.
   - Variables:

     | Name | Value |
     |---|---|
     | `ASPNETCORE_ENVIRONMENT` | `Production` |
     | `DIGIBYTE_HOST` | `digibyted`'s private DNS name (check the exact form in Railway's private networking panel) |
     | `DIGIBYTE_RPC_USER` / `DIGIBYTE_RPC_PASSWORD` | reference the `digibyted` variables so there is one source of truth |
     | `DIGIBYTE_TESTNET` | `false` |
     | `DIGIDOLLAR_ENABLED` | `true` (read routes only; no node wallet) |
     | `DIGIDOLLAR_WALLET` | blank (a single space if Railway rejects an empty value): no node wallet, so the fee-reserve monitor stays idle |
     | `DIGIDOLLAR_INDEX_DB` | `/data/dd-index.db` |
     | `NODEAPI_METRICS_DB` | `/data/metrics.db` |
     | `NODEAPI_WRITE_KEY` | secret operator key for operator-only routes |
     | `NODEAPI_WALLET_ORIGINS` | wallet origins, comma separated |
     | `DGB_RECOMMENDED_VERSION` | `9.26.7` (health check compares the running node to it) |

   In Production the node API refuses to start with empty or `changeme` RPC credentials.
4. Wait for the first sync (see "Measurements" below). During sync `/api/health` answers 200 with `status: "syncing"`; it is not a failure.
5. Run the checks in `specs/004-railway-digibyte-node/quickstart.md` (steps 1 to 8).

### Measurements (fill in during the first sync)

| Item | Value |
|---|---|
| Sync start / finish | |
| Peak RAM | |
| Disk used when synced | |
| Time to resume after a redeploy | |

## Health signal and alerts

`GET /api/health` returns `status` (`starting`, `syncing`, `ok`, `unhealthy`), `nodeVersion`, `blocks`, `headers`, `lag`, `peers`, `txindex`, `recommendedVersion`, `versionOk`. HTTP 200 for the first three states and 503 only when the node cannot be reached. Contract: `specs/004-railway-digibyte-node/contracts/nodeapi-health.md`.

Set up an external uptime monitor that checks every minute (or faster) and alerts when any of these holds:

- non-200 for 5 minutes
- `lag > 10`
- `peers == 0`
- `versionOk == false`
- `txindex == false` after the node is synced
- `status` stays `starting` for more than 30 minutes

In Railway also enable: deploy-failure and crash notifications, a cost alert at $40, a hard usage limit at $60, and a disk alert at 80% of the volume. Confirm the exact options available on the current plan.

## Credential rotation

1. Change `DGB_RPC_PASSWORD` on `digibyted` (and the referenced value on `node-api`), and/or `NODEAPI_WRITE_KEY` on `node-api`.
2. Redeploy `digibyted` first (the start script rewrites its config on every start), then `node-api`.
3. Update any consumer that sends `X-NodeApi-Key` (DigiPay API).

No resync is needed. If a secret may have leaked, rotate immediately.

## Upgrade

1. Read the new release's notes. Check the minimum version needed for the next rule change: **mainnet Thaw Day is block 24,490,000; every full node must run v9.26.6 or newer before that height**.
2. Back up first if any wallet exists (this node has none, so chain data does not need a backup; it can be re-synced).
3. Set `DGB_VERSION` to the new version on `digibyted` and redeploy. The image build verifies the download against the release's `SHA256SUMS`. A checksum detects changed bytes; it is not a digital signature.
4. Set `DGB_RECOMMENDED_VERSION` on `node-api` to the same value (it is a second setting, so both must be updated on every upgrade).
5. Check `/api/health`: `nodeVersion` equals the new version, `versionOk: true`, node back to `ok`.

A normal upgrade between patch releases of the same line does not need a reindex (see the release notes).

Rehearsal timings (fill in after the rehearsal on a throwaway copy): operator effort ____ minutes, node downtime ____ minutes, resumed without resync: yes / no. Targets: under 30 minutes effort and under 10 minutes downtime.

## Recovery and reindex

- **Node restarts or redeploys**: it resumes from the volume. Expect it to answer `starting` while it loads the block index, then `syncing`, then `ok`.
- **Volume full**: enlarge the volume in Railway (Pro allows up to 1 TB) and redeploy. Act at the 80% alert.
- **Corrupt chain data**: stop the service, then start once with the extra argument `-reindex` (service start command arguments), and remove the argument afterwards. A reindex takes hours. If that fails, delete the volume contents and let the node sync from scratch; no funds are at risk because the node holds no wallets.
- **Node API index looks wrong or empty**: the DigiDollar index in the `node-api` volume rebuilds itself from the node; it is only slow, not a data loss.

## Cutting the wallet over

1. Prove the new node API with the quickstart checks first (balance, UTXOs, broadcast, DigiDollar reads, operator-only routes closed, RPC not reachable from the internet).
2. Repoint a staging wallet by changing only `NodeApiUrl`, then production.
3. Keep the old node API running as the **rollback**: to roll back, set `NodeApiUrl` back to the old address. Remove it only after the new one has been stable.

If the node is unreachable the wallet already falls back to public explorers in a clearly labelled degraded mode.

## Operator-only routes

Outside Development, node control, key, wallet, mining, PSBT, descriptor, `tx/create`, `tx/fund`, block invalidate/reconsider, and costly or peer-revealing routes need the `X-NodeApi-Key` header and answer 503 when no key is configured. The full table of what is open and what is gated, and why, is in `specs/004-railway-digibyte-node/research.md` (section R6a).

## Cost review

After the first week, record real RAM, CPU, volume and egress usage and the projected monthly cost.

| Item | Value |
|---|---|
| Projected monthly cost | |
| Share of time within 3 blocks of an explorer | |
| Target / ceiling | $40 target, $60 hard ceiling (including the $20 Pro credit) |

If the projection is above $60, stop and revisit the design rather than raising the limit.

## Teardown

1. Point the wallet back at the previous node API (see "Cutting the wallet over").
2. Delete the `node-api` and `digibyted` services, then the volumes, then the project. Deleting the volumes is safe: the node holds no wallets or keys.
3. Remove the monitor and the cost alerts.

## Wallet settings (own node as a data source)

The wallet web service can use this node, through the node API, as a blockchain data source: for sending, transaction lookups, fees and chain height. Address balances and spendable outputs still come from public explorers first (the node answers those by scanning its whole set of spendable outputs, about 14 seconds), with the node as their backup, and address history always comes from explorers. The wallet uses the node only while the node's health report says it is ready, and goes back to explorers by itself when it is not. Design: `specs/005-wallet-own-node-source/`.

Settings are environment variables on the **wallet web service** (not on the node project). They are written to `config.json` each time the container starts, so a change needs a restart, not a rebuild:

| Variable | Meaning | Default |
|---|---|---|
| `WALLET_NODE_ENABLED` | `true` turns node use on | off |
| `WALLET_NODE_API_URL` | The node API address, e.g. `https://node.example.org` | the existing `NodeApiUrl` |
| `WALLET_NODE_CHECK_SECONDS` | Seconds between readiness checks (10 to 300) | 30 |
| `WALLET_NODE_STABLE_SECONDS` | Seconds the node must stay ready before it is used (at least two check intervals) | 120 |
| `WALLET_NODE_HEIGHT_TOLERANCE` | Blocks the node may differ from a public explorer (1 to 50) | 5 |
| `WALLET_DEV_VIEW` | `true` shows the developer diagnostics page | off |

Nothing in this file is secret: any visitor's browser can read it. Turn node use off again by removing `WALLET_NODE_ENABLED` (or setting it to `false`) and restarting the service.

**Limit: the content-security policy.** The wallet page only lets the browser talk to servers listed in its content-security policy (`src/DigiByte.Web/wwwroot/index.html`). The node API's address must be listed there, as one exact origin (no wildcard). So the address can be changed by these settings alone only to an origin the policy already allows; moving to a new origin needs a reviewed edit of the policy first. Give the node API a stable custom domain so this never has to change.

**Switching on, in order:** deploy the web service with the new image; check the node API's `/api/health` shows `ok`; set `WALLET_NODE_ENABLED=true` and `WALLET_NODE_API_URL`, restart; open the wallet's data-source indicator (a small dot) and confirm it reports the node, or syncing if it is not ready yet. **Roll back** by turning `WALLET_NODE_ENABLED` off and restarting.
