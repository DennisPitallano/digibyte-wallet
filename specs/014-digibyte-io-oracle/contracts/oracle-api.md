# Contract: `GET /api/oracle` (data service)

The one route the wallet calls. Read-only, anonymous, no input.

## Request

`GET {data service origin}/api/oracle`

- No query string, no body, no cookies, no custom headers are read. Anything sent is ignored.
- Cross-origin access uses the service's existing `WasmClient` policy (the wallet's origin).
- Rate limit: 30 requests per minute per visitor; beyond that, `429` with `Retry-After: 60`.

## Success: `200 OK`

```json
{
  "priceMicroUsd": 2725,
  "lastUpdateHeight": 24568788,
  "lastUpdateTime": 1794763840,
  "reportedStale": false,
  "volatilityFreeze": false,
  "mintingRestricted": false,
  "allOperationsRestricted": false,
  "errActive": false,
  "fetchedAt": 1794763855,
  "source": "digibyte.io"
}
```

- All fields are always present on success; booleans are real booleans; the numbers are integers.
- `fetchedAt` is when the service obtained the data from digibyte.io, not when it answered. The wallet uses it for the 5-minute limit.
- `source` is a fixed label for the wallet's source note.
- Response header `Cache-Control: no-store` (the service does its own caching).

## Not available: `503 Service Unavailable`

```json
{ "reason": "unavailable" }
```

Returned when: digibyte.io cannot be reached or does not answer in time and no snapshot younger than 5 minutes exists; either upstream read is unusable (see the validity rules in data-model.md) and no usable snapshot younger than 5 minutes exists; the upstream answer is larger than the size cap. The reason is deliberately generic: no upstream detail is passed on.

## Switched off: `404 Not Found`

When `Oracle:Enabled` is false on the service. The wallet treats it like any failure.

## Too many requests: `429`

Per-visitor limit exceeded. The wallet treats it like any failure and does not retry before its next 30-second window.

## Behaviour guarantees

1. Concurrent calls cause at most one upstream attempt per kind of data per 30 seconds, whether the previous attempt succeeded or failed (an outage is not retried on every call).
2. A snapshot older than 5 minutes (from `fetchedAt`) is never served.
3. No request to digibyte.io carries anything from the caller (no address, header, cookie or query value).
4. The service logs no visitor address for this route.
5. The route answers within about 3.5 seconds in the worst case (3 s upstream limit, both reads in parallel).

## Versioning

Additive changes only (new fields). The wallet ignores unknown fields and treats a missing known field as unavailable.
