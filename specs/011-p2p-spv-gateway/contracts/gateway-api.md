# Contract: SPV Gateway HTTP API (v1)

Base path `/v1`. All bodies are JSON unless stated. All endpoints are anonymous and rate-limited (Clarification 4). Anything that carries addresses uses `POST`, so addresses never appear in URLs, access logs or caches. Heights are integers, hashes and transaction ids are lowercase hex, amounts are in satoshis.

Every response carries `X-Network` (`mainnet` or `testnet`). A request whose addresses belong to a different network than the gateway's is refused with `400 wrong_network`.

## Error shape

```json
{ "error": "over_limit", "message": "Too many addresses in one request (max 100).", "retryAfterSeconds": 5 }
```

Codes: `bad_request`, `wrong_network`, `over_limit`, `rate_limited` (`429`), `range_unavailable`, `not_ready` (`503`), `not_found` (`404`), `internal`.

## GET /v1/health

The source health report. Never contains wallet or address data.

```json
{
  "status": "ok",
  "network": "mainnet",
  "headerHeight": 24563023,
  "lagBlocks": 0,
  "peerCount": 8,
  "fullHistoryPeerCount": 5,
  "filterPeerCount": 0,
  "matchingMode": "blocks",
  "indexedFromHeight": 24390000,
  "buildVersion": "1.0.0"
}
```

`indexedFromHeight` is null until the block index exists. `status` is `starting` (no peer has connected yet), `unhealthy` (fewer connected peers than the configured minimum, 4 by default), `syncing` (more than 5 blocks behind the best peer) or `ok`. Answers `200` for every state it can report on, like the node health endpoint in feature 005.

## GET /v1/headers?from={height}&count={n}

Binary `application/octet-stream`: `n` consecutive 80-byte headers starting at `from`, on the best chain. `count` is at most 2000. Headers: `X-Tip-Height`, `X-Tip-Hash`. The wallet checks every one itself and never relies on this endpoint's say-so (Clarification 1). Refuses with `range_unavailable` for heights below the gateway's earliest stored header.

## POST /v1/scan

Find everything involving a set of addresses in one height range. The workhorse for balance refresh and restore. The gateway keeps nothing after answering.

Request:

```json
{
  "addresses": ["dgb1q...", "D..."],
  "fromHeight": 24500000,
  "toHeight": 24520000,
  "knownOutpoints": ["ab12...:0", "cd34...:1"]
}
```

Limits: at most 100 addresses, at most 100 `knownOutpoints`, and a range of at most a configured number of blocks (proposal: 50,000). A wallet with more addresses or outpoints than that splits them into several requests over the same range and combines the answers. Over-limit requests get `over_limit`. A range that starts before the scan window (`indexedFromHeight` in `/v1/health`, default 90 days back, operator-adjustable) gets `range_unavailable`, which tells the wallet to use its explorer path for that range. The gateway never returns a partial result for such a range as if it were complete.

Response:

```json
{
  "tipHeight": 24563023,
  "scannedFrom": 24500000,
  "scannedTo": 24520000,
  "complete": true,
  "transactions": [
    {
      "txid": "ab12...",
      "height": 24510001,
      "blockHash": "00ff...",
      "raw": "0100000001...",
      "proof": { "index": 3, "branch": ["11aa...", "22bb..."] }
    }
  ]
}
```

`raw` is the full serialized transaction, so the wallet derives outputs, amounts and spent inputs itself instead of trusting summary fields. `proof` is the merkle branch tying the transaction to the header at `height`. `complete` is false if the gateway stopped early (a time or size budget), and the wallet continues from `scannedTo + 1`. Only transactions that are in blocks are returned: the gateway does not match unconfirmed transactions to addresses in the first release, and the wallet records its own unconfirmed sends itself.

## POST /v1/utxos

For a set of addresses, the currently spendable outputs, with the proof data to check them. Intended for a wallet that has no sync state yet and whose history lies inside the scan window. A routine refresh uses `/v1/scan` from the wallet's last-refreshed height minus 6 and derives its outputs from its own stored transactions. Same limits and address rules as `/v1/scan`. Implemented as a scan over the window the wallet needs plus a spent-output check, so an output spent in a later block is never returned.

Request: `{ "addresses": [...], "fromHeight": 24500000 }`

Response:

```json
{
  "tipHeight": 24563023,
  "utxos": [
    {
      "txid": "ab12...",
      "vout": 0,
      "amount": 150000000,
      "scriptPubKey": "0014...",
      "height": 24510001,
      "blockHash": "00ff...",
      "proof": { "index": 3, "branch": ["11aa..."] }
    }
  ]
}
```

Unconfirmed outputs are not returned here. The wallet tracks its own unconfirmed sends locally (spend rules in the spec: own change is spendable at once).

## GET /v1/tx/{txid}

One transaction by id, with proof if it is in a block. `404` if unknown. Used for detail views and for confirming a sent transaction.

```json
{ "txid": "ab12...", "raw": "0100...", "height": 24510001, "blockHash": "00ff...", "proof": { "index": 3, "branch": ["11aa..."] } }
```

An unconfirmed transaction the gateway has seen in a peer's memory pool returns `height: null` and no proof.

## POST /v1/broadcast

Relay a signed transaction to at least 4 peers (fewer only when fewer are connected).

Request: `{ "raw": "0100000001..." }`

Response:

```json
{
  "txid": "ab12...",
  "relayedTo": 6,
  "rejections": [],
  "seenBackFrom": 2,
  "outcome": "accepted"
}
```

`relayedTo` is at least 4 unless fewer peers are connected, and then it says how many. `outcome` is `accepted`, `rejected` (with `rejections` giving each peer's reason) or `no_response`. Submitting the same transaction again returns the same `txid` and does not count as a new payment. The wallet treats `accepted` as "sent", `rejected` as a failure with the reason, and `no_response` as "sent, not yet confirmed by peers" and keeps watching.

## POST /v1/proofs

Prove specific transactions the wallet already knows about, so explorer-sourced transactions can be upgraded to "checked" without a history scan.

Request: `{ "items": [ { "txid": "ab12...", "height": 24100000 } ] }`, at most 20 items.

Response:

```json
{
  "proofs": [
    { "txid": "ab12...", "height": 24100000, "blockHash": "00ff...", "raw": "0100...", "proof": { "index": 3, "branch": ["11aa..."] } }
  ],
  "unavailable": [ { "txid": "cd34...", "reason": "block_not_served" } ]
}
```

Works for any age that a peer with full history can still serve, by fetching that single block. Items it can't prove come back in `unavailable`, and the wallet leaves those as "unchecked". The same per-client rate limits apply. The gateway stores and logs nothing about the request.

## GET /v1/fee

Advisory fee estimate (spec assumption). `{ "satoshisPerVbyte": 12.5, "basis": "recent-blocks" }`. The wallet's existing fee source stays authoritative until this proves reliable.

## What the gateway never does

- It never accepts keys, seed words, extended public keys or signatures-in-progress. Its only write path is `/v1/broadcast` with an already signed transaction.
- It never logs full addresses or transaction contents, and never stores address sets after a request completes (FR-021).
- It never reports a transaction as verified. Verification is the wallet's job, using `proof` and the headers it checked itself.
