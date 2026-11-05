# Release check: PSBT signing

Results to copy into the pull request (T048, SC-005; T046, SC-009 and FR-028).

## Regtest release gate (quickstart section 3), 2026-11-05

DigiByte Core v9.26.7 on regtest (`docker-compose.yml`), wallet in the browser on regtest through the local node API.
The wallet's receive address `dgbrt1qdenmjpjwrxyyytrhljaekzf22tde9cmu24qvyq` was funded with three 10 DGB coins from Core.

| Run | Steps | Result | Transaction ID |
|---|---|---|---|
| Round trip through Core | `createpsbt` + `utxoupdatepsbt` → signed in the wallet → `finalizepsbt` → `sendrawtransaction` | Core: complete; node accepted it; mined | `69fe756ab696ae60ab1c0dd41924b10eccb92c39c0eeb314943cfac4e375fa3b` |
| Broadcast from the wallet | `createpsbt` + `utxoupdatepsbt` → signed and broadcast in the wallet | Node had it in the mempool, then mined; shown in wallet history as Sent 3 DGB; balance 22.998 DGB as expected | `a538e1388a48860d536628b7d7cb3df5fbb100b6294f2251b7f9ad5991500541` |
| Missing input data (Story 5) | `createpsbt` only (no coin data) → signed in the wallet → `finalizepsbt` → `sendrawtransaction` | Wallet filled `witness_utxo` (10 DGB) from the chain; Core: complete; mined | `c8ff2a1e713213a5cf964610398d675cbc94368cdad7218bfb7d857b6ffc4a69` |

## Manual checks done (quickstart section 2)

| Check | Result |
|---|---|
| Simple mode: no Settings entry; direct link shows only the Advanced mode explanation | Pass (mainnet) |
| Advanced mode: Settings entry near Recovery Tools | Pass |
| Review time (SC-009, under 5 s) | 0.14–0.2 s on regtest |
| All inputs the wallet's, new address | Same warning as Send |
| Wrong PIN | "Incorrect PIN. 2 attempts remaining before lockout.", nothing signed |
| Copy base64 | Copies the signed PSBT that Core then finalized |
| Broadcast confirmation | Lists every output and the verified fee |
| Browser console and server log (FR-028) | No PSBT contents found; no logging calls in PSBT code. The console buffer had dropped older messages, so this check is partial |
| Node API stopped, then load | Found a bug: in development builds the verified lookup fell back to demo data and called the coin spent. Fixed so it throws (tests added). Not re-checked in the browser, because the browser's wallet data was cleared during the re-check |

## Not yet done

- Manual: upload as a file, spending limit block, three wrong PINs and the delay, lock during review, watch-only wallet, unlock-screen regression, network-down check after the fix.
- T047 security self-review.
- T049 usability check with 10 test users (needs people).
