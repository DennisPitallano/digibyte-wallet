# Contract: NodeApi Health Signal

`GET /api/health` (public, no auth, no secrets).

## Response (JSON)

| Field | Type | Meaning |
|---|---|---|
| `status` | `starting` / `ok` / `syncing` / `unhealthy` | Overall state (`starting` = node answers but is still loading its block index) |
| `nodeVersion` | string | Running DigiByte Core version |
| `blocks` | int | Validated tip height |
| `headers` | int | Best known header height |
| `lag` | int | `headers - blocks` |
| `peers` | int | Connected peers |
| `txindex` | bool | Transaction index ready |
| `recommendedVersion` | string | Version the deployment is configured for |
| `versionOk` | bool | `nodeVersion >= recommendedVersion` |

## HTTP status

| Condition | Status |
|---|---|
| `starting` | 200 |
| `ok` | 200 |
| `syncing` (including lag after downtime) | 200 (so Railway does not restart a syncing node) |
| Node RPC unreachable / auth failure | 503 (differs from the old handler, which returned 200 even when disconnected) |

## Monitor rules (external uptime monitor reading the body)

Check at least every 1 minute. Alert when any of: non-200 for 5 min; `lag > 10`; `peers == 0`; `versionOk == false`; `txindex == false` after sync.

## Rules

- No RPC credentials, wallet data or file paths in the response.
- Existing consumers that only check for HTTP 200 keep working.
- Response must be cheap (no walk over historical blocks).
