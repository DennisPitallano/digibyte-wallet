# Contract C5: Payout and payout-address webhooks

These webhooks are delivered to the store's `WebhookUrl`. They use the same signing and headers as the session webhooks:
- `X-DigiPay-Event`
- `X-DigiPay-Delivery`
- `X-DigiPay-Signature: sha256=<hex HMAC-SHA256(store.WebhookSecret, rawBody)>`

They are retried on the same schedule (1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours, then dead-lettered). Each body is saved in `WebhookDelivery.PayloadJson` and replayed byte for byte.

The envelope matches session webhooks: `event` and `timestamp`, then one object named after the subject. JSON is camelCase with nulls left out.

## Payout events

`payout.sent`, `payout.confirmed`, `payout.failed`

```json
{
  "event": "payout.sent",
  "timestamp": "2026-11-27T12:00:00Z",
  "payout": {
    "id": "dpo_…",
    "merchantId": "mer_…",
    "storeId": "sto_…",
    "currency": "DD",
    "amountCents": 4500,
    "destination": "DD1…",
    "status": "sent",
    "txid": "…",
    "confirmations": 0,
    "trigger": "auto",
    "items": [ { "sessionId": "ses_…", "source": "payment", "amountCents": 4000 },
               { "sessionId": "ses_…", "source": "extra_refund", "amountCents": 500 } ],
    "failureCode": null,
    "failureReason": null,
    "willRetry": null,
    "createdAt": "…", "sentAt": "…", "confirmedAt": null, "failedAt": null
  }
}
```

`payout.failed` sets `failureCode`, `failureReason` and `willRetry`, and leaves out `txid`.

No webhooks are sent for `waiting` or `checking`, which are internal states. They show in the Dashboard and API. No `payout.failed` is sent for a payout closed as `destination_superseded` either: that is DigiPay re-planning the payout to the current address, and a new payout follows.

## Payout-address events

`payout_address.change_requested`, `payout_address.change_cancelled`, `payout_address.changed`

```json
{
  "event": "payout_address.change_requested",
  "timestamp": "…",
  "payoutAddress": {
    "changeId": "pac_…",
    "storeId": "sto_…",
    "merchantId": "mer_…",
    "oldAddress": "DD1…|null",
    "newAddress": "DD1…",
    "requestedAt": "…",
    "effectiveAt": "…",
    "status": "pending|cancelled|applied"
  }
}
```

A first address that takes effect at once sends `change_requested` and `changed` back to back.

## Compatibility

These are new event names, and existing `session.*` payloads don't change apart from the extra fields in C2. Receivers that switch on `event` and ignore unknown values aren't affected. The SDK READMEs (`sdk/*`) and samples list the new events.
