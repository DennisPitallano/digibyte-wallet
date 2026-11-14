# Quickstart: Validating the P2P Light-Client Gateway

Run these checks to prove the feature end to end. Each scenario names the spec items it covers. Contract details are in [contracts/gateway-api.md](contracts/gateway-api.md) and entity rules in [data-model.md](data-model.md). Commands are placeholders until the projects exist (tasks.md fills in exact names).

## Prerequisites

- .NET SDK from `global.json`.
- Network access to DigiByte peers, or the in-process fake peer used by the automated tests.
- A testnet or mainnet wallet with known history for the live scenarios. For mainnet, use a small throwaway wallet, never real funds' seed in a test log.

## 0. Peer capabilities (research re-check)

Re-run the probe that produced the research findings:

```bash
python specs/011-p2p-spv-gateway/crawl.py
```

Expect: a count of reachable peers, how many serve compact filters and how many keep full history. Record the result in research.md before and after implementation. If any peers now serve filters, open an M3 task. Also record the result of the hasher feasibility spike (Qubit and Odocrypt ports) in research.md R3 before starting the foundational work.

## 1. Header and proof checks (Crypto layer)

```bash
dotnet test tests/DigiByte.Crypto.Tests --filter "FullyQualifiedName~Headers"
```

Expect passing tests that cover:
- Real mainnet header vectors for every algorithm and the retarget eras after the checkpoint.
- Rejection of a header with a broken link, wrong difficulty, an unknown algorithm or a bad hash.
- Merkle proof acceptance for a real transaction and rejection after changing one byte of the branch, the index or the transaction.

Covers FR-002, FR-012, FR-013, SC-001.

## 2. Gateway against a fake peer

```bash
dotnet test tests/DigiByte.SpvGateway.Tests
```

Expect passing tests that cover:
- Header sync, a reorganization of 6 blocks, and the index rolling back with it (SC-008).
- A scan returning exactly the transactions for the watched addresses and nothing for others.
- A wrong-network address refused. Over-limit requests refused with a clear reason.
- Logs and health output searched for full addresses and transaction contents: none found (SC-007).
- A heavy scan running while normal reads stay responsive (SC-009).

## 3. Run the gateway locally

```bash
dotnet run --project src/DigiByte.SpvGateway
```

Then, in another shell:

```bash
curl http://localhost:5290/v1/health
```

Expect `status` moving from `starting` to `syncing` to `ok`, a rising `peerCount` and `matchingMode: "blocks"`.

## 4. Wallet reads through the gateway (User Story 1)

1. Configure the wallet with the gateway enabled, its URL, and position set to first (for the test only).
2. Switch the public explorers off (block them in the browser or point them at nothing).
3. Open the wallet with a known test history.

Expect: balance, spendable outputs and history match the chain. Before M2 they are labeled "checked". After M2 they are labeled "verified". A transaction less than 6 blocks deep shows as pending and is not in the spendable balance, except change from a payment the wallet just sent.

Tamper test: point the wallet at a proxy that changes one byte of a proof. Expect the transaction is not shown as fact, the source is marked faulty, and the next source answers (User Story 1, scenario 2).

Tip-hash test: point the wallet at a gateway whose chain tip hash differs from the explorer's (the `GatewayTipCrossCheckTests` scenario, or a proxy that changes the hash). Expect the source is marked faulty and not used, and the next source answers. Also confirm the status detail says what "checked" means and does not call it protection against a dishonest gateway.

Old-wallet test: use a wallet whose first payment is older than the gateway's scan window. Expect the first refresh to go through the explorer path with transactions shown as "unchecked (explorer-sourced)", later refreshes to ask the gateway only for new blocks (watch the request heights in the network log), a new payment to appear on the next refresh, a spend of an old output to be detected, and the old transactions to become "checked" over time through proofs. Send a payment and read the balance again before it confirms: the spent inputs must not be offered, the change must be spendable at once and the transaction shown as pending. A payment sent to the wallet by someone else should appear once it is in a block. Delete the wallet (and, separately, use the clear-data action in Settings) and confirm its sync state is gone from the browser's storage and that a new refresh rebuilds it. Also open a wallet that existed before this feature and confirm the first explorer-served refresh records a sync state, and the next refresh goes through the gateway. While the explorers are serving, confirm the network log shows no address or outpoint requests to the gateway, only header downloads, and that the sync state's height still moves forward. Then give the wallet more than 100 addresses and confirm the requests are split.

## 5. Send through the gateway (User Story 2)

1. With explorers and the node unreachable, send a small test transaction.
2. Watch the result in the wallet and the response from `/v1/broadcast`.

Expect `outcome: "accepted"`, a later confirmation, and a second submit of the same transaction returning the same id with no error. Then stop the gateway and send again: the wallet falls back and the send succeeds if any other source is up.

## 6. Restore (User Story 3)

1. Restore a known test seed with funds on both receive and change addresses, using a date shortly before the first payment.
2. Interrupt it halfway by closing the tab, then reopen.
3. Repeat with no date given.

Expect: all funds found, a progress indicator, resume from where it stopped, and (no date) a clear message and the explorer fallback path when the range is beyond the gateway's limit.

## 7. Degraded modes (User Story 4)

Run the wallet while the gateway is: stopped, freshly started and syncing, and artificially behind the chain by more than the tolerance.

Expect: balance, receive, send and history behave as before, no new errors, an accurate source indicator, and automatic switch back after the stability window once the gateway is healthy (SC-005, SC-006).

## 8. Privacy and security checks

- Capture the wallet's traffic to the gateway: confirm only addresses, heights and signed transactions appear, never keys, seed words or an extended public key (FR-014).
- Confirm the wallet's Content Security Policy allows only the configured gateway origin and nothing broader.
- Confirm the status detail shows the one-line note that the service sees the addresses it is asked about (FR-020).
