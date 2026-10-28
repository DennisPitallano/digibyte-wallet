# Quickstart: Validate the Railway Node

Prerequisites: Railway Pro workspace, Railway CLI or dashboard, a public explorer for height comparison. Variable names are in [contracts/railway-services.md](contracts/railway-services.md).

## 1. Deploy

1. Create a **new** Railway project.
2. Add service `digibyted` from `docker/digibyted/Dockerfile` with `DGB_VERSION=9.26.7`, the Railway start command, a 100 GB volume, secret variables for RPC user/password, and the allowed private address range (`DGB_RPC_ALLOW_IP`). Add **no** public domain.
3. Add service `node-api` with the variables from the contract, a small volume for its DigiDollar index and metrics files, and a public domain.

Expected: both services deploy; `digibyted` logs show the v9.26.7 banner and block sync starting; `node-api` health returns 200 with `status: syncing`.

## 2. Check version and sync

```bash
curl -s https://<node-api-domain>/api/health
```

Expected: `nodeVersion` = 9.26.7, `lag` shrinking, `peers` > 0. When synced: `status: ok`, height within 3 of the explorer, `txindex: true`.

## 3. Prove RPC is private

From outside Railway, confirm nothing answers on port 14022 of any public hostname of the project:

```bash
curl -m 10 -s -o /dev/null -w "%{http_code}\n" https://<node-api-domain>:14022/
```

Expected: connection refused/timeout. Also confirm in the Railway dashboard that `digibyted` has no public domain and no TCP proxy for port 14022, and that `node-api` exposes only its HTTP port.

## 4. Consumer reads and broadcast

```bash
curl -s https://<node-api-domain>/api/blockchain/height
curl -s https://<node-api-domain>/api/address/<known-address>/balance
```

Expected: correct height and balance. Broadcast a known-valid signed test transaction through the existing broadcast route and see it in the explorer.

## 5. Operator-only routes are closed

Call a wallet/key/mining/invalidate route without `X-NodeApi-Key`.

Expected: 401/403/404, never 200.

## 6. Restart without resync

Redeploy `digibyted`. Expected: serving again within 10 minutes, `blocks` continues from the previous height (not 0).

## 7. Wallet cut-over and degradation

1. Point a staging wallet at the new `node-api` URL; check balance, UTXOs, send, DigiDollar views.
2. Stop `digibyted`; the wallet must fall back to its explorer mode with a degraded-mode notice and keep DGB balance/receive/send working.

## 8. Alerts and cost

1. Stop the node 10 minutes; confirm the monitor alert fires.
2. After one week, record RAM/CPU/volume usage and projected monthly cost against the $40 target and $60 ceiling.
