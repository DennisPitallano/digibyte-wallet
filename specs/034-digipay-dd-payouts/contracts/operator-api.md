# Contract C3: Operator payout controls (Pay.Api)

These endpoints are for the DigiPay operator, not merchants. They are never linked from the Dashboard.

**Auth**: header `X-DigiPay-Operator-Key`, compared in constant time against the `DIGIPAY_OPERATOR_KEY` environment variable.
- When the key isn't configured, the endpoints return 503 `operator_key_not_configured` (fail closed).
- A wrong or missing key returns 401.
- The endpoints are rate-limited with the `auth` policy (10 per minute per IP).
- Merchant `dgp_` and `dps_` tokens are never accepted.

## `POST /v1/pay/operator/payouts/pause`

```json
{ "storeId": "sto_…|null", "reason": "incident text (≤ 256)" }
```

- `storeId` null pauses everything: it sets `OperatorSetting payouts.paused_at`. Otherwise it sets `PayStore.DdPayoutsPausedByOperatorAt`.
- Returns 200 with `{ pausedAt }`.
- Writes the audit entry `payouts.paused` (actor `System`, `metadata.reason`).

## `POST /v1/pay/operator/payouts/resume`

```json
{ "storeId": "sto_…|null" }
```

Returns 200 and writes the audit entry `payouts.resumed`.

## `GET /v1/pay/operator/payouts/health`

```json
{
  "paused": false,
  "node": { "reachable": true, "ddEnabled": true, "feeReserveDgb": 512.3, "reserveHealthy": true },
  "walletAvailableCents": 1203400,
  "liabilityCents": 1199900,
  "coverageOk": true,
  "checkingCount": 0,
  "waitingCount": 2,
  "lastScanAt": "…",
  "openAlerts": ["…"]
}
```

`liabilityCents` = the sum of every owner's confirmed held DD plus the amounts of all `sending` and `checking` payouts. `walletAvailableCents` = the wallet's DD at 0 confirmations − DD from incoming payments with fewer than 6 confirmations. `coverageOk` = `walletAvailableCents` ≥ `liabilityCents` (research R4).

The env var `DIGIPAY_PAYOUTS_PAUSED=true` is a global pause that needs no API call. It is meant for a deploy-time emergency.

## Alerts

Each alert is an `ILogger` entry with `EventId` name `DdPayoutAlert` and a structured `alert` property. Railway alert rules match on these.

| `alert` | Level | When |
|---|---|---|
| `fee_reserve_low` | Error | The node `/health` reports `reserve_healthy: false` while payouts are due |
| `coverage_short` | Critical | `walletAvailableCents` is below the liability, or an insufficient-funds rejection has lasted more than 10 minutes |
| `node_unavailable_6h` | Error | The node has been unreachable for more than 6 hours with payouts due (FR-037) |
| `reversal_absorbed` | Critical | A credit already paid out was reversed (FR-023) |
| `unattributed_receive` | Warning | DD arrived at a wallet address that isn't in `DdAddresses` |
| `store_auto_paused` | Warning | A store had 3 failed payouts in a row |
| `checking_stuck` | Error | A payout has been `checking` for more than 1 hour |
