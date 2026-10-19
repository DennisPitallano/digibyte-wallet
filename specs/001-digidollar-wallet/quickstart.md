# Quickstart: Validating DigiDollar in the DGB Wallet

Runnable validation for [spec.md](spec.md). Details: [contracts/](contracts/) ·
[data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK, Docker
- A **testnet26** `digibyted` v9.26.5 with `digidollar=1`, `txindex=1`,
  `addnode=oracle1.digibyte.io:12033`, fully synced (`docker-compose.testnet.yml`, after
  upgrading the image tag)
- A second DD-capable wallet (DigiByte Core Qt or `digibyte-cli`) funded with testnet DGB and DD
  from the testnet faucet / a test mint, used as the "external sender"
- The BIP39 test mnemonic from `tests/DigiByte.Crypto.Tests` (never a real seed)

## 1. Automated checks

```powershell
dotnet build
dotnet test tests/DigiByte.Crypto.Tests --filter "FullyQualifiedName~DigiDollar"
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~DigiDollar"
dotnet test tests/DigiByte.NodeApi.Tests --filter "FullyQualifiedName~DigiDollar"
```

Expected: all pass, including the golden-vector test in `DigiDollarTransferBuilderTests`
(research R8).

## 2. Derivation interop check (one-time, gates Send)

On the testnet26 node:

```powershell
digibyte-cli -testnet createwallet "ddvector" true true "" false true
digibyte-cli -testnet -rpcwallet=ddvector importdescriptors '[{"desc":"tr(<tprv from test mnemonic>/86h/1h/0h/0/*)#<checksum>","timestamp":"now","active":true,"range":[0,5]}]'
digibyte-cli -testnet -rpcwallet=ddvector getdigidollaraddress
```

Expected: the first address equals the wallet's first DD receive address for the same mnemonic.

## 3. Run locally

```powershell
$env:DIGIDOLLAR_ENABLED="true"
dotnet run --project src/DigiByte.NodeApi      # wait for /api/digidollar/health → indexer.lagBlocks == 0
dotnet run --project src/DigiByte.Web          # set DigiDollar.ViewEnabled/SendEnabled = true in wwwroot/appsettings.Development.json
```

Switch the wallet to **testnet** and restore the test mnemonic.

## 4. Scenario walkthrough

| # | Action | Expected | Spec |
|---|---|---|---|
| 1 | Set `ViewEnabled=false` and reload | No DD card, nav item, or routes (`/digidollar` → redirect) | US1-2, FR-002 |
| 2 | Set `ViewEnabled=true` and reload | DD card on Home with `$0.00` | US1-1, FR-001 |
| 3 | Home → DigiDollar → Receive | `TD…` address + QR in ≤ 3 taps | US1-3, SC-001 |
| 4 | Send $5.00 DD to that address from the external wallet | Pending entry within ~15 s; confirmations climb; balance updates | US1-4, SC-002 |
| 5 | Generate a second address, reopen Receive | Both addresses listed with per-address balances | US1-5, FR-007 |
| 6 | Send → paste a DGB `dgbt1…` address | Inline "not a DigiDollar address"; Send disabled | US2-2 |
| 7 | Send → amount $0.50, then $100,000.01 | Min / max errors | FR-012 |
| 8 | Send $2.00 back to the external wallet | Confirm dialog shows the full address, `$2.00`, DGB fee, and custody line → Pending → Confirmed | US2-1/5, FR-015/017/026 |
| 9 | Empty the wallet's DGB, then try to send | "Add DGB to pay the network fee" | US2-4, FR-013 |
| 10 | Send 6 times within 60 s | 6th is blocked with "slow down" | US2-6, FR-016 |
| 11 | Stop NodeApi | DD sections show "temporarily unavailable"; DGB balance/send still work | FR-003, SC-008 |
| 12 | Stub `/health` with `oracle.height` 30 blocks old | Stale banner; Send still allowed with an extra warning in the confirm dialog | US3-2, FR-019/027 |
| 13 | Switch to regtest | No DD UI | FR-029 |
| 14 | Open a WIF-imported wallet | "DigiDollar requires a recovery-phrase wallet" | research R2 |
| 15 | Browse to `/digidollar/mint` | Not found | FR-028 |
| 16 | Restore the test mnemonic on a fresh browser profile that has received DD at `/0/0` and `/0/7` | `Scanning` state, then the balance and both addresses appear (gap 20) | FR-007 |
| 17 | Set `ViewEnabled=false` on a profile that has opened DD | Only "DigiDollar is temporarily unavailable" on Home; on a never-used profile, nothing | FR-002 |
| 18 | Submit a send, then drop it from the node (restart without mempool persistence) | Stays Pending 30 min, then Failed with a notification and funds released | FR-017 |
| 19 | Stop NodeApi with cached data present | Cached balance labeled "cached, as of …"; Send disabled; Receive still works | FR-003 |
| 20 | Inspect IndexedDB | DD address list and history are ciphertext; only `dd.used` is plaintext | FR-008 |
| 21 | Hold $10.00 DD, send $9.50 | Blocked inline: change would be under $1.00; "Send all" offered | FR-012 |
| 22 | Tap "Send all" on a $10.00 balance | Amount fills with $10.00; confirm dialog shows the fee; the built tx has no DD change output | FR-012 |
| 23 | Open the Send screen | Fee is read-only; no fee preset or custom input | FR-014 |
| 24 | Stub `/health` with `oracle: null` | DGB USD equivalent hidden; "Price unavailable" and banner; balance, receive, send still work; the send confirm dialog adds a "price unavailable" warning line | FR-019/021/027 |
| 25 | Stub `/health` with `deploymentActive: false` | "DigiDollar is not active on this network yet"; no receive/send; recovers on refresh once active | FR-003 |

## 5. Security spot checks

- DevTools → Network during a send: only `/addresses/*`, `/tx/{txid}/status`, `/health`, `/tx/broadcast`, and the anonymous `/api/metrics/event` are
  called, and no request body contains a mnemonic, xprv, or WIF.
- `curl https://<wallet-origin>/appsettings.json` → contains no `WriteKey`.
- A cross-origin `POST /api/digidollar/send` from the wallet origin is rejected by CORS or
  returns 401.

## 6. Rollout switches (FR-022)

Both switches live in the wallet's `wwwroot/appsettings.json` (`DigiDollar:ViewEnabled`, `DigiDollar:SendEnabled`) and ship **off**.
`appsettings.Development.json` turns both on for local runs.

| Stage | ViewEnabled | SendEnabled | What users see |
|-------|-------------|-------------|----------------|
| Ship dark | false | false | No DigiDollar tile; the existing DGB flows are unchanged. Deploy the NodeApi first. |
| Beta (view only) | true | false | Hold and Receive work; Send is hidden. Watch the anonymous counters (`/api/metrics/summary`, operator key). |
| GA | true | true | Hold, Receive and Send. |

Rollback is flipping the flag back and redeploying the wallet; keys and the encrypted store are untouched.

Deployment notes:
- Set `NODEAPI_WRITE_KEY` on the NodeApi (and the DigiPay API). Wallet-scoped DD routes now fail closed without it outside Development.
- Add the real NodeApi origin to the wallet's CSP `connect-src` in `wwwroot/index.html` (only `http://localhost:5260` is allowed for dev).
- Set `DigiDollar:WalletOrigins` on the NodeApi to the wallet origin so CORS and the metrics endpoint accept it.

### Backend choice

`DigiDollar:Backend` selects where the wallet reads chain data:

| Value | Needs | Gives | Trade-off |
|-------|-------|-------|-----------|
| `esplora` (production default) | nothing to deploy; reads `digiexplorer.info` (`EsploraMainnetUrl` / `EsploraTestnetUrl`) | balances, history, status, broadcast | no oracle price or status panel; addresses go to the explorer; no usage counters |
| `nodeapi` | the NodeApi service plus a DigiDollar-capable node | everything, including the oracle panel | more infrastructure |

The explorer path was validated against real mainnet transactions (a mint and the transfer that spent it): amounts, address mapping, history, and the on-chain Schnorr signature (SIGHASH_DEFAULT). Mints carry extra pushes after the amount (lock height, tier, a 32-byte commitment); only the first amount is the DigiDollar value.

## 7. Verification status (2026-10-19)

Verified with the mock NodeApi and the automated suites: scan, balance, receive, send end to end, cached, not-active and stale states.
Not verified: a real testnet26 node (T002 vectors, acceptance of a built transaction, sighash confirmation) and the screen-reader pass.
