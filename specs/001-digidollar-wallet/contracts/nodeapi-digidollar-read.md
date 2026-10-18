# Contract: NodeApi DigiDollar read endpoints (wallet-facing)

Base path: `/api/digidollar` on `DigiByte.NodeApi`. These endpoints are **read-only and
unauthenticated** (public chain data), rate-limited per IP, and CORS-allowed for the wallet
origin. When `DigiDollar:Enabled=false` in NodeApi, every endpoint returns `503
{"error":"digidollar_disabled"}`.

The wallet MUST NOT call the wallet-scoped endpoints (`/address/new`, `/balance`, `/send`,
`/send-many`, `/mint`, `/redeem`, `/positions`). Those serve DigiPay only, behind
`X-NodeApi-Key`, and CORS MUST reject them for the wallet origin (research R7).

Amounts: `amountCents` is USD cents (`long`), `valueSats` is DGB satoshis (`long`).

---

## GET `/health`

Existing endpoint. The response is extended to include:

```json
{
  "enabled": true,
  "deploymentActive": true,
  "nodeHeight": 23901234,
  "networkHeight": 23901235,
  "oracle": { "priceMicroUsd": 6310, "height": 23901230 },
  "protection": { "errActive": false, "volatilityFreeze": false, "systemCollateralPercent": 187 },
  "indexer": { "height": 23901234, "lagBlocks": 0 }
}
```

`oracle` is `null` when there is no usable price (wallet shows "Price unavailable", research R16).
`deploymentActive: false` puts the wallet into its `NotActive` state.

Errors: `503` when disabled.

**Privacy rule**: DigiDollar addresses are accepted only in POST bodies. There is no single-address
GET route, because an address in a URL path would be written to standard access logs next to the
client IP. Request logging for this group must not record bodies.

## POST `/addresses/utxos`

Unspent DD outputs for a set of addresses, fetched in one call (reduces round trips on mobile).
The wallet uses this for a single address too, by sending a one-element list.

Request: `{ "addresses": ["DD…", "DD…"], "minConf": 0 }`. At most 200 addresses; duplicates are ignored.
Response: `{ "DD…": [ …utxo… ], … }`, with an entry (possibly an empty array) for every requested address.

```json
{ "txid": "…", "vout": 0, "amountCents": 2500, "scriptPubKey": "5120…", "valueSats": 0,
  "confirmations": 3, "spentByMempool": false }
```

`confirmations` is 0 for an unconfirmed (mempool) output, which is listed only when `minConf` is 0.
`spentByMempool` is true for a confirmed output that an unconfirmed transaction already spends: the wallet
excludes it from spendable funds and counts it as pending outgoing. Only outputs with at least 1
confirmation can be spent (consensus rejects unconfirmed DD inputs).

Errors: `400 {"error":"invalid_address","detail":[…bad addresses…]}` if any address is invalid or belongs to
the other network, `400 {"error":"invalid_request"}` for an empty list, `413 {"error":"too_many_addresses"}`
above 200, and `503 {"error":"indexer_behind","detail":{"lagBlocks":n}}` when the index is more than 6 blocks
behind the node tip or has not finished its first pass.

## POST `/addresses/history`

Request: `{ "addresses": ["DD…"], "count": 50, "before": "<txid cursor>|null" }`

Also used by the restore scan (FR-007, research R12): the wallet sends windows of 20 derived
addresses per chain (40 per request, well under the 200 limit) and treats any returned item as
activity for the address that appears in `ddInputs`/`ddOutputs`.

```json
{
  "items": [
    {
      "txid": "…", "blockHeight": 23901200, "blockTime": "2026-09-27T03:10:00Z",
      "confirmations": 35, "feeSats": 10000000,
      "ddInputs":  [ { "address": "DD…", "amountCents": 5000 } ],
      "ddOutputs": [ { "address": "DD…", "amountCents": 2500 }, { "address": "DD…", "amountCents": 2500 } ]
    }
  ],
  "next": "<txid cursor>|null"
}
```

The wallet computes Direction, net amount, and counterparty locally (data-model.md), so the
server never learns which of the addresses are "mine".

## GET `/tx/{txid}/status`

Used by the wallet to track pending sends (FR-017, research R13).

```json
{ "status": "mempool" | "confirmed" | "unknown", "confirmations": 0 }
```

`unknown` means absent from both the node mempool and the chain index. Errors: `400
{"error":"invalid_txid"}` and `503` when disabled. This returns public chain data only.

## POST `/api/tx/broadcast`

Reuses the existing raw-tx broadcast (`sendrawtransaction`) outside the `/api/digidollar` group; the wallet
already calls it for DGB.

Request: `{ "hex": "…" }` · Response: `{ "txid": "…" }`

Errors: the existing global RPC handler answers `400 {"error":"<node reject message>","code":n}` (or `503` when
the node is unreachable). The wallet maps known DD reject reasons (for example `transfer-dd-conservation-violation`,
`dd-input-amounts-unknown`, `bad-dd-script`) to user-facing messages.

---

## POST `/api/metrics/event` and GET `/api/metrics/summary`

Anonymous aggregate counters for SC-005 (research R17). These live under `/api/metrics`, not
`/api/digidollar`, and are not wallet-scoped.

`POST /api/metrics/event`: empty body; the event name is a path or query value from the allowlist
`dd_section_view`, `home_view`, `dd_send_attempted`. Returns `204`. `400` for any other name or any
request body. The server stores a per-day, per-event count only and logs no IP address, user agent,
cookie, or identifier.

Abuse controls: the POST is rate-limited per client IP with the IP held in memory only (never stored or
logged), for example 60 events per minute, then `429` with `Retry-After`; CORS allows only the wallet
origin. `GET /api/metrics/summary` requires `X-NodeApi-Key` (operator only) and fails closed when no key is
configured.

`GET /api/metrics/summary`: `{ "days": [ { "date": "2026-10-19", "home_view": 120, "dd_section_view": 41, "dd_send_attempted": 3 } ] }`

---

## Error envelope

`{ "error": "<snake_case_code>", "reason": "<optional detail>" }`, and no stack traces. Rate
limit: `429` with a `Retry-After` header.
