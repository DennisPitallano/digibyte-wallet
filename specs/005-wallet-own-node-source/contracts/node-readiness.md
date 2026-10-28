# Contract: How the Wallet Reads the Node's Readiness

Consumes `GET {ApiUrl}/api/health` from feature 004 ([../../004-railway-digibyte-node/contracts/nodeapi-health.md](../../004-railway-digibyte-node/contracts/nodeapi-health.md)). No authentication, no secrets, safe to call from the browser.

## Request

- One `GET` per check interval, with a 3 second timeout and **no retries**.
- Never on a user's request path; run in the background.

## Fields the wallet uses

| Field | Use |
|---|---|
| `status` | Must be `ok` for the node to be ready; `starting`, `syncing`, `unhealthy` are all "not ready" with that reason |
| `blocks` | Node height for the explorer cross-check |
| `lag` | Informational (shown in the detail panel and developer view) |
| `peers` | Informational |
| `txindex` | Must be true |
| `versionOk` | Must be true; if the field is absent, treated as true |
| `nodeVersion`, `recommendedVersion` | Informational |

Fields it does not need are ignored. Unknown fields never cause an error.

## Interpretation rules

| Answer | Result |
|---|---|
| Timeout, network error, blocked by the browser, non-JSON | `Unavailable` (`unreachable` or `malformed`); never "ready" |
| HTTP 503 | `Unavailable` (the node API says the node is unreachable) |
| HTTP 200 with `status` `starting` / `syncing` | `NotReady` with that reason |
| HTTP 200, `status` `ok`, `txindex` false | `NotReady(txindex)` |
| HTTP 200, `status` `ok`, `versionOk` false | `NotReady(version)` |
| HTTP 200, `status` `ok`, all good, height within tolerance of an explorer | **Ready** |
| HTTP 200, `status` `ok`, height differs by more than the tolerance | `NotReady(height-drift)` |
| HTTP 200, `status` `ok`, but no explorer height available | Inconclusive; the state does not change (spec FR-005a) |

## Explorer cross-check

One height lookup from a public explorer per check, using the explorers in their normal order. Tolerance is symmetric (the node may be ahead or behind).

## Data requests once the node is `Active`

Reads and broadcast use the same node API routes as today (`/api/address/*`, `/api/tx/*`, `/api/network/fee/*`, `/api/blockchain/height`). No new endpoints are needed on the node API.

| Route class | Routes | Order when `Active` | Read budget |
|---|---|---|---|
| fast | `/api/tx/{txid}`, `/api/network/fee/*`, `/api/blockchain/height`, `POST /api/tx/broadcast` | node first, explorers for that request if it fails | 5 s |
| address | `/api/address/*` (balance, spendable outputs, batches) | explorers first, node as fallback | 20 s |

The split exists because the node answers address lookups by scanning its whole set of spendable outputs (measured about 14 seconds for one address). A later feature can add an address index to the node, after which the address class can move to node first.

While the node is `Active`, each node read has the budget in the table above. A failed or over-budget read is answered by an explorer for that request and reported to the monitor; 3 consecutive failures demote the node immediately instead of waiting for the next health check, and any success resets the count.
