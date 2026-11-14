# Data Model: P2P Light-Client Gateway

Entities from the spec, with the fields, rules and lifecycle each needs. "Gateway" data lives in the service's SQLite file. "Wallet" data lives in the browser's IndexedDB. Neither side stores keys or seed material in these records.

## Gateway side

### HeaderRecord (Chain view)

| Field | Type | Rule |
|---|---|---|
| Network | enum | mainnet or testnet. Part of the key. |
| Height | int | Unique per network and branch. |
| Hash | 32 bytes | Unique. |
| PrevHash | 32 bytes | Must match the stored hash at Height-1 on the same branch. |
| Raw | 80 bytes | The header as received. |
| Algo | enum | Read from the version bits (mask `15 << 8`). Must be a known algorithm. |
| ChainWork | big integer | Cumulative work up to and including this header. |
| IsBestChain | bool | True for headers on the most-work chain. |

**Lifecycle**: received, then validated (linkage, proof of work, retarget), then stored. A header on a losing branch stays stored with `IsBestChain` false so a later reorganization can switch back cheaply. A reorganization flips `IsBestChain` for the affected range in one transaction and records the depth.

### PeerRecord (Peer set)

| Field | Type | Rule |
|---|---|---|
| Address, Port | endpoint | Unique together. |
| Services | bitfield | From the handshake. Used to pick full-history peers (`NETWORK`) versus limited ones. |
| UserAgent, ProtocolVersion | text, int | Informational. |
| LastSeen, LastGood | time | Drives ranking and pruning. |
| FailureCount | int | Backoff and ban threshold. |
| Misbehavior | int | Sent invalid headers or blocks raises it. Past a threshold the peer is dropped for a period. |

### BlockIndexRecord (self-built match index)

| Field | Type | Rule |
|---|---|---|
| Height, BlockHash | int, 32 bytes | Key. Entry is removed if the block leaves the best chain. |
| OutputScriptHashes | set of short hashes | For every output script in the block. |
| SpentOutpoints | set of short hashes | For every input's previous outpoint. |
| TxOffsets | list | Position of each transaction, so a hit can be fetched and proved. |
| IndexedAt | time | For retention. |

Holds nothing about any wallet. Entries older than the scan window (default 90 days, operator-adjustable) are dropped. Ranges older than the window are not served (research R2).

### ResultCacheEntry (short-lived)

An in-memory cache of recent match results, keyed by a hash of the request, expiring in seconds. Never persisted (FR-021, Clarification 3).

### HealthReport (Source health report)

| Field | Type |
|---|---|
| Status | starting, syncing, ok, unhealthy |
| Network | mainnet or testnet |
| HeaderHeight | int |
| LagBlocks | int, against the best height reported by connected peers |
| PeerCount, FullHistoryPeerCount, FilterPeerCount | int, FilterPeerCount is the number of connected peers advertising compact filters |
| MatchingMode | `blocks` now, `filters` later |
| IndexedFromHeight | int, the start of the indexed window |
| BuildVersion | text |

Contains no wallet or address data. It is the gateway's counterpart of the node health report from feature 005 and maps onto the same monitor.

## Wallet side

### GatewayHeaderStore

| Field | Type | Rule |
|---|---|---|
| Network | enum | Part of the key. |
| CheckpointHeight, CheckpointHash | int, 32 bytes | The built-in anchor the chain is verified from. |
| Headers | ordered 80-byte records | Only headers that passed the wallet's own checks. |
| TipHeight, TipChainWork | int, big integer | Updated when a longer valid chain arrives. |

**Rule**: a header is stored only after linkage, retarget and (from M2) proof-of-work checks pass. On a reorganization the wallet truncates to the fork point and re-verifies. The store is public data and is kept outside the encrypted seed store.

### ScanProgress (Restore progress)

| Field | Type | Rule |
|---|---|---|
| WalletId | local id | Never sent to the gateway. |
| StartHeight | int | From the birth height or the user's date estimate. |
| NextHeight | int | Resume point. Advances only after a chunk is fully processed. |
| ReceiveHighestUsed, ChangeHighestUsed | int | Gap-limit bookkeeping. |
| Mode | normal, deep | Uses the existing deep-scan gap setting. |
| Source | gateway, explorer | Records which path the scan is using. |

### WalletSyncState

The wallet's own record that makes a routine refresh incremental (spec FR-009a). One per wallet, on the device only, never sent to the gateway.

| Field | Type | Rule |
|---|---|---|
| WalletId | local id | Never sent to the gateway. |
| LastSyncedHeight, LastSyncedHash | int, 32 bytes | "advance only after every request of the refresh has succeeded". A refresh that fails part way leaves both unchanged. |
| Transactions | list | Each with height, block hash and **Origin**: `gateway-proven` or `explorer-sourced`. Raw bytes are required for `gateway-proven` entries and optional for `explorer-sourced` ones, until `POST /v1/proofs` returns the transaction. |
| UnspentOutpoints | list | Derived from Transactions for gateway-proven entries. For explorer-sourced entries, taken from the explorer's unspent outputs at the bootstrap snapshot. Sent as `knownOutpoints` so spends of outputs older than the gateway's scan window are found. |
| UpdatedAt | time | Informational. |

**Bootstrap**: a wallet with no state (a wallet that existed before this feature, or one whose first refresh came from the explorer path) gets its state from the result of an explorer-served refresh: the explorer's transactions and unspent outputs recorded as `explorer-sourced`, `LastSyncedHeight` set to the explorer's reported tip height, read before the snapshot, minus 6, and `LastSyncedHash` taken from the `GatewayHeaderStore`. If the header store can't supply that hash yet, nothing is recorded and the wallet tries again on the next refresh. Bootstrap never delays or changes the explorer-served result.

**Own sends**: when the wallet broadcasts a transaction, through any source, it records it in the state at once as unconfirmed, with its inputs marked spent and its change output added. It removes the entry if the broadcast is rejected and replaces it with the gateway-proven version once the transaction is in a block.

**Storage and deletion**: kept in the wallet's existing per-wallet persistent cache on the device, keyed by wallet id. Deleted when the wallet is deleted or reset and in every clear-data path. Never included in an export or backup. After a deletion it is rebuilt through the bootstrap rule.

**Freshness**: the state is re-recorded after every explorer-served refresh from the explorer data already fetched (merge: new transactions added as `explorer-sourced`, `gateway-proven` entries kept when they agree with the explorer and marked disputed when they do not, `LastSyncedHeight` moves forward only). A state whose `LastSyncedHeight` is older than the gateway's scan window is not used for gateway refreshes.

**Lifecycle**: created at the end of a restore or of the first refresh (for a wallet older than the scan window, from the explorer path, with every transaction `explorer-sourced`). Each refresh asks the gateway from `LastSyncedHeight - 6`. If the block hash recorded at `LastSyncedHeight` no longer matches the verified chain, the wallet rewinds to the fork point before asking. `explorer-sourced` transactions are upgraded to `gateway-proven` one by one through `POST /v1/proofs` and the header store, best effort.

### WalletBirthHeight

| Field | Type | Rule |
|---|---|---|
| Height | int | Recorded at wallet creation for wallets created after this feature ships (FR-008a). |
| Origin | recorded, estimated-from-date, unknown | Shown to the user on restore. |

### VerifiedTransactionView (derived, not stored separately)

For each transaction the wallet shows, it derives a state from proofs and headers:

| State | Meaning | Counts toward spendable balance? |
|---|---|---|
| Pending, unconfirmed | A transaction the wallet itself just sent and recorded, not yet in a block. Payments from others are not reported by the gateway until they are in a block. | No, except change from a transaction this wallet signed and sent. |
| Pending, n of 6 | In a verified block, fewer than 6 blocks deep. | No, same exception. |
| Verified | At least 6 blocks deep in the verified chain, proof checked. | Yes. |
| Rejected | Proof failed against headers. Never shown as fact. | No. |
| Unchecked (explorer-sourced) | Returned by the explorer path and not yet proven by the gateway. | As today for explorer data: counts once confirmed there. Shown as unchecked. |
| Disputed | The gateway's proof or chain contradicts what the explorer reported. | No, until resolved. Kept unchecked, with a local note. |

**Transitions**: pending moves to verified as blocks arrive. A reorganization can move a transaction from verified or pending back to unconfirmed or removed. A failed proof marks the source faulty and triggers fallback.

### GatewaySourceState

The existing `NodeSourceMonitor` state for the gateway: disabled, unavailable, stabilizing, active, not ready (with a reason). Plus the deployment settings: enabled, base URL, position (first or backup), height tolerance, stability period.

## Relationships

- A **ScanProgress** record references a wallet and reads headers from the **GatewayHeaderStore** to check every proof it receives.
- A **BlockIndexRecord** references a **HeaderRecord** by height and hash and is deleted when that header leaves the best chain.
- The gateway **HealthReport** is derived from the best-chain **HeaderRecord** tip, the **PeerRecord** set and the index window, and is the only gateway data the wallet monitor reads.
- A **WalletSyncState** is the input to every routine refresh and is written by the restore flow when a restore completes. Its `explorer-sourced` transactions become `gateway-proven` as the wallet checks proofs against the **GatewayHeaderStore**.
