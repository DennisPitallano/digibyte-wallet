# Contract: Railway Services and Variables

New Railway project (separate from existing wallet/DigiPay projects).

## Service `digibyted`

| Item | Value |
|---|---|
| Build | `docker/digibyted/Dockerfile`, `DGB_VERSION=9.26.7` |
| Start | `/etc/digibyte/entrypoint-railway.sh` |
| Volume | one volume at the node data directory, start 100 GB |
| Public domain | **none** |
| TCP proxy | none by default; optional for P2P 12024 |
| Private port | RPC 14022 (mainnet), reachable only inside the project |
| Memory/CPU | ~2 GB / ~1 vCPU to start; measure and tune |

Variables:

| Name | Purpose |
|---|---|
| `DGB_RPC_USER` | RPC user (not `dgbrpc` default in prod; any non-trivial value) |
| `DGB_RPC_PASSWORD` | Long random secret; set as a Railway secret variable |
| `DGB_DBCACHE_MB` | Cache size; high during initial sync, lower afterwards |
| `DGB_RPC_BIND` | Optional; address RPC listens on (default `0.0.0.0`; `::` if the private network is IPv6 only) |
| `DGB_RPC_ALLOW_IP` | Project's private address range allowed to call RPC (not secret; start script refuses to run without it) |

## Service `node-api`

| Item | Value |
|---|---|
| Build | `docker/node-api/Dockerfile` (repo root context) |
| Public domain | yes, e.g. `node.<domain>` |
| Healthcheck path | `/api/health` (see [nodeapi-health.md](nodeapi-health.md)) |
| Healthcheck timeout | long enough for cold start; must pass while node is syncing |
| Volume | small volume (start 1 GB) for the DigiDollar output index and metrics SQLite files; mount path set in task T040 |

Variables:

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `DIGIBYTE_HOST` | `digibyted`'s private DNS name **(verify exact form)** |
| `DIGIBYTE_RPC_USER` / `DIGIBYTE_RPC_PASSWORD` | Reference the `digibyted` variables |
| `DIGIBYTE_TESTNET` | `false` |
| `DIGIDOLLAR_ENABLED` | `true`; read routes only, hot wallet off (see research R7) |
| `DIGIDOLLAR_WALLET` | blank (or one space): no node wallet, fee-reserve monitor stays idle |
| `NODEAPI_WRITE_KEY` | Secret operator key; required for operator-only routes |
| `DGB_RECOMMENDED_VERSION` | Version the health check compares against (update with every upgrade) |
| (defaults) | Mainnet RPC port 14022 comes from the app defaults; the faucet must stay off |
| `DIGIDOLLAR_INDEX_DB` | Path of the DigiDollar index file, inside the node-api volume |
| `NODEAPI_METRICS_DB` | Path of the metrics file, inside the node-api volume |
| (volume mount) | `/data`. The app's defaults are `data/dd-index.db` and `data/metrics.db` (relative to the app directory, so they would be lost on redeploy), hence the two variables above |
| `NODEAPI_WALLET_ORIGINS` | Wallet PWA origins only (comma-separated) |

## Consumers

| Consumer | Change |
|---|---|
| Wallet PWA | `NodeApiUrl` -> new `node-api` public URL |
| Wallet API / DigiPay | Only if they call NodeApi; point at new URL (or move into this project and use the private address) |

## Must not exist

- Any public route to port 14022.
- Credentials committed to the repo or printed in logs.
- Default password `changeme` accepted in Production.
