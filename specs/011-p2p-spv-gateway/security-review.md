# Security review notes: P2P light-client gateway (T118)

A self-review of the branch by the author, not an independent audit. It lists what was checked, what was found and fixed on the way, and what is still open. An independent review should still happen before the gateway is switched on for users.

## What was checked

| Area | Result |
|---|---|
| CSP origin (T045) | `connect-src` in `index.html` lists one gateway origin, currently the placeholder `https://spv.dgbwallet.app`. It must be replaced by the real origin before release. This is the only CSP change. Flag it in the PR description. |
| Anonymous endpoints | Every endpoint is rate limited per client (120 a minute), bounded (100 addresses, 100 outpoints, 20 proofs, 50,000 blocks a scan, 4 scans at once, 400 KB transactions) and answers errors in one shape with no request data. `TrustForwardedFor` is off by default and must be on only behind a proxy that sets the header. CORS allows any origin without credentials, which is acceptable for an anonymous read-and-relay service. |
| Request data in logs and files | Tests (`LogRedactionTests`, `ProofEndpointTests`, `ScanEndpointTests`) check that no address, transaction, transaction id or raw body reaches a log line, a response header, the health report or any file other than the public header and index files. |
| Peers | The peer interface cannot carry an address, script or filter; a test also searches the connector source for filter and bloom messages. Peers get block hashes, header locators and the broadcast transaction. |
| Reorganizations | The gateway and the wallet both follow the branch with more work, step back to the fork, and re-check headers; the wallet drops transactions that left the chain (`GatewayReorgTests`, `HeaderDownloaderTests`). One path (keeping the wallet's own branch when a gateway branch has less work) cannot be reached with real data and is untested. |
| Eclipse resistance | The wallet compares its headers with an independent explorer at the tip and at 1, 10 and 100 blocks below it, and treats a settled-height mismatch as a faulty source. A difference at the tip alone is treated as a race. A hidden fork that differs only in the last block cannot be told from a race. |
| Proofs | Every transaction the wallet uses is matched to a header the wallet holds, never to one the gateway supplied. A 64-byte transaction (confusable with two merkle nodes) is refused. Merkle proofs do not cover witness data. |
| Staged labels | Nothing is labelled "verified" and the gateway stays a backup source while `GatewayCapabilities.WalletPowVerified` is false (tested). |

## Found and fixed during the work

1. The framework request log wrote full URLs, so `GET /v1/tx/{txid}` put transaction ids in the logs. The request-log categories are now warnings only.
2. The header store shared one SQLite connection between the sync loop and the HTTP endpoints without a lock, which caused random failures. It is now locked.
3. The background peer service was registered from a configuration value that a test host had not applied yet, so every test host opened real peer connections and indexed blocks in the background. It now reads the option when it starts.
4. `RuntimeConfigLoader` accepted only the node and DigiDollar sections, so the gateway settings written by the container script would have been ignored. It now accepts those six settings.
5. A modern DigiByte node closes the connection for a protocol version below 70017; the connector now announces 70019.

## Still open

- **Proof of work in the wallet (T113, T114)**: not measured under WebAssembly. The cost is native-only (scrypt 2.2 ms a header). Until it is measured and the flag is set, headers are checked for linkage, difficulty and time but not proof of work.
- **Gateway broadcast against real peers**: not tried. Modern nodes may not send reject messages, so "accepted" is not proof of mempool acceptance.
- **Not run in a live app**: the registration graph, the new UI pieces and the restore flow have not been exercised in a browser (the preview server could not start because port 5251 was in use). They are covered by unit and integration tests and a successful build only.
- **Checkpoint**: the built-in checkpoint holds 84 headers, so the gateway can answer for about 83 blocks before it. A checkpoint about 90 days back has to be built in, and the first fill of a full window has not been timed.
- **Wallet sync record**: stored as plain JSON next to the other caches. It lists the wallet's transactions; encrypting it under the store secret is worth deciding.
- **Result cache**: the gateway keeps no result cache at all, so the "seconds-long in-memory cache" in the plan does not exist; that is stricter than asked, not looser.
- **Fee endpoint**: reports the network minimum only (the gateway cannot see what transactions paid).
- **Testnet**: no testnet checkpoint, so the gateway refuses to start on testnet, and the wallet only offers the gateway on mainnet.
