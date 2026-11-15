# Quickstart: validating the oracle from digibyte.io

How to run, test and check the feature end to end once it is built. See [contracts/oracle-api.md](contracts/oracle-api.md) and [data-model.md](data-model.md) for the details referred to below.

## 1. Run the data service locally

```bash
dotnet run --project src/DigiByte.Api --urls http://localhost:5007
curl -s http://localhost:5007/api/oracle
```

Expected: a `200` with the fields in the contract and `"source": "digibyte.io"`. Call it twice within 30 seconds: the second answer has the same `fetchedAt` (one shared answer, one upstream request).

Switch it off: start with `Oracle__Enabled=false`; the route answers `404`.

## 2. Run the wallet against it

In the wallet's development settings (not committed), set `DigiDollar:OracleEnabled` to true, `DigiDollar:Backend` to `esplora`, and `DigiDollar:OracleApiUrl` to `http://localhost:5007/api/oracle`. Network must be mainnet. Open a DigiDollar screen.

Expected:

- The price (six decimals), the update block and its age, and the line "Price from digibyte.io. Not verified by the wallet."
- A loading placeholder for a moment on first load, never the "unavailable" message on a normal load.
- The browser's network panel shows requests to `localhost:5007/api/oracle` only, none to `digibyte.io`, and at most one per 30 seconds.

## 3. Failure drills (each must end in "unavailable" with every other screen working)

| Drill | How | Expect |
|---|---|---|
| Data service down | Stop the service | Everything on the screen loads at once; the oracle area shows loading on first load and then unavailable within 6 s; balance, history, receive and send work |
| Data service hung | A stub that never answers | Same as above: nothing else on the screen is held up |
| Explorer down, digibyte.io up | Block the explorer | The DigiDollar data is unavailable and the oracle area shows unavailable with it |
| digibyte.io down, recent answer | Block digibyte.io after a first success | The last answer keeps showing with its age until it is 5 minutes old, then unavailable |
| Unusable data | Point `Oracle__BaseUrl` at a stub returning price 0, a missing field, or status "inactive" | Unavailable |
| Slow upstream | Stub that waits 10 s | Route answers `503` within about 3.5 s; wallet shows unavailable |
| Switch off | `DigiDollar:OracleEnabled` false, or testnet, or the node backend | No oracle display and no request to the data service |
| Rate limit | Call the route 31 times in a minute | `429` from the 31st; the wallet's own polling never reaches it |

## 4. Protection states and notices

Use a stub for digibyte.io (the API tests have one) and serve, in turn: a stale price (`is_stale` true, then an old update block), `protection_active` true, `minting_restricted` true, `all_operations_restricted` true, and `is_emergency` true.

Expect the matching notice only, in its own state (SC-009). For "all operations restricted", open a send to the confirm step: the warning shows and the send can still be completed (SC-010).

## 5. Automated tests

```bash
dotnet test tests/DigiByte.Api.Tests --filter "FullyQualifiedName~Oracle"
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Oracle"
```

Opt-in live check against the real site (read-only):

```bash
ORACLE_LIVE=1 dotnet test tests/DigiByte.Api.Tests --filter "FullyQualifiedName~OracleLive"
```

## 6. Privacy check

With the wallet open on a DigiDollar screen and an address or transaction visible, inspect the request the wallet sends to the data service: no query string, no body, no custom headers. Inspect the service's outgoing requests (or the stub's log): only the two fixed GETs.

## 7. Deployment

1. Deploy `DigiByte.Api` with the route. Optional settings: `Oracle__Enabled` (default true), `Oracle__BaseUrl` (default `https://digibyte.io/api`).
2. Confirm on production: `curl -s https://digibyte-api-production.up.railway.app/api/oracle` returns the contract's shape.
3. Ship the wallet build (the oracle switch is off in the build default).
4. Set `WALLET_DD_ORACLE=true` on the web service and restart it. Open DigiDollar on mainnet: price and source note appear.
5. Rollback: set `WALLET_DD_ORACLE=false` (wallet side, no release) or `Oracle__Enabled=false` (service side).

## 8. Before switching the display on

- Check the meaning of the non-normal values (`burn_multiplier` during an emergency, the restriction flags) against DigiByte Core or its DigiDollar documentation (research.md R3, known gap).
- Amend spec 001 (FR-019, FR-021) so it no longer says the main balance's dollar value comes from the oracle (spec 014, FR-018).
- Update `docs/SECURITY.md` (data flow through the relay) and the Railway notes with the two service settings.
