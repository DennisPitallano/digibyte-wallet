# Research: Oracle price and DigiDollar status from digibyte.io

Observations were made on 2026-11-15 and 2026-11-16 with plain read-only GET requests. Nothing here depends on a private agreement with digibyte.io.

## R1. How the wallet reaches the data: through the project's data service

- **Decision**: The wallet calls a new route on `DigiByte.Api`; the service calls digibyte.io (clarification Q1, option B).
- **Rationale**: Keeps every user's network address and browser details away from digibyte.io; one shared cache means digibyte.io sees one request per kind per 30 s; no change to the browser's content policy (the API origin is already allowed); the constitution allows servers to be public data relays (Principle II).
- **Alternatives rejected**: Direct from the browser (exposes users, needs a policy exception, one request per wallet). Direct with a fallback to the service (most code, still exposes users most of the time).
- **Risk accepted**: The service becomes a dependency of the oracle display only. If it is down the wallet shows "unavailable" and nothing else is affected (FR-010).

## R2. What digibyte.io serves, and what we use

digibyte.io publishes a fixed set of read-only node commands as `GET https://digibyte.io/api/<command>` with open cross-origin access, no key, and responses in about 0.7 to 1.2 s. There is no availability promise.

| Command | Used | Fields consumed |
|---|---|---|
| `getoracleprice` | Yes | `price_micro_usd`, `last_update_height`, `last_update_time`, `validity_blocks`, `is_stale`, `status`, `mint_volatility.protection_active`, `mint_volatility.minting_restricted`, `mint_volatility.all_operations_restricted` |
| `getdigidollarstats` | Yes | `is_emergency`, `err_tier.burn_multiplier` |
| `getdigidollardeploymentinfo` | No | (oracle roster size and consensus threshold are not needed for the display) |
| `getdeploymentinfo`, `getblockchaininfo` | No | chain height is already known from the explorer |

Ignored on purpose: `24h_high` and `24h_low` (they read as cents, not dollars), `volatility` numbers, collateral and supply figures (out of scope in the spec).

Sample, 2026-11-15 (normal state): price 2725 micro-USD ($0.002725), last update block 24,568,788, `validity_blocks` 20, `is_stale` false, `status` "active", `protection_active` false, `minting_restricted` false, `all_operations_restricted` false, `is_emergency` false, `err_tier.burn_multiplier` 1.

## R3. Field mapping onto the wallet's health model

| Wallet field | From | Rule |
|---|---|---|
| `OraclePriceMicroUsd` | `price_micro_usd` | Integer greater than zero, else unusable |
| `OracleHeight` | `last_update_height` | Integer greater than zero |
| `OracleUpdatedAt` (new) | `last_update_time` | Unix seconds |
| `OracleReportedStale` (new) | `is_stale` | Boolean; required |
| `VolatilityFreeze` | `mint_volatility.protection_active` | Boolean; required |
| `MintingRestricted` (new) | `mint_volatility.minting_restricted` | Boolean; required |
| `AllOperationsRestricted` (new) | `mint_volatility.all_operations_restricted` | Boolean; required |
| `ErrActive` | `is_emergency` OR `err_tier.burn_multiplier` greater than 1 | Either present and true/greater |

Anything required that is missing, of the wrong type, or `status` other than "active" makes the whole answer unusable (spec FR-008, clarification Q3).

**Known gap**: only the normal state has been observed. The meaning of the non-normal values (what `burn_multiplier` is during a real ERR event, what `rejection_reason` says) comes from the field names, not from a sample. Tasks must include checking these against the DigiByte Core source or the DigiDollar documentation before the display is switched on, and the tests use synthetic non-normal answers.

**Check result (T034, 2026-11-16, from the DigiByte Core release notes for v9.26.0-rc4, rc7 and rc35; the Core source was not read)**: the notes say `getdigidollarstats` includes the Emergency Reserve Ratio tier information, that during ERR the DD burn requirement goes up (it does not reduce the collateral returned), and that with no oracle price ERR is reported as "not evaluated" rather than active. The field name `burn_multiplier` is not documented in the notes. A multiplier above 1 is therefore read as "ERR active", which matches the mapping (`is_emergency` or `burn_multiplier` above 1). An absent oracle price gives no reading at all (503), so ERR is never shown as active when it was not evaluated. The meaning of `rejection_reason` is not used. Still open: confirm the exact field with `digibyte-cli help getdigidollarstats` or the source before switching the display on in production; the mapping and tests need no change on the evidence so far.

## R4. Why one combined route and a strict rule

- **Decision**: `GET /api/oracle` merges the two reads and answers 503 if either is unusable.
- **Rationale**: ERR state is part of what the notices show. Showing a price with the emergency state unknown could hide an emergency, so a missing protection read makes the oracle "unavailable" (consistent with FR-008). Price and status come from the same host, so their failures are correlated anyway.
- **Alternative rejected**: Two routes (price, status) with partial display. More states, more UI, and a window where a price shows without its warning.

## R5. Caching, ages and time limits

- **Service**: one shared answer per kind, valid for 30 s (single-flight, so concurrent wallets cause one upstream request). If digibyte.io fails, the last good answer is served until 5 minutes after it was fetched; after that the route answers 503. This age limit counts from the moment the service fetched the answer.
- **Wallet**: reuses an answer for 30 s, shared by all screens, at most one request per 30 s; never presents an answer older than 5 minutes (measured from the service's `fetchedAt`); a fetch is limited to 5 s and never holds up the health call (R6).
- **Freshness of the oracle itself** (FR-004) is separate: more than 20 blocks behind the chain height, or the source's own stale flag; if the height is missing from an otherwise available answer, the source's stale flag and an update time older than 5 minutes.
- **Upstream timeout**: 3 s per read, both reads in parallel, so the route answers in about 3.5 s in the worst case, inside the wallet's 5 s.
- **Retry spacing**: after any attempt to reach digibyte.io for a kind, successful or not, the service makes no new attempt for that kind within 30 s. During an outage it keeps serving the last good answer (until 5 minutes after it was fetched) and then answers 503, while still reaching digibyte.io at most twice a minute per kind. Without this rule, every wallet request during an outage would trigger an upstream attempt and break SC-012.

## R6. Where the merge happens in the wallet

- **Decision**: A wrapper (`OracleEnrichedDigiDollarApi`) around the explorer-backed `IDigiDollarNodeApi` merges the reading into `GetHealthAsync`; every other call passes straight through.
- **Rationale**: `EsploraDigiDollarApi` stays unchanged; the wrapper applies only on the explorer backend (FR-014) and only on mainnet when the oracle switch is on (FR-012, FR-013); all existing consumers of `NetworkHealth` (price line, panel, banner, confirm dialog, send check) work without changes to their logic. The merge never waits for the oracle: the wrapper awaits only the explorer call, merges the latest reading it already holds, and starts a background refresh; the oracle area shows a loading state until the first reading arrives and the screens re-read the health when it does (FR-010, SC-001). A blocking merge was rejected because a hung data service would hold the DigiDollar screens for up to 5 seconds.
- **Alternative rejected**: Editing `EsploraDigiDollarApi.GetHealthAsync` directly. It would mix an unrelated source into the explorer client.

## R7. Abuse, privacy and size limits on the route

- No query string or body is read; the upstream base address comes from configuration, so there is no way to make the service fetch anything else.
- Per-visitor rate limit using the data service's existing visitor-key logic (rightmost forwarded address), 30 requests per minute (a healthy wallet makes at most 2).
- Upstream responses are read with a size cap (64 KB; the two answers are under 4 KB) and parsed strictly; redirects are not followed.
- The service logs no visitor address for this route (spec 001, FR-023). Operators see health through the usual service monitoring.

## R8. User interface additions

- **Source note** under the price on every screen that shows it: "Price from digibyte.io. Not verified by the wallet." (en and es).
- **Loading placeholder** while the first answer is on its way (up to 5 s); not the "unavailable" message.
- **Notices**: the existing stale, volatility and ERR notices are reused. Two new conditions (minting restricted, all operations restricted) use the confirm-step warning list and the health banner; neither blocks a send (clarification Q2).
- **DigiDollar home card** keeps its oracle-based dollar value of the DGB balance (it is a DigiDollar element). The main balance card keeps the market price (clarification Q5, FR-018).

## R9. Alternatives considered for the data itself

- **Verifying the price from the signed bundle in each block's coinbase**: promising (the latest coinbase carries a 96-byte oracle record with a price and a signature), but the format and the oracle key list are unconfirmed. Out of scope; a possible follow-up spec.
- **Talking to the oracle servers directly** (port 12024): they are peer-to-peer nodes, not web services; not possible from a browser and adds nothing over the data service.
- **Own node API**: no longer exists.

## R10. Deployment and rollout

1. Deploy `DigiByte.Api` with the route (no wallet uses it yet). `Oracle__Enabled=false` turns it off without a release.
2. Ship the wallet build. `DigiDollar:OracleEnabled` is false in the build default, so no request is made.
3. Set `WALLET_DD_ORACLE=true` on the web service and restart it.
4. Watch the route's answers and the wallet's behaviour for a day before treating it as done.

The data service runs one instance today; with more instances, the upstream load multiplies by the instance count (SC-012 assumes one).
