# Data Model: Railway-Hosted DigiByte Core Node

This feature adds no application database. The "data" is deployment configuration and the signals the operator watches.

## Node Service

| Field | Description |
|---|---|
| version | Running DigiByte Core version; must equal the selected `DGB_VERSION` |
| network | `main` |
| blocks / headers | Validated tip vs best known header; `headers - blocks` is the lag |
| verificationProgress | 0-1 sync estimate |
| peers | Connected peer count |
| txindex | Must be true; index fully built |
| state | `starting`, `syncing`, `ok`, `unhealthy` |

State transitions: `starting -> syncing -> ok`; `ok -> syncing` after downtime; any -> `unhealthy` when RPC is unreachable. `syncing` is not a failure.

## Chain Data Volume

| Field | Description |
|---|---|
| mountPath | Node data directory |
| sizeGb / usedGb | Provisioned and used; alert at 80% used |
| growthPerMonthGb | Measured, used to project exhaustion |

Rule: survives redeploys; losing it means a full resync, not a loss of funds.

A second, small volume belongs to `node-api` (DigiDollar output index and metrics files). Losing it means the index is rebuilt from the node, which is slow but not a loss of funds.

## Node Credentials

| Field | Description |
|---|---|
| rpcUser / rpcPassword | Railway variables shared by the node and NodeApi; never in git or logs |
| operatorKey | `X-NodeApi-Key` value for operator-only NodeApi routes |

Rule: rotating changes variables on both services and redeploys; no resync.

## Consumer App

| Field | Description |
|---|---|
| name | Wallet PWA, wallet API, DigiPay, future apps |
| reaches | NodeApi public URL (browser) or private address (same-project services) |
| allowedOrigins | CORS list for browser consumers |

## Deployment Config (per service)

Variable names and values are defined in [contracts/railway-services.md](contracts/railway-services.md).
