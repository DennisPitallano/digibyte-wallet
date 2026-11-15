# Data Model: Oracle price and DigiDollar status from digibyte.io

Nothing is stored. Every entity below lives in memory and is replaced by the next answer.

## OracleSnapshot (data service, in memory)

One shared record per fetch, held by the service for at most 5 minutes after it was fetched.

| Field | Type | Meaning |
|---|---|---|
| `priceMicroUsd` | integer greater than 0 | USD per DGB times 10^6 |
| `lastUpdateHeight` | integer greater than 0 | Block the oracle last updated at |
| `lastUpdateTime` | Unix seconds | When the oracle last updated |
| `reportedStale` | boolean | The source's own stale flag |
| `volatilityFreeze` | boolean | A volatility protection is active |
| `mintingRestricted` | boolean | Minting is restricted |
| `allOperationsRestricted` | boolean | Every DigiDollar operation is restricted |
| `errActive` | boolean | Emergency state is active (emergency flag set, or burn multiplier above 1) |
| `fetchedAt` | Unix seconds | When the service obtained it from digibyte.io |

**Validity** (all must hold, otherwise the fetch is a failure and the previous snapshot, if any, keeps serving until it is 5 minutes old): price and height are integers greater than zero; `status` is "active"; every boolean is present and boolean; `lastUpdateTime` is a plausible time (not more than 10 minutes in the future).

**Lifecycle**: empty, then fresh (0 to 30 s old: served without a new fetch), then serving-old (30 s to 5 min old: served while a refresh is attempted or failing, with at most one attempt per kind every 30 s), then expired (older than 5 min: not served; the route answers 503).

## OracleReading (wallet, in memory)

The same fields as the snapshot, plus when the wallet received it (`receivedAt`). Created only from a route answer that passes the wallet's own checks (the shape checks of the relay's contract, repeated on purpose because the wallet does not trust the relay's shape: whole numbers greater than zero, every flag present and true or false, plausible times; the "active" status check belongs to the relay). Reused for 30 s; never presented once `fetchedAt` is more than 5 minutes old.

## Oracle feed (wallet, in memory)

What the wallet's oracle client offers to the rest of the wallet, so nothing has to wait for the network:

| Member | Meaning |
|---|---|
| `Current` | The latest valid reading whose `fetchedAt` is at most 5 minutes old, or none |
| `IsLoading` | True only while the first fetch is in flight and there is no current reading |
| `RequestRefresh()` | Starts a background fetch (limited to 5 s) only when no fetch is in flight, at least 30 s have passed since the previous attempt started (whether it succeeded or failed), and the current answer, if any, is more than 30 s old; never throws, never blocks |
| `Changed` | Raised when a fetch completes, successfully or not (so screens can re-read the health at once; the 30 s spacing above stops that re-read from starting another fetch) |

## NetworkHealth (wallet, existing record, extended)

Existing fields keep their meaning. Additions:

| Field | Type | Meaning |
|---|---|---|
| `OracleUpdatedAt` | DateTimeOffset? | From `lastUpdateTime` |
| `OracleReportedStale` | bool | From `reportedStale` |
| `MintingRestricted` | bool | From the reading |
| `AllOperationsRestricted` | bool | From the reading |
| `OracleLoading` | bool | From the oracle feed: no current reading and the first fetch in flight |

The reading is merged only when the explorer's health answer is available; an unavailable health stays unavailable and shows no oracle. Existing fields filled from the reading: `OraclePriceMicroUsd`, `OracleHeight`, `ErrActive`, `VolatilityFreeze`.

**Stale rule** (`IsOracleStale`), in order:

1. `OracleReportedStale` is true: stale.
2. The chain height is known (`NodeHeight` greater than 0): stale when `NodeHeight - OracleHeight` is more than 20.
3. The chain height is missing from an otherwise available answer (`NodeHeight` is 0): stale when `OracleUpdatedAt` is more than 5 minutes before now.

**Degraded conditions** (`DegradedConditions`): existing `Err`, `VolatilityFreeze`, `OracleStale`, `PriceUnavailable`, plus two new ones, `MintingRestricted` and `AllOperationsRestricted`. They produce notices and the confirm-step warning. None blocks a send.

## Display states of the oracle area

| State | When | Shows |
|---|---|---|
| Off | Oracle switch off, test network, or node backend | Nothing (as today) |
| Loading | `OracleLoading`: no current reading and the first fetch in flight (up to 5 s) | Neutral placeholder |
| Available | Reading valid and not stale | Price, update block, age in blocks, source note |
| Stale | Stale rule true | Price with the stale notice |
| Protected | Volatility, minting-restricted or all-operations-restricted | Price with the matching notice |
| Emergency | ERR active | Price with the emergency notice |
| Unavailable | Anything that makes the data unobtainable or unusable | The existing "unavailable" message |

States can combine (for example Stale and Emergency); each notice follows its own condition (SC-009).

## Mapping from digibyte.io

See research.md R3 for the field-by-field mapping and contracts/digibyte-io-upstream.md for the consumed shapes.
