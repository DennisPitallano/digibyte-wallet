# Research: Railway-Hosted DigiByte Core Node

Items marked **(verify)** were not confirmed from an authoritative source during planning and must be checked at deploy time.

## R1 - Which release

- **Decision**: DigiByte Core **v9.26.7** (GitHub release published 2026-10-06; asset `digibyte-9.26.7-x86_64-linux-gnu.tar.gz` plus `SHA256SUMS`).
- **Rationale**: Latest release. Release notes say all full nodes must run v9.26.6+ before mainnet block 24,490,000 (Thaw Day); v9.26.7 adds chain-info speedups and a shutdown fix with no new activation or reindex. The repo's `DGB_VERSION=8.26.2` is too old.
- **Alternatives**: v9.26.6 (has the rules but not the fixes); building from source (slow, no benefit).

## R2 - Config for a full node

- **Decision**: `server=1`, `txindex=1`, no `prune`, `printtoconsole=1`, no credentials in the committed file. `rpcuser`/`rpcpassword` written at start from Railway variables.
- **Rationale**: Clarification answer (full node). v9.26.x keeps DigiDollar history and enables txindex by default since v9.26.3. The existing `digibyte-mainnet.conf` (`prune=550`, committed password) is unsuitable.
- **Alternatives**: pruned (rejected in clarification); `rpcauth` salted hash instead of plain password (better; entrypoint can generate the hash from a variable. Chosen if NodeApi can still authenticate with the plain password, which it can).

## R3 - Network exposure on Railway

- **Decision**: RPC bound inside the container, allowed only from the project's private network range; **no public domain or TCP proxy for the RPC port**. P2P port (12024) left outbound-only by default; an inbound TCP proxy is optional.
- **Rationale**: Railway private networking is per project (services reach each other by internal DNS name). Inbound peers are not needed for syncing or relaying.
- **(verify)** exact private address range (passed to the node as `DGB_RPC_ALLOW_IP`, set during first deploy) and whether it is IPv6-only in this project (affects `rpcbind`/`rpcallowip` and how NodeApi's host is written).
- **Alternatives**: public RPC with a password (rejected, FR-005); VPN/tunnel for other projects (deferred with User Story 3).

## R4 - Disk and cost

- **Decision**: Start with a volume of **100 GB**, grow as needed (volumes can be resized; Pro supports up to 1 TB). Node limits ~2 GB RAM, ~1 vCPU burst. NodeApi small (≤0.5 GB).
- **Rationale**: Chain + txindex size is **unverified**; planning estimate 60-100 GB. Railway bills usage (RAM, CPU, volume GB-months, egress).
- **Risk**: At typical published Railway rates **(verify)**, 2 GB RAM + modest CPU + 100 GB volume can approach or exceed the $40 total. Mitigations: measure real usage during the first sync week, set a Railway cost alert at $40 and a hard usage limit at $60 (clarified ceiling), tune `dbcache`/`maxconnections` down after initial sync, and revisit the budget or node type if it doesn't fit. If it can't fit, the fallback is a pruned node, which would need a new clarification because of DigiDollar requirements.

## R5 - Initial sync time

- **Decision**: Sync from peers; raise `dbcache` (e.g. 1-2 GB) only during initial sync, then lower it. No snapshot bootstrap.
- **Rationale**: No trusted official chain snapshot is documented; importing an unverified one would weaken trust. A full sync may exceed 24 h on shared Railway CPU/disk; SC-001 is therefore a target to measure, not a guarantee. Healthcheck must not kill the service while syncing.
- **Alternatives**: third-party snapshot (rejected: trust); larger instance for the first sync then scale down (acceptable, cheap, operator's call).

## R6 - NodeApi in a public deployment

- **Decision**: The wallet PWA calls NodeApi directly from the browser (`NodeApiUrl`), so NodeApi stays public with CORS limited to the wallet origins. Public profile must expose only read and broadcast routes. Operator-only routes (wallet/key endpoints, mining, `block/invalidate|reconsider`, faucet) must be disabled or gated by the existing `X-NodeApi-Key` operator key, failing closed when unset.
- **Rationale**: `src/DigiByte.NodeApi/Endpoints/` contains Key, Wallet, Mining, Faucet and block-invalidate routes. The operator-key mechanism exists for DigiDollar wallet-scoped routes; whether every sensitive route is covered is **not yet audited**. This is the main security task.
- **Route audit (R6a)**: the deny-list must come from a real audit of every caller of each route (task T039), not from the Web client alone; the result is recorded here when done.
- **Local state**: NodeApi keeps a DigiDollar index and metrics store as SQLite files, so the `node-api` service needs its own small volume (task T040).
- **Alternatives**: put a separate read-only proxy in front (more moving parts; deferred with the gateway).

## R7 - DigiDollar wallet-scoped routes

- **Decision**: Run the node with `disablewallet=1` and keep `DigiDollar:Enabled` for read routes only (T010: checked by reading the code, see below); leave the hot-wallet (`digipay-hot`) scoped routes disabled on this node in this release.
- **Rationale**: FR-014 says the node holds no wallets. Existing DigiDollar read endpoints (`/deployment`, `/stats`, address history/UTXOs, tx status) work without a node wallet. Enabling the hot wallet is a separate decision tied to DigiPay.

## R8 - Health and alerting

- **Decision**: Railway healthchecks are HTTP-only, so the health signal lives on NodeApi (`/api/health`), extended to report node version, block height vs header height, peer count and a status of `syncing` / `ok` / `unhealthy`. HTTP 200 for `syncing` and `ok`, 503 only when the node is unreachable. An external uptime monitor additionally alerts on `lag > 10 blocks`, `peers = 0`, or `version < recommended` (reads the JSON body). Railway notifications cover crashes/deploy failures; a workspace usage limit covers cost.
- **(verify)** Railway alert/notification options and usage-limit behaviour in the current plan.
- **Alternatives**: sidecar service posting to chat (more to run; optional later).

## R9 - Upgrades and backups

- **Decision**: Upgrade = change `DGB_VERSION`, redeploy; Dockerfile verifies the tarball against `SHA256SUMS` from the same release (checksum catches changed bytes only, not a signature; release notes say so). Chain data needs no backup (re-syncable); if a wallet were ever loaded, it would be backed up first. Railway volume backups are optional.
- **Rationale**: Release notes confirm a normal upgrade from a working v9.26.6 needs no reindex.

## R10 - Wallet cut-over

- **Decision**: New NodeApi gets its own public Railway domain; wallet `NodeApiUrl` and `ClientOrigin`/CORS are changed by configuration. Keep the old endpoint until the new one is proven, then switch; the old one is the rollback.
- **Rationale**: SC-005 (no user-visible difference) and constitution principle V.

## R6a - Route audit (task T039)

Searched `src/`, `sdk/`, `samples/`, `tests/`, `docker/`, compose files, `scripts/`, `tools/`, `digipay.postman_collection.json` and `test-nodeapi.ps1`. Real callers of NodeApi:

- Wallet (`src/DigiByte.Wallet/Services/NodeApiBlockchainService.cs`): `/api/address/{a}/balance`, `/api/address/{a}/utxos`, `POST /api/address/balances`, `POST /api/address/utxos`, `POST /api/tx/broadcast`, `GET /api/tx/{txid}`, `GET /api/network/fee/6`, `GET /api/blockchain/height`.
- Wallet (`src/DigiByte.Wallet/Services/DigiDollarNodeApiClient.cs`): `/api/digidollar/addresses/history|utxos`, `/api/digidollar/health`, `/api/digidollar/tx/{txid}/status`.
- DigiPay API uses the same DigiDollar client; its wallet-scoped routes already require `X-NodeApi-Key` through `WriteKeyAuthMiddleware`.
- `/api/health` is called by Railway healthchecks of every service. Developer scripts (`test-nodeapi.ps1`, `DigiByte.NodeApi.http`) call wallet, mining and util routes against a local regtest node only.

Decision (Production only; Development unchanged). Gated routes need `X-NodeApi-Key` and fail closed when no key is set:

| Route | Real caller | Decision |
|---|---|---|
| `/api/keys/*` | none (dev scripts) | gate |
| `/api/wallet/*` | none (dev scripts) | gate |
| `/api/mining/*` | none (dev scripts) | gate |
| `/api/faucet/*` | none | gate |
| `/api/psbt/*` | none | gate |
| `/api/descriptor/*` | none | gate (includes costly `scan`) |
| `/api/util/stop`, `/api/util/rescan` | none | gate |
| `/api/util/utxo/lock`, `/api/util/utxo/unlock` | none | gate |
| `/api/util/txoutset` | none | gate (costly) |
| `/api/blockchain/block/invalidate`, `/api/blockchain/block/reconsider` | none | gate |
| `/api/tx/create`, `/api/tx/fund` | none | gate (fund uses a wallet) |
| `/api/network/mempool/raw` | none | gate (costly) |
| `/api/network/peers`, `/api/network/ping` | none | gate (leaks peer addresses / acts on peers) |
| `/api/address/*`, `/api/blockchain/*` reads, `/api/tx/{txid}`, `/api/tx/{txid}/raw`, `/api/tx/decode`, `/api/tx/broadcast`, `/api/tx/testmempool` | wallet | open |
| `/api/network/info`, `/connections`, `/mempool`, `/mempool/entry/*`, `/fee/*`, `/totals` | wallet / monitors | open |
| `/api/util/version`, `/uptime`, `/verify`, `/txout/*` | none | open (cheap reads) |
| `/api/digidollar` public routes, `/api/health`, `/api/metrics` | wallet / platform | open (existing rules unchanged) |

No rate limiting exists in this release; the remaining open routes are cheap reads, and a throttling layer belongs to the deferred gateway.

## R7a - DigiDollar read routes without a node wallet (task T010)

Checked by reading `src/DigiByte.NodeApi/Endpoints/DigiDollarEndpoints.cs` and `Services/DdFeeReserveMonitor.cs`; a live regtest run was not possible in this session (Docker engine was not running), so it is still worth one real run before cut-over (quickstart step 4).

- Public routes (`/deployment`, `/stats`, `/oracle/price`, `/protection`, `/address/validate`, `/collateral/estimate`) call node-level RPCs only. The wallet-facing reads (`/addresses/utxos`, `/addresses/history`, `/tx/{txid}/status`) use the output index and the chain source, not a wallet.
- `/api/digidollar/health` reads the fee reserve through the wallet but catches the failure: without a wallet it returns `wallet_loaded=false` and `reserve_healthy=false`. The wallet client does not use the reserve fields.
- `DdFeeReserveMonitor` would poll the missing wallet every minute and log a warning each time. It now stays idle when `DigiDollar:WalletName` is blank (`DIGIDOLLAR_WALLET` set to a blank value), covered by `DdFeeReserveMonitorTests`.
- Wallet-scoped routes (`/balance`, `/send`, `/mint`, ...) will fail on this node by design; they also need the operator key.
