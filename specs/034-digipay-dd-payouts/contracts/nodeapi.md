# Contract C4: NodeApi DigiDollar routes used by payouts

Base: `/api/digidollar`.
- "Wallet" routes need `X-NodeApi-Key` and act on `DIGIDOLLAR_WALLET` (`digipay-hot`).
- Every route returns 503 when `DigiDollar:Enabled` is false.

## Existing routes, used unchanged

| Route | Scope | Used for |
|---|---|---|
| `GET /health` | public | Whether the node is reachable, `deployment_active`, `dgb_fee_reserve_dgb`, `reserve_healthy` |
| `POST /address/new` | wallet | Session and donation addresses (unchanged) |
| `GET /balance` | wallet | The wallet's total DD, for the coverage check (`minConfirmations=0`; Pay.Api subtracts its own incoming credits with fewer than 6 confirmations, research R4) |
| `GET /transactions?category=receive&count&skip` | wallet | Ledger scan. Each item needs `txid`, `address`, `amount` (cents), `confirmations`, `time`. |
| `GET /tx/{txid}/status` | public | `sent` → `confirmed`, and spotting reversed credits |

## `POST /send` (changed response)

**Request** (unchanged):

```json
{ "address": "DD1…", "amountCents": 4500, "comment": "dpo_…" }
```

**Response** (changed): the RPC result is parsed and returned in a stable shape.

```json
200 { "txid": "64-hex" }
```

The handler accepts either a bare txid string or an object with `txid` from `senddigidollar`. If neither is present, it returns 502 `send_result_unparsed` with the raw result logged, and Pay.Api treats that as `checking`.

**Errors:**

| Status | Meaning | Pay.Api treats it as |
|---|---|---|
| 400 | RPC error with a message (e.g. invalid address, insufficient funds) | `failed` |
| 503 | RPC code -1, node down, or DD disabled | `checking` if the request may have reached the node, otherwise `waiting` |
| timeout | No answer | `checking` |

## `GET /sends?since={unix}&count={n≤200}` (new, wallet)

Lists recent DD sends from the wallet, newest first, through `listdigidollartxs` with category `send`.

```json
200 { "items": [ { "txid": "…", "address": "DD1…", "amountCents": 4500, "comment": "dpo_…|null", "time": 1764200000, "confirmations": 0 } ] }
```

**Rules:**
- Must be listed in `WalletScopedRoutes`. The route-classification test enforces this.
- `comment` is null when the node doesn't keep it. Pay.Api then falls back to matching (address, amount, time).

## Not changed

`/send-many`, `/mint`, `/redeem` and `/positions` aren't used by payouts.
