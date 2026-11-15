# Research: P2P Light-Client Gateway

All items from the spec's open question and the plan's Technical Context are resolved here, or recorded with the test that will resolve them.

## R1. Do public DigiByte peers serve compact block filters?

**Decision**: No. Design v1 around downloading blocks and keep a swap-in seam (`IBlockMatcher`) for filters later.

**Evidence**: I wrote a small probe ([probe.py](probe.py), [crawl.py](crawl.py)) that opens a peer connection, sends a `version` message and reads the answering service bits. Run on 2026-11-15, it crawled the address book of one seed peer (529 addresses) and reached 8 peers.

| Service bits seen | Meaning | Peers |
|---|---|---|
| `0x409` | network, witness, network-limited | 5 |
| `0x408` | witness, network-limited (no full history) | 1 |
| `0xc08` | witness, network-limited, v2 transport (no full history) | 1 |
| compact filters (bit 6) | | 0 |
| bloom (bit 2) | | 0 |

Peer software was DigiByte 9.26.3 to 9.26.7. Only 8 of 150 probed addresses answered, so the sample is small. Re-run before the plan is implemented and again before release.

**Cross-check against the network page** (chainz.cryptoid.info/dgb, "Network" tab, read 2026-11-15): about 256 clients seen in the last 24 hours, 99 connections from that explorer's own node. Versions: 9.26.x about 77%, 8.26.x about 11%, 8.22.x about 3%, 7.17.x about 6%, plus a few seeders and wallets. The page lists version and protocol only, not service bits, so it can't confirm or refute filter support. It does show that most of the network runs the 9.26 line, which matches the peers my probe reached, so the probe sample is not skewed toward one odd version. Filters are an opt-in setting in the Bitcoin Core code DigiByte tracks, so the likely situation is "supported but switched off". That points to our own node as the practical source if we want filters (M3).

**Rationale**: With no filter service and no bloom service, block download is the only privacy-preserving method on the public network, which is what the spec's assumption already allowed.

**Alternatives considered**: Bloom filters (BIP37) are excluded by the spec for privacy and no peer offers them anyway. Our own pruned node (feature 004) might serve filters if DigiByte's `blockfilterindex` and `peerblockfilters` options exist in 9.26. That is unconfirmed (the web search found no DigiByte documentation), so it is M3 work: run `digibyted -help` on our node and test.

**Update after the larger crawl (2026-11-15, tasks T005 and T006)**: the first sample (8 reachable peers) was too small. Using the real DNS seeds (see R5) the crawler reached 36 of 500 probed addresses:

| Service bits | Meaning | Peers |
|---|---|---|
| `0x409` | network (full history), witness, network-limited | 17 |
| `0x408` | witness, network-limited only (pruned, recent blocks only) | 13 |
| `0x449` | as `0x409` plus **compact filters** | 3 |
| `0xc08` | `0x408` plus v2 transport | 2 |
| `0x40d` | `0x409` plus **bloom** | 1 |

- Full history (network bit): 21 of 36. Limited only: 15 of 36. Compact filters: 3 of 36. Bloom: 1 of 36. Versions: 9.26.x for most, a few 8.26.x and one 8.22.0.
- So the earlier "no peer serves filters" was wrong: a small share (about 8%) does. The decision for the first release does not change, because the spec asks for at least 3 peers from different address ranges before filter matching is built, and 3 peers is the minimum with no margin. It does show that filter matching (M3) is realistic and worth re-checking before release.
- From the DigiByte Core source (`init.cpp`, develop branch): `-blockfilterindex` and `-peerblockfilters` both exist, `-peerblockfilters` defaults off and the node sets the compact-filters service bit only when both are on. So our own node can serve filters by configuration, which answers task T006 without running a node. Verify on a real node before relying on it.
- One reachable peer reported a block height of 23,869,662 against about 24,563,415 for the rest, so some peers are still syncing. The gateway must compare peers' heights and not trust a single peer's tip.

## R2. How does the gateway match addresses without per-wallet state?

**Decision**: The gateway builds its own **wallet-independent block index** as it downloads blocks: for each block, a compact record of the script hashes of every output and every spent outpoint. Matching a wallet's addresses is then a local lookup against that index, so the gateway stays stateless per wallet (Clarification 3) and peers never learn which addresses anyone watches.

**Facts that shape it**:
- DigiByte is at about block 24.5 million with 15-second blocks (about 5.8 million blocks a year). Downloading full history through peers is not realistic, so the index covers a configured recent window (default 90 days, about 520,000 blocks, operator-adjustable; confirmed in the spec), and ranges older than the window are refused with `range_unavailable` so the wallet uses its explorer path.
- Peers advertising only `NETWORK_LIMITED` keep only about the last 288 blocks. The gateway must prefer peers with the full-history service bit when it needs older blocks and fall back to the explorer path when none answer.
- The wallet drives restore in bounded chunks (a height range per request) and keeps its own progress, so an interrupted restore resumes without the gateway remembering anything.

**Open measurement (M0)**: real average block size and per-peer throughput, to size the index and set the chunk size. The 5-minute target for a 30-day restore (172,800 blocks) depends on them.

**Alternatives considered**: An Electrum-style address-to-transaction index for all history is simplest to query but needs a full node and gigabytes of storage, which the spec rules out. Per-wallet watch lists would be faster but store address sets, which Clarification 3 rules out.

**Measured (tasks T008 and T009, 2026-11-15)**: 400 consecutive mainnet blocks (heights 24,563,017 to 24,563,416, from the digiexplorer.info Esplora API; every header rebuilt from the block fields matches its block id):

| Measure | Value |
|---|---|
| Block size | mean 567 bytes, median 357, 95th percentile 938, maximum 39,234 |
| Transactions per block | mean 1.21, maximum 6 |
| Algorithm mix | sha256d 89, scrypt 84, skein 78, odo 77, qubit 72 (all five active algorithms appear) |

- A 30-day restore is 172,800 blocks, so about 100 MB to download; the 90-day index window is about 520,000 blocks, so about 300 MB. Both are modest, and the 5-minute restore target is realistic if peers deliver a few MB per second. Per-peer throughput still has to be measured on the running gateway (task T033 onwards).
- Blocks are small enough that the first sync of the 90-day index should take minutes to tens of minutes, not hours, given enough full-history peers (21 of 36 reachable peers).

## R3. How does the wallet verify DigiByte proof of work?

**Decision**: Port DigiByte's multi-algorithm proof of work and retarget rules into `DigiByte.Crypto/Headers`, tested against real mainnet header vectors. Start the wallet's chain from a built-in checkpoint close to the tip, so it downloads and checks megabytes of headers, not gigabytes.

**Facts** (from DigiByte Core `block.h` and `pow.cpp`, read 2026-11-15):
- The algorithm is encoded in the block version: mask `15 << 8`. Constants: Scrypt `0 << 8`, SHA256D `2 << 8`, Groestl `4 << 8`, Skein `6 << 8`, Qubit `8 << 8`, Odocrypt `14 << 8`. Five algorithms are active (`NUM_ALGOS` is 5).
- Difficulty is tracked per algorithm. Retargeting has four eras, chosen by height: `GetNextWorkRequiredV1` to `V4`, using consensus heights `multiAlgoDiffChangeTarget`, `alwaysUpdateDiffChangeTarget` and `workComputationChangeTarget`. A checkpoint after the last era boundary means the wallet only needs V4.
- The hash functions per algorithm are not in the files I read. They live in the block hashing code and the hash library. Odocrypt takes a key derived from the block time (`OdoKey`), which changes on a schedule.
- The wallet's current `DigiByteNetwork` consensus (`PowTargetTimespan` 14 days, Bitcoin-style) is not DigiByte's real rule and must not be used for header checks. NBitcoin's own header checks assume SHA256D and would reject valid blocks.

**Findings from DigiByte Core source (tasks T010 and T011 assessment, 2026-11-15)**:
- Active algorithms after height 23,808,000 (`nGroestlDeactivationHeight`, v9.26.2) are SHA256D, Scrypt, Skein, Qubit and Odocrypt. Groestl blocks are rejected after that height, and Odocrypt has been active since height 9,112,320. A checkpoint after 23,808,000 means the wallet never needs Groestl or any pre-V4 retarget code. The retarget version is V4 from `workComputationChangeTarget` (1,430,000).
- PoW hash per algorithm (`primitives/block.cpp`): SHA256D is the block hash itself; Scrypt is `scrypt_1024_1_1_256` over the 80-byte header; Skein, Qubit and Groestl hash the serialized header with their own functions; Odocrypt hashes it with a key `nTime - nTime % nOdoShapechangeInterval` (10 days on mainnet, 1 day on testnet).
- V4 retarget (`pow.cpp`, `GetNextWorkRequiredV4`): needs the last block of the same algorithm (`pindexPrevAlgo`), the block `NUM_ALGOS * nAveragingInterval` = 50 blocks before the tip, and the median time past of both. Parameters on mainnet: averaging interval 10, target timespan `10 * 75 = 750`, adjust up 8% and down 16% (minimum timespan 690, maximum 870), per-algorithm adjustment 4%, `powLimit` = all ones shifted right by 20 bits. The timespan is first damped: `750 + (actual - 750) / 4`. So the checkpoint must carry at least the last 61 headers (50 plus 11 for the median) and the last header of each algorithm.
- Source size for the ports (DigiByte Core `src/crypto`): Odocrypt about 12 KB of C++ (small), Skein about 28 KB, Qubit is five hash functions in about 167 KB of C++ with large constant tables (Luffa 37 KB, CubeHash 18 KB, SHAvite 38 KB, SIMD 50 KB, ECHO 24 KB). NBitcoin does not provide Skein, Luffa, CubeHash, SHAvite, SIMD, ECHO or Odocrypt. Estimate: SHA256D and Scrypt are available now; Odocrypt and Skein are about a day each; Qubit is the large one, several days with test vectors. The feasibility spike (T011) is therefore still open.
- **Oracle for hasher tests**: the explorer API gives no PoW hash, but a correct hasher must produce a hash at or below the target for every real block. With 400 real headers covering all five algorithms, a wrong hasher fails almost certainly, so `hash <= target(nBits)` over the fixtures is the test.

**Update (tasks T023 and T025, 2026-11-15)**: the Skein and Odocrypt ports are done and verified. Skein-512 matches the published known answer for the message FF, and for each algorithm every real block in the 400-block fixture (78 Skein, 77 Odocrypt) meets its own target with the ported hash, while a changed nonce breaks the work in nearly every case. Odocrypt needed the cipher (about 300 lines), a Keccak-p[800, 12] permutation and the key rule `nTime - nTime % interval`. Both took well under a day each. Together with SHA256D and Scrypt that is four of the five active algorithms; Qubit is the only port left, and the open question for T011 is the effort for its five component hashes.

**Update (task T024, 2026-11-15)**: Qubit is ported, which completes the spike (T011). Luffa-512, CubeHash-512, SHAvite-3-512, SIMD-512 and ECHO-512 each match values produced by DigiByte Core's own C++ sources, which I compiled for this purpose (`qubit_ref.cpp` here, built with g++ in WSL against the DigiByte Core files; its output is `tests/DigiByte.Crypto.Tests/Headers/Fixtures/qubit-reference.json`, 22 inputs of lengths 0 to 200 plus real headers, reproducing the real chain for all six sampled Qubit blocks). The chain also meets its target on every real Qubit block in the fixture. All five active algorithms are therefore implemented and `PowHashers.AllActiveAlgorithmsSupported` is true. With every hasher, the 316 real headers after the checkpoint (all five algorithms) are checked in about 260 ms natively, roughly 0.8 ms per header on average. WebAssembly speed is not measured yet (T098): the wallet checks about 172,800 headers a month, so at even 5 ms each the first sync of a month-old checkpoint would take around 15 minutes, which is why the checkpoint should stay recent and the work should run in the background.

**Mainnet checkpoint (task T010)**: height 24,563,100, block id `febd98bde090f0585949ff6df3b5395e2df6e0b07b48ce3ae43cdce490542dfc`. The fixtures hold the 83 headers before it, which covers the 61-header window and the last header of each algorithm. Each release should refresh the checkpoint to about a month behind the tip (about 172,800 headers, about 14 MB, for the wallet to download). **Testnet** checkpoint: not chosen yet; testnet seeds are in place (R5) and a testnet checkpoint is picked when the testnet gateway is first run (task T103).

**Risk and mitigation**: Qubit (a chain of five hashes) and Odocrypt have no ready managed implementation I know of. Mitigation: M0 collects test vectors first, hashers are ported one algorithm at a time behind one interface, and M1 ships without the "verified" label (see Spec Deltas in the plan). The gateway needs the same hashers from M1, because it must check proof of work before following any chain (FR-002); on the server speed is not a concern. If a port proves impractical for WebAssembly, the wallet's label stays "checked" and the gateway stays a backup source. There is no partial mode: checking four algorithms and trusting the claimed difficulty of the fifth never earns "verified". A time-boxed spike in M0 (task T011) checks both ports against the fixtures before Phase 2 depends on them, and if either is not feasible the spec comes back for a decision, because FR-002 requires the gateway to check proof of work.

**Alternatives considered**: Trusting the gateway's headers with only an explorer tip check is weaker and was rejected in Clarification 1. Requiring the explorer tip hash on every session is cheaper but makes the explorer the root of trust again.

## R4. How is the peer client built?

**Decision**: Use NBitcoin's networking (`Node`, `NodeServer` behaviors) on the existing DigiByte network definition, for connection handling, `getheaders`, `getdata` and `tx` relay. Use own validation for headers (R3) instead of NBitcoin's chain classes.

**Rationale**: NBitcoin 9.0.5 is already a dependency and already knows the network magic and port. It removes the need to write message framing. Block and transaction parsing (including segwit) is the same as Bitcoin's.

**Alternatives considered**: A hand-written minimal protocol client (as in the probe script) is small but needs more edge-case handling. A different library would add a dependency.

## R5. Where do the peers come from?

**Decision**: Use DNS seeds plus a built-in list of known good addresses, and a peer book that remembers peers that worked, ranked by service bits and behavior.

**Resolved (task T007, 2026-11-15)**: DigiByte Core's `chainparams` lists these seeds. Mainnet: `seed.digibyte.io`, `seed.diginode.tools`, `seed.digibyte.link`, `seed.aroundtheblock.app`, `seed.tuyul.cc`. Testnet: `testnetseed.digibyte.io`, `testnetseed.digibyte.link`, `testnetseed.digibyte.services`. All five mainnet seeds resolved (24 to 25 addresses each, except `seed.digibyte.io` with one); on testnet two of three resolved. The repository's old single seed `seed1.digibyte.io` does not exist, and `DigiByteNetwork.cs` now uses the real lists.

**Original evidence**: The repository's only configured seed, `seed1.digibyte.io`, did not resolve from the research machine, and `seed2` and `seed3` did not either. One of the other names I tried did resolve, which gave the probe its starting peers. Seed names need verifying against DigiByte's `chainparams.cpp` in M0, and the network definition updated. The gateway must not depend on a single seed.

## R6. How is the gateway added to the source chain?

**Decision**: Reuse `NodeSourceMonitor` for a second source. It already handles stabilizing, promotion, demotion, three-strike read failures and the height cross-check. Add a `GatewayHealthHttpSource` that maps the gateway's health report to the shape the monitor expects. Extend `FallbackBlockchainService` with a configurable position for the new source (Clarification 2).

**Rationale**: It avoids a second state machine and gets the behavior the spec already asks for. Task planning must check how tightly the monitor is tied to node-specific fields (`NodeReadiness`, `NodeSourceTypes`) and generalize minimally.

## R7. Where are headers and progress kept in the wallet?

**Decision**: In the existing IndexedDB store, outside the encrypted seed store, because headers and scan progress are public data. Birth height is stored with the wallet record (FR-008a).

**Alternatives considered**: Keeping headers only in memory re-downloads them every session. Putting them in the encrypted store adds cost with no secrecy benefit.

## R8. How does broadcast work?

**Decision**: The gateway sends `inv` then `tx` to at least 4 outbound peers and watches for `reject` messages, a matching `getdata`, or the transaction appearing from other peers. It reports accepted, rejected with reason, or no response, and treats a repeat of the same signed transaction as the same payment.

**Rationale**: Mirrors how Bitcoin Core relays. Peers do not confirm acceptance directly, so the report is "relayed to N peers, M rejections, seen back from K" rather than a guarantee.

## R9. How are rate limits and abuse handled?

**Decision**: Per-client limits on request rate, addresses per request (proposal: 100), and blocks per scan chunk, using a small limiter inside the gateway project. `DigiByte.Server.Shared` is not referenced: it is documented as the outbound-request guard for the two existing public APIs, and the gateway is meant to depend on `DigiByte.Crypto` only (plan, Project Structure). Over-limit requests get a clear refusal. A heavy restore chunk is queued behind a bounded worker pool so it cannot starve normal reads (SC-009).

## R10. How does a routine refresh work, given the gateway only knows the last 90 days?

**Decision**: The wallet keeps its own sync state (see data-model.md, WalletSyncState): its transactions and outputs, where each came from, and the height and hash of its last completed refresh. A refresh asks the gateway only for blocks from that height minus 6, sends its known unspent outpoints so spends of outputs older than the scan window are found, and splits long address and outpoint lists into requests of at most 100. A wallet older than the window does its first refresh through the existing explorer path, and its transactions stay "unchecked (explorer-sourced)" until the gateway proves them one at a time through `POST /v1/proofs`, which needs only the single block at a known height.

**Rationale**: A balance depends on the whole history of an address, and the gateway's index covers 90 days by design. Keeping the older part on the device makes every later refresh small and fast and keeps the gateway stateless per wallet.

**Alternatives considered**:
- A whole-chain index of unspent outputs by script on the gateway answers any wallet at any age, but needs gigabytes of storage and a long first sync, and moves toward the full indexer the spec rules out.
- Serving only wallets younger than the window is the cheapest but gives long-time users no benefit.

**Bootstrap (decided)**: a wallet with no sync state, including every wallet that existed before this feature, gets one from the result of an explorer-served refresh, recorded as explorer-sourced with the last-refreshed height set to the explorer's tip height (read before the snapshot) minus 6 and the hash from the wallet's own checked headers. If those headers cannot supply the hash yet, it tries again on the next refresh. Without this step the gateway would never be used by an existing wallet.

**Unconfirmed payments (decided, first release)**: the gateway reports only transactions in blocks, so payments from others appear once they are in a block (about 15 seconds). The wallet records its own broadcasts in its sync state immediately. Matching unconfirmed payments to addresses (a mempool index in the gateway) is deferred behind the same interface.

**Freshness (decided)**: with the gateway as a backup, the wallet re-records its sync state after every explorer-served refresh, so it is current when an outage makes the gateway the answering source. This needs no extra gateway contact. Headers may sync in the background (public data). Proofs for explorer-sourced transactions are requested only when the gateway is the answering source, or when `BackgroundProving` is on (default off), because proof requests reveal transaction ids. Alternative rejected: refreshing against the gateway in the background whenever it is healthy, which keeps proofs current but shows the gateway the wallet's addresses all the time.

**Disagreement rule (decided)**: when a proof or the gateway's chain contradicts what the explorer reported, the gateway's proven data wins, the disputed transaction stays unchecked and out of the spendable balance until resolved, and the wallet keeps a local note. No user-facing warning in the first release.

## Items to confirm in M0

- Re-run the peer probe, and probe our own node for `blockfilterindex` and `peerblockfilters` support.
- Confirm seed names and add working seeds.
- Collect header vectors for all five algorithms, including the earliest and latest Odocrypt keys.
- Measure average block size and peer throughput, then set the index window and chunk size.
- Choose checkpoint heights per network (after the last retarget era change).
- Confirm how `connect-src` for the gateway origin is currently managed for the node API.

**Update (T112 and T113, native only)**: the wallet can now check proof of work (`SpvGateway:CheckProofOfWork`, default off). Native cost per header on the development machine, measured on the real headers of the fixture: SHA-256d 0.003 ms, Skein 0.035 ms, Odocrypt 0.35 ms, Qubit 0.37 ms, Scrypt 2.2 ms. These numbers are NOT WebAssembly numbers; T113 asks for those and they have not been taken. Rough reading only: if WebAssembly were 20 times slower, Scrypt would take about 45 ms a header and a week of blocks (about 40,000) would need several minutes, so the measurement decides whether "verified" can ever be offered. Until it is taken, `GatewayCapabilities.WalletPowVerified` stays false.
