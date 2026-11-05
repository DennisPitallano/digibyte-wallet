# Quickstart: Validate PSBT Signing

**Feature**: [spec.md](spec.md) | **Contracts**: [contracts/psbt-signing.md](contracts/psbt-signing.md)

This guide proves the feature works. It runs only once the feature is implemented.

## Prerequisites

- .NET 10 SDK
- For the release check: Docker, to run the regtest DigiByte Core node and `DigiByte.NodeApi` from `docker-compose.yml`

## 1. Automated tests

```bash
dotnet test tests/DigiByte.Crypto.Tests --filter "FullyQualifiedName~Psbt"
```

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Psbt|FullyQualifiedName~PinAttempt|FullyQualifiedName~VerifiedUtxo"
```

Multisig must be unchanged (SC-008):

```bash
dotnet test tests/DigiByte.Crypto.Tests --filter "FullyQualifiedName~Multisig"
```

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Multisig"
```

### Required bad-case set (SC-002)

Each case must produce the named refusal and no signature.

| Case | Expected refusal |
|---|---|
| Oversized text and oversized file | TooLarge |
| Random text; valid base64 that isn't a PSBT | NotAPsbt |
| Duplicate input; no outputs | InvalidStructure |
| DigiDollar version; DigiDollar data output under a plain version | UnsupportedDigiDollar |
| `NONE`, `SINGLE`, and `ANYONECANPAY` on a wallet input; `SINGLE` on another participant's input | UnsupportedSignatureType |
| Taproot input | UnsupportedTaproot |
| Legacy wallet input without its previous transaction | MissingPreviousTransaction |
| Previous transaction that doesn't match the input | PreviousTransactionMismatch |
| False amount or script on a wallet input | AmountOrScriptMismatch |
| From Story 4: false amount on another participant's input | AmountOrScriptMismatch |
| Spent wallet input; coin that exists only on the other network | InputSpentOrUnknown |
| Outputs greater than inputs | OutputsExceedInputs |

### Required safety tests

| Case | Expected |
|---|---|
| Multisig input containing the wallet's key (SC-004) | No signature on that input |
| Input of another key beside a wallet input | Only the wallet input signed; result incomplete |
| Wrapped SegWit input on the wallet's key | Treated as someone else's; not signed |
| PSBT key paths claiming an attacker's output is change | Shown as an outside payment |
| Coin spent between load and sign | Refused at signing |
| Chain sources all failing | No signature; status Unavailable |
| Wrong PIN | No signature; session and active wallet unchanged |
| Tampered existing signature | Not complete; broadcast not offered |
| Fully signed PSBT with no wallet inputs | Nothing to sign; broadcast not offered |
| Input with no data whose lookup fails | Refused as InputNotIdentified, with a retry |
| All outputs return to the wallet, with a fee above 10% of inputs | High fee acknowledgment required |
| Already signed PSBT over the spending limit | Blocked before the broadcast confirmation |
| A check in the confirmation flow fails | Later steps don't run, and nothing is signed or broadcast |
| Total leaving the wallet (SC-003) | Exactly matches the signed transaction, including the case where the wallet gains overall |

## 2. Manual check in the wallet

Start the wallet and switch to mainnet in Settings, using a throwaway wallet with no funds. On testnet, the dev build falls back to mock data that shows history for every address:

```bash
dotnet run --project src/DigiByte.Web
```

| Step | Expected |
|---|---|
| Simple mode: open Settings | No PSBT entry |
| Simple mode: open `/settings/psbt-sign` directly | Advanced mode explanation; no input controls |
| Advanced mode: open Settings | Entry near Recovery Tools |
| Paste a valid PSBT, then upload the same PSBT as a file | The same review within 5 seconds, showing outputs, change, fee, and total leaving |
| Pay an unfamiliar address with all inputs yours | The same warning as Send |
| Set a spending limit below the amount, then sign | Blocked with the limit message |
| Enter a wrong PIN three times | Delay starts, as on the unlock screen |
| Sign, then export by copy and by download | Both forms are the same PSBT; balance and history unchanged |
| Lock the wallet during review | PSBT discarded |
| Open with a watch-only wallet | Review works; Sign and broadcast are unavailable, with an explanation |
| On the unlock screen, enter a wrong PIN three times, wait out the delay, then enter the correct PIN | Same delay and message as before this feature; the wallet unlocks and the failure count resets |
| Disconnect the network, then load | Review labeled unverified; Sign is unavailable; retry works after reconnecting |

Check the browser console during these steps: no PSBT contents should appear (FR-028).

## 3. Release gate: DigiByte Core on regtest (SC-005)

This must pass before release. It can't be replaced by the automated tests.

**Round trip through Core**

1. Mine enough blocks to mature a coinbase, then fund a wallet address on regtest.
2. In DigiByte Core, create a PSBT spending that coin with `createpsbt`, then add its data with `utxoupdatepsbt`.
3. Load it in the wallet and sign it.
4. In Core, run `finalizepsbt` on the exported PSBT, then send the result with `sendrawtransaction`.

**Expected**: Core reports the PSBT complete, and the node accepts the transaction.

**Broadcast from the wallet**

1. Create a second PSBT the same way.
2. Sign and broadcast it in the wallet.

**Expected**: The node reports the transaction in its mempool or a mined block, and it appears in the wallet's history.

On regtest, address discovery can't run because the node refuses history checks, so the wallet stays on its saved address range. Use addresses within that range for this test.

Also run a `createpsbt` PSBT with no `utxoupdatepsbt` step, to prove Story 5's filling of missing data against Core's real output.
