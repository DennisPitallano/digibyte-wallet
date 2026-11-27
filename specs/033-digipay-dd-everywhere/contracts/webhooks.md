# Contract C3: Webhook changes

Signing, retries and the envelope `{ event, timestamp, session }` are unchanged.

## Session payload (all `session.*` events)

New fields on `session`:

| Field | Type | Notes |
|---|---|---|
| `buyerChoice` | bool | |
| `lockedAt` | string? | |
| `extra` | object? | `{ currency, amount, txid, seenAt }`. `amount` is in satoshis for DGB and cents for DD. |

`currency`, `amountSatoshis`, `amountCentsDd` and `receivedSatoshis` describe the currency **actually paid** once the session has locked (FR-027). Every status event after funds are seen is sent after the lock, so a receiver that reads `currency` always sees the paid currency.

## New event: `session.extra_payment`

Sent once per session, when funds are first found on the option the session didn't lock to. The payload is the standard session payload with `extra` set. Later top-ups to the same address update `extra.amount` without sending another event.

Merchants decide how to refund:
- extra DGB arrives at the merchant's own address
- extra DD is paid out to them by spec 034, labelled "extra payment, refund the buyer"
