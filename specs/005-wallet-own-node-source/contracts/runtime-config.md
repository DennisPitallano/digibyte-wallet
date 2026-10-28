# Contract: Wallet Runtime Configuration

The wallet's web container writes `/config.json` when it starts. The app fetches it before building its services and merges it over `appsettings.json`.

## Deployment variables (web service)

| Variable | `config.json` key | Default if unset |
|---|---|---|
| `WALLET_NODE_ENABLED` | `OwnNode:Enabled` | `false` |
| `WALLET_NODE_API_URL` | `OwnNode:ApiUrl` | the existing `NodeApiUrl` |
| `WALLET_NODE_CHECK_SECONDS` | `OwnNode:CheckIntervalSeconds` | `30` |
| `WALLET_NODE_STABLE_SECONDS` | `OwnNode:StablePeriodSeconds` | `120` |
| `WALLET_NODE_HEIGHT_TOLERANCE` | `OwnNode:HeightTolerance` | `5` |
| `WALLET_DEV_VIEW` | `OwnNode:DevView` | `false` |

No secrets. Anything the browser reads from here is public.

## Rules

- A missing, unreadable or invalid `config.json` means the feature is **off** and the app starts normally.
- `NodeApiUrl` itself is **not** written or changed by this file: the DigiDollar client and the usage counter keep using it. The health check and the node blockchain client use `OwnNode:ApiUrl` when set, otherwise `NodeApiUrl`.
- Invalid or out-of-range numbers fall back to that field's default (see the data model).
- The file must be served with no-cache headers (an exact-match rule in `nginx.conf` that beats the generic `*.json` caching rule), and must not be in the service worker's precached asset list, so a changed variable takes effect on the next page load.
- `OwnNode:ApiUrl` must be https (plain http is accepted only for a local test node on localhost) so chain data cannot be altered on the way, and must be an origin allowed by the page's CSP `connect-src`. Today that is one exact origin (the node API's stable custom domain) added to `index.html`. Pointing to a different origin needs a CSP change first, otherwise the browser blocks every call and the wallet behaves as "node unavailable".

## Example

```json
{
  "OwnNode": {
    "Enabled": true,
    "ApiUrl": "https://node.example.org",
    "CheckIntervalSeconds": 30,
    "StablePeriodSeconds": 120,
    "HeightTolerance": 5,
    "DevView": false
  }
}
```

## Turning it off

Set `WALLET_NODE_ENABLED=false` (or remove it) and restart the web service. The wallet returns to today's behaviour on the next page load and never contacts the node's health endpoint.
