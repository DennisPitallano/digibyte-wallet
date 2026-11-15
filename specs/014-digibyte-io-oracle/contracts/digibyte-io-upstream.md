# Contract: what the data service consumes from digibyte.io

Two read-only requests, made by the data service only (never by the wallet). Fixed addresses from configuration (`Oracle:BaseUrl`, default `https://digibyte.io/api`); no caller input reaches them.

## `GET {BaseUrl}/getoracleprice`

Fields read (others are ignored):

```json
{
  "price_micro_usd": 2725,
  "last_update_height": 24568788,
  "last_update_time": 1794763840,
  "is_stale": false,
  "status": "active",
  "mint_volatility": {
    "protection_active": false,
    "minting_restricted": false,
    "all_operations_restricted": false
  }
}
```

Required, with the types shown: every field above. `status` must equal "active" (case-sensitive). `price_micro_usd` and `last_update_height` must be integers greater than zero.

## `GET {BaseUrl}/getdigidollarstats`

Fields read:

```json
{
  "is_emergency": false,
  "err_tier": { "burn_multiplier": 1 }
}
```

`is_emergency` must be a boolean. `err_tier.burn_multiplier` must be a number. The emergency state is active when `is_emergency` is true or `burn_multiplier` is greater than 1.

## Request rules

- `GET` only, no body, no cookies, no authorisation, fixed `User-Agent: DigiByte-Wallet/1.0`.
- 3-second timeout per request; both made in parallel.
- Redirects are not followed. Response size cap 64 KB.
- Any status other than 200, any invalid JSON, any missing or mistyped required field, or an oracle `status` other than "active" makes the fetch a failure.

## Assumptions about the other side

- No availability promise, no published rate limit, no key. The service asks at most once per kind every 30 seconds, so load stays far below any reasonable limit.
- The format may change. A change makes the fetch fail validation, which shows as "unavailable" and never as wrong numbers.
- Only the normal state has been observed (research.md R3, known gap). Non-normal values are covered by synthetic tests until they are checked against DigiByte Core or its documentation.
