# Quickstart: Validate the Wallet's Own-Node Mode

Prerequisites: the node API from feature 004 reachable from your browser (its stable origin allowed in the page's CSP), a wallet build from this feature, and a few funded test addresses. Settings are described in [contracts/runtime-config.md](contracts/runtime-config.md); behaviour in [contracts/node-readiness.md](contracts/node-readiness.md) and [contracts/status-ui.md](contracts/status-ui.md).

## 1. Feature off is unchanged

1. Leave `WALLET_NODE_ENABLED` unset.
2. Open the wallet; use balance, history, receive, send.

Expected: identical to before; the browser's network log shows **no** request to the node's health endpoint; no status dot.

## 2. Node syncing: wallet keeps working on explorers

1. Set `WALLET_NODE_ENABLED=true` and the node API URL; restart the web service.
2. Use the wallet while the real node is still syncing.

Expected: balances, history and fees come from the explorers; no errors; the dot is neutral; the detail panel says the own node is syncing; no banner. The network log shows only the health call to the node (every 30 s), no data calls.

## 3. Controlled transitions with a stub health endpoint

Run a stub that returns each health state on demand (a few lines of script that serves `GET /api/health` with a chosen `status`, `txindex`, `versionOk`, `blocks`; details in the implementation tasks), and point `WALLET_NODE_API_URL` at it, with its origin allowed for local testing.

| Step | Stub answers | Expected |
|---|---|---|
| a | `syncing` | Explorers; informational detail only |
| b | `ok`, height matching an explorer | After 2 to 2.5 minutes (and within the 3 minute target): own node active (green dot); privacy line shown; sending, transaction lookups, fees and height go to the node first, while balances and spendable outputs are still answered by the explorers (check the network log) |
| c | `ok`, `blocks` 20 behind an explorer | Within 35 s: back to explorers; banner "node unavailable" (it was active) |
| d | stop the stub | Same as c; no repeated errors on screen |
| e | `ok`, then flip `ok`/`syncing` every 20 s | The wallet does not switch to the node (never stays ready for the stable period) |
| f | `ok` with `versionOk: false` | Not used; detail says out of step or unavailable |
| g | `ok` but explorers unreachable (block them) | State does not change either way |
| h | `ok` but the node's data routes fail or hang (stop the real node API while the stub still answers health) | Each request is answered by an explorer within about 5 s; after 3 failures the wallet stops using the node at once (state not ready, banner) |

Throughout: send and receive still work; balance and history do not flicker or duplicate across switches.

## 4. Sending

- While the node is not ready, send a small transaction: it goes through the explorers first (check the log).
- While the node is active, send: it goes to the node first and falls through to the explorers if the node rejects it.

## 5. Everything down

Block the explorers and the node. Expected: last known balance marked out of date, the "can't reach the blockchain" banner, sending explains why it is unavailable. Unblock: banner clears by itself.

## 6. Developer view and cut-over check

1. Set `WALLET_DEV_VIEW=true`; open the developer page.
2. Against the real node once it reports `ok`: enter at least 20 sample addresses and run the comparison.

Expected: readiness snapshot matches `/api/health`; the comparison shows matching balances, output counts and heights (SC-008). With `WALLET_DEV_VIEW` unset the page shows "not found".

## 7. Cut-over

When the node has reported `ok` and the comparison matches: nothing to deploy. The wallet switches by itself after the stable period. To roll back instantly: set `WALLET_NODE_ENABLED=false` and restart the web service.
