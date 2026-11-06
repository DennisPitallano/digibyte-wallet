# Data Model

Mostly no schema change. The hotfix is behavioral.

## OutboundUrlPolicy (config, server-only)

| Field | Type | Default | Notes |
|---|---|---|---|
| `AllowLoopback` | bool | false | Development only |
| `AllowedPorts` | int[] | webhooks `[443, 8443, 8080, 8000]`; Digi-ID `[443]` | Per caller |
| `TimeoutSeconds` | int | 10 | |
| `MaxResponseBytes` | int | 65536 | |

## Digi-ID callback request (unchanged shape, new rules)

`{ uri, address, signature, callbackUrl? }`. `callbackUrl` is optional and, if present, must equal the URL derived from `uri`. Derivation: `digiid://host/path?x=nonce` becomes `https://host/path?x=nonce`.

## PayStore (Pay.Api, existing)

`WebhookUrl` and `WebhookSecret` are unchanged in shape. New rule: `WebhookUrl` must pass the guard at write time and again at dispatch.

## WebhookDelivery (existing)

`ResponseSnippet` keeps the first 512 characters of the reply as plain text for any status (was 2048). No schema change.

## Directory entry (existing, in-memory)

Read-only for now. No new fields.
