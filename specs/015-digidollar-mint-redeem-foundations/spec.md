# Feature Specification: DigiDollar mint and redeem foundations

**Feature Branch**: `feature/digidollar-mint-redeem-foundations` (not created yet)

**Created**: 2026-11-16

**Status**: Draft

**Input**: User description: "DigiDollar mint and redeem foundations (spec 015). Make minting and redeeming DigiDollar possible in the wallet later, without any UI in this spec: pin down the exact mint and redeem transaction formats from the DigiByte Core documentation and real mainnet transactions, build and verify the transaction builders and a collateral position reader against those transactions, and find out whether the SPV gateway can read and verify the oracle price from the chain so that digibyte.io is no longer needed."

## Background

The wallet can show DigiDollar balances and history, and send DigiDollar it already holds. It cannot create new DigiDollar (mint, by locking DGB as collateral) or give DigiDollar back to unlock the collateral (redeem). Spec 001 (FR-028) keeps both out of the wallet, and that rule stays in force here.

Three things stand between the wallet and minting or redeeming:

1. **The exact transaction formats are not written down in this project.** The DigiByte Core documentation describes them in outline (a locked collateral output, a zero-value DigiDollar output, a data output, lock tiers with collateral ratios, limits and fees). Nothing in the wallet has been checked against a real mainnet mint or redeem.
2. **The wallet has no way to build or read them.** The wallet's cryptography layer can build and sign DigiDollar sends. It has no mint or redeem transaction, and no way to read a locked collateral position back from the chain.
3. **The price a mint or redeem depends on is third-party today.** The network checks a mint or redeem against an oracle price that miners commit into blocks. Spec 014 shows that price from digibyte.io, display only. A transaction that locks real funds must not rest on an unverified third-party price. The chain data the gateway already downloads may contain the same price, signed by the oracle set, which would let the wallet use a price it can verify and drop digibyte.io.

This spec produces the foundations: the verified formats, the transaction builders and reader, and an answer about the on-chain oracle price. It adds nothing a wallet user can see or press.

Terms: "the gateway" is the project's SPV gateway. "Golden vector" means a real transaction from the chain (raw bytes plus the facts it should decode to) used as the test that the builders and reader are right. "Position" means one locked collateral output together with the DigiDollar minted against it.

## Clarifications

### Session 2026-11-16

- Q: If the DigiByte Core post-quantum mint plan changes the mint data payload, which format should the builders target? → A: Only the format real mainnet transactions use today (option A). The post-quantum layout is recorded as "planned, not built" and is added by a later change once it appears on chain.
- Q: Should the gateway's new oracle price endpoint be deployed to the live gateway in this spec, or only built and tested? → A: Deployed to the live gateway, read-only, and not used by the wallet yet (option B), because the gateway's API is not shared with anyone outside the project.
- Q: Should the builders also support multisig wallets for minting and redeeming, or only single-key wallets? → A: Single-key wallets only (option A). Multisig mint and redeem is recorded as a later follow-up.
- Q: If the gateway can read the oracle commitment but cannot verify its signatures by itself, should it still serve the price marked "unverified"? → A: No (option A). The endpoint serves only verified prices; if verification is not possible it serves nothing and the research records why.

## User Scenarios & Testing *(mandatory)*

The people served by this spec are the wallet's maintainers and the later specs that build on it (016 and 017). Wallet users benefit when those later specs ship.

### User Story 1 - A written, verified description of mint and redeem (Priority: P1)

A maintainer opens one document and finds, for mint and for redeem: every output and its order, the data payload and each of its fields, the lock tiers with their collateral ratios and lock lengths, the mint limits, the minimum DigiDollar output, the fee rules, what makes a redeem valid, and what changes when the network is in its emergency (ERR) state. Each rule is marked as confirmed against real chain data or as taken from documentation only.

**Why this priority**: Everything else depends on getting the formats right. A wrong lock script or amount rule in a transaction that locks real funds can strand them. This must be settled from evidence before code is written.

**Independent Test**: Take the document and, for each rule, find the real transaction or the documentation passage that backs it. Pick any captured mint or redeem and read it by hand using only the document. Every field must match.

**Acceptance Scenarios**:

1. **Given** the research is done, **When** a maintainer looks up the mint transaction, **Then** the document lists each output, its order, its value and its script form, with the DigiDollar data fields and their sizes, and cites a real transaction for each.
2. **Given** the research is done, **When** a maintainer looks up the lock tiers, **Then** each tier shows its lock length and collateral ratio, and the document says whether each was seen on the chain or only read in documentation.
3. **Given** a rule in the documentation conflicts with what a real transaction shows, **When** the maintainer reads the document, **Then** the conflict is listed with both sources and the rule the wallet will follow.
4. **Given** a rule could not be confirmed from chain data, **When** the maintainer reads the document, **Then** it is marked unconfirmed and a later spec cannot treat it as settled.

---

### User Story 2 - Build mint and redeem transactions that match the chain exactly (Priority: P1)

A maintainer can ask the wallet's cryptography layer to build an unsigned mint transaction (collateral amount, tier, owner key, funding coins, change) and an unsigned redeem transaction (a position, its DigiDollar, fee coins), and the bytes it produces match what the real network produces for the same inputs.

**Why this priority**: This is the core of "possible". With correct builders and tests, the later service and UI work is plumbing. Without them, nothing can be minted.

**Independent Test**: For each golden vector, feed the builder the inputs read from that real transaction and compare its output byte for byte with the real one (ignoring signatures where the real one is signed). Then check that the builder refuses invalid requests.

**Acceptance Scenarios**:

1. **Given** the inputs of a real mainnet mint, **When** the builder builds a mint, **Then** the outputs, their order, scripts, amounts and data payload equal the real transaction's.
2. **Given** the inputs of a real mainnet redeem, **When** the builder builds a redeem, **Then** the outputs and data payload equal the real transaction's.
3. **Given** a mint request below the minimum, above the maximum, with an unknown tier, with a collateral amount that does not meet the tier's ratio at the given price, or whose DigiDollar output would be under the minimum, **When** the builder is asked to build it, **Then** it refuses with a reason and builds nothing.
4. **Given** a redeem request for a position whose lock has not expired, or for less than the position's full principal, **When** the builder is asked to build it, **Then** it refuses with a reason.
5. **Given** the network is in its emergency (ERR) state and the extra DigiDollar burn is required, **When** a redeem is built, **Then** the burn is computed from the position, never above the documented 125% limit, and the data payload and change reflect it.
6. **Given** the fee rules, **When** a mint or a redeem is built, **Then** the fee is at least the documented floor and is paid from funding coins, never from the collateral.
7. **Given** a built transaction, **When** it is signed with the test keys, **Then** the signed result is accepted as valid by the reference check available to the tests (see FR-012).

---

### User Story 3 - Read a locked collateral position back from the chain (Priority: P1)

Given a raw transaction from the chain, a maintainer can find out whether it is a mint, a redeem, or neither; and for a mint, read the position it created: collateral amount, the DigiDollar minted, tier, unlock height and owner. Later specs use this to list a person's open positions and decide what can be redeemed.

**Why this priority**: Redeem needs to know what was locked and when it unlocks. The reader is also the check that the builder's output is understood the same way the chain's is.

**Independent Test**: Run the reader over every golden vector and over ordinary transfers and non-DigiDollar transactions. Mints and redeems decode to the facts recorded for them; everything else is reported as neither.

**Acceptance Scenarios**:

1. **Given** a real mint, **When** the reader reads it, **Then** it returns the collateral amount, DigiDollar amount in cents, tier, unlock height, owner key and the identity of the locked output.
2. **Given** a real redeem, **When** the reader reads it, **Then** it returns which position it closed, the DigiDollar burned and any DigiDollar change.
3. **Given** a DigiDollar transfer or a plain DGB transaction, **When** the reader reads it, **Then** it reports "not a mint or redeem".
4. **Given** truncated, malformed or contradictory data (for example a tier that does not match the lock length), **When** the reader reads it, **Then** it reports "invalid" with the reason and never returns a position.
5. **Given** the same transaction built by the builder and read by the reader, **When** the facts are compared, **Then** they equal the inputs.

---

### User Story 4 - Find out whether the gateway can supply a verified oracle price (Priority: P1)

A maintainer gets a clear answer, backed by real blocks, to: where does the oracle price sit on the chain, what does its signed bundle contain, how is one price chosen from it, which oracle keys and how many signatures make it valid, and can the gateway check all of that on its own? If yes, the gateway offers the latest verified oracle price (value, the block it applies to, its age, and the proof it was checked); if no, the document says exactly why and what the wallet will use instead.

**Why this priority**: Minting and redeeming must rest on a price the wallet can trust, and the answer decides whether digibyte.io stays (spec 014) or goes. It is also the main unknown in the whole project, so it should be answered early.

**Independent Test**: Take several real blocks that carry an oracle commitment and several that do not. For each commitment the gateway's price equals the price the full node reports for that block, and equals digibyte.io's price for the same block height. Alter a copy of one commitment (a changed price, a missing signature, a wrong key) and confirm the gateway refuses it.

**Acceptance Scenarios**:

1. **Given** a real block that carries an oracle commitment, **When** the gateway reads it, **Then** it returns the price, the block height it applies to and the number of valid signatures, and the price equals the full node's for that block.
2. **Given** a commitment with fewer valid signatures than the network requires, a signature from a key outside the oracle set, or a changed price, **When** the gateway reads it, **Then** it refuses the price and says why.
3. **Given** blocks with no oracle commitment, **When** the gateway scans them, **Then** the latest earlier verified price stays the current one and its age in blocks is reported honestly.
4. **Given** the latest verified price is older than the freshness limit the network uses for mints and redeems, **When** the price is requested, **Then** it is reported as stale, not as current.
5. **Given** a price from the gateway and the price digibyte.io shows for the same height, **When** they are compared over a test period, **Then** the differences are recorded in the document (count, size and cause).
6. **Given** the gateway cannot verify the price (for example the signing scheme cannot be checked without the full node's state), **When** the research is finished, **Then** the document states the blocking reason, the evidence, and the fallback price source recommended for spec 016.

---

### User Story 5 - Retire digibyte.io if the gateway's price is good enough (Priority: P2)

If the gateway can supply a verified price, a decision is recorded on whether the wallet's oracle display (spec 014) should switch to it and digibyte.io be removed, with the conditions that must hold first and what the display loses (for example the volatility, minting-restricted and emergency flags that digibyte.io reports and the chain price alone may not give). The switch itself is not made in this spec.

**Why this priority**: It is the payoff of Story 4 and a real simplification, but it depends on that answer and is a change to a shipped feature, so it follows the research.

**Independent Test**: Read the decision record. For each item spec 014 shows from digibyte.io, it says whether the gateway can supply it, where else it would come from, or that it would be dropped.

**Acceptance Scenarios**:

1. **Given** the gateway supplies a verified price, **When** the maintainer reads the decision, **Then** every item the oracle display shows today is listed with its new source or its removal.
2. **Given** some displayed status cannot come from the chain, **When** the maintainer reads the decision, **Then** the choice is stated (keep digibyte.io for that part only, or drop it) with the trade-off.
3. **Given** the decision, **When** spec 016 is written, **Then** it can take the price source as settled without repeating the research.

---

### Edge Cases

- The documentation, the code and the chain disagree on a field size, an order or a limit. The chain wins; the conflict is recorded.
- A tier or limit in the documentation has never appeared on the chain (for example the longest locks). It is marked unconfirmed and the builder keeps it behind the documented rule.
- The format changes in a later network release, including the post-quantum mint plan in the DigiByte Core documentation. The builders target only the format real mainnet transactions use today; a planned layout is recorded as "planned, not built". The research records which version the formats were verified against, and the builder's tests fail loudly if a vector stops matching.
- A mint is requested at a collateral level that meets the ratio at one price but not at the price a block later. The builder works from a price it is given and says which price it used; checking against the live price is a later spec's job.
- Funding coins are too small or too fragmented to pay for the mint plus fee (the node can report a "consolidation pending" case). The builder refuses with a reason and does not partly build.
- A redeem names a destination for the unlocked collateral, and the wallet cannot also supply a change address of its own. The builder refuses so that leftover funds never go to an address the wallet does not own.
- A block carries more than one oracle commitment (the network rejects this). The gateway treats such a block as invalid and does not take a price from it.
- The gateway has not yet seen any block with a commitment (new or empty index). The price is reported as unavailable, not zero.
- Different networks (mainnet and testnet26) use different oracle keys and thresholds. The gateway uses the set for its own network and never mixes them.
- Fixture transactions are public chain data. No private key or seed ever appears in a fixture; test keys are generated for the tests.

## Requirements *(mandatory)*

### Functional Requirements

**Research and record**

- **FR-001**: The project MUST have a written record of the mint transaction: the output list and order, the script form of each output, the zero-value DigiDollar output, the data payload with each field and size, the transaction version marker, and how the lock length and tier relate.
- **FR-002**: The record MUST cover redeem: what makes it valid (expired lock, the full principal), the outputs, the data payload when there is DigiDollar change, where the unlocked collateral goes, and how the fee is paid.
- **FR-003**: The record MUST list the ten lock tiers with lock length and collateral ratio, the minimum and maximum mint, the minimum DigiDollar output, the fee rate and the fee floor, and the emergency (ERR) burn rule up to its limit.
- **FR-004**: Every rule in the record MUST be marked "confirmed on chain" (with the transaction it was seen in) or "documentation only". Conflicts between sources MUST be listed with both sides and the chosen rule.
- **FR-005**: The record MUST name the network release the formats were checked against and the documents they were read from. Formats described in the documentation but not yet seen in real mainnet transactions (including the post-quantum mint plan) MUST be listed as "planned, not built" and MUST NOT be implemented by the builders in this spec.
- **FR-006**: The project MUST hold at least five real mainnet golden vectors, including at least one mint and one redeem (and, where they exist, a redeem under the emergency rule and a redeem with DigiDollar change), stored as raw bytes with the facts each should decode to. Testnet26 vectors MAY be added for rules mainnet has not shown. Each vector MUST record where it came from.

**Transaction builders and reader (cryptography layer)**

- **FR-007**: The cryptography layer MUST provide a mint builder that, from the request (collateral, tier, owner key, price used, funding coins, change address), returns an unsigned mint transaction in the recorded format, with the fee at or above the floor and paid from funding coins.
- **FR-007a**: The builders in this spec MUST support single-key wallets only. A request that names a multisig owner or funding input MUST be refused with a reason. Multisig (partially signed) mint and redeem is out of scope and is a later follow-up.
- **FR-008**: The cryptography layer MUST provide a redeem builder that, from a position, the DigiDollar to burn, funding coins and a destination, returns an unsigned redeem transaction in the recorded format, applying the emergency burn rule when told the network is in that state.
- **FR-009**: Both builders MUST refuse, with a stated reason and no output, any request that breaks a recorded rule: amount below the minimum or above the maximum, unknown tier, collateral below the tier's ratio at the price given, DigiDollar output below the minimum, lock not expired, less than the full principal, a mismatch between tier and lock length, or funding that cannot cover the amount and fee.
- **FR-010**: The cryptography layer MUST provide a reader that classifies a raw transaction as mint, redeem or neither, returns the position facts for a mint and the closing facts for a redeem, and reports malformed or contradictory data as invalid with a reason.
- **FR-011**: Builders and reader MUST be pure: no network access, no clock reads, no randomness except through the caller-supplied keys, so the same inputs always give the same bytes.
- **FR-012**: Each builder MUST be tested against every relevant golden vector for equal outputs, order, scripts, amounts and data payload. A built-and-signed transaction MUST also be checked by an independent validity check (for example the reference script check available to the tests, or a real node on testnet26) so a transaction that merely looks right is not accepted.
- **FR-013**: Builders and reader MUST work for mainnet and testnet26, take the network as an input, and refuse a request whose addresses belong to the other network.
- **FR-014**: Following the project's rules for the cryptography layer, the tests MUST include the negative paths named in FR-009 and malformed-input paths for the reader (truncated, wrong marker, wrong tier, trailing bytes).

**Oracle price from the chain (gateway)**

- **FR-015**: The research MUST determine, from real blocks, where the oracle commitment sits in a block, its layout, how the single price is chosen from it, the oracle key set and signature threshold per network, the freshness limit, and what a mint or redeem is checked against.
- **FR-016**: If the gateway can verify it, the gateway MUST read the oracle commitment from the blocks it already fetches, verify it against the oracle key set and threshold for its network, and serve the latest verified price with its block height, its age in blocks and how many signatures were valid. It MUST NOT serve a price it could not verify.
- **FR-017**: The gateway MUST report "unavailable" (not a zero or an old price as current) when it has no verified price, and "stale" when the latest is beyond the freshness limit. A block with more than one commitment, a commitment with too few valid signatures, or one signed by an unknown key MUST NOT produce a price.
- **FR-018**: The gateway's price MUST be compared against the full node's value and against digibyte.io for the same heights, and the comparison (how many blocks, how many differ, by how much, why) MUST be recorded.
- **FR-019**: The new gateway endpoint MUST follow the existing gateway rules (no wallet data in requests, bounded response size, same error shape as the other endpoints) and MUST be covered by tests that alter a real commitment and expect refusal.
- **FR-019a**: If the gateway can verify the price, the endpoint MUST be deployed to the live gateway in this spec as read-only, so the comparison in FR-018 can run against the live gateway. It MUST NOT be called by the wallet, MUST NOT be announced in the wallet's settings, documentation for integrators or any public page, and MUST be removable without affecting any other gateway endpoint (the wallet's existing reads, scans, proofs and broadcast stay unchanged).
- **FR-019b**: The gateway MUST NOT offer any price labelled "unverified". If it can read a commitment but cannot verify its signatures, the endpoint is not deployed and the price read is recorded in the research only.
- **FR-020**: If the gateway cannot verify the price, the research MUST say exactly why and recommend the fallback price source for spec 016, with its trust trade-offs. In that case spec 014 and digibyte.io stay as they are.

**Decision on digibyte.io**

- **FR-021**: The research MUST record a decision on whether the oracle display from spec 014 moves to the gateway's price: for each item that display shows (price, update block and age, stale state, volatility, emergency state, minting restricted, all operations restricted) it MUST say what supplies it afterward or that it is dropped.
- **FR-022**: This spec MUST NOT remove or change the digibyte.io feed. Any switch is made by a later change once the decision is accepted.

**Boundaries**

- **FR-023**: This spec MUST NOT add any screen, button, route, menu entry or text a wallet user can reach, MUST NOT call the new builders from any wallet flow, and MUST NOT change spec 001 FR-028. The mint teaser stays as it is.
- **FR-024**: This spec MUST NOT send any transaction to mainnet. Any live check of a built transaction uses testnet26 and test funds only.
- **FR-025**: No private key, seed or password may be stored in a fixture, document or log. Test keys are generated by the tests.

### Key Entities *(include if feature involves data)*

- **Golden vector**: A real chain transaction as raw bytes, its network, where it came from, and the facts it must decode to. The test oracle for builders and reader.
- **Format record**: The written description of the mint and redeem transactions, tiers, limits and fees, with each rule's evidence level (confirmed on chain or documentation only).
- **Lock tier**: One of ten levels with a lock length and a collateral ratio. A mint names one tier.
- **Mint request / Redeem request**: The inputs a builder takes. A mint: collateral, tier, owner key, price used, funding coins, change address, network. A redeem: position, DigiDollar to burn, emergency flag, funding coins, destination, network.
- **Position**: One locked collateral output and its DigiDollar. Facts: output identity, collateral amount, DigiDollar amount in cents, tier, unlock height, owner key, network.
- **Oracle commitment**: The signed price data a miner puts in a block. Facts: block, price, the oracle keys that signed, how many signatures.
- **Verified oracle price**: The latest price from a commitment the gateway checked: value, block it applies to, age in blocks, signature count, state (current, stale or unavailable).
- **Source decision**: The record of whether the oracle display moves to the gateway's price and what each displayed item's source becomes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every rule in the format record is marked confirmed or documentation-only, and at least 90% of the rules the builders depend on are confirmed against real chain transactions; the rest are listed by name.
- **SC-002**: The builders reproduce 100% of the golden vectors byte for byte (outputs, order, scripts, amounts and data payload), and the reader decodes 100% of them to their recorded facts.
- **SC-003**: Every invalid request listed in FR-009 is refused with a reason, in a test for each, and the reader reports each malformed case in FR-014 as invalid; none returns a position or a transaction.
- **SC-004**: At least one mint and one redeem built and signed with test keys are accepted by an independent validity check on testnet26 (or the reference script check where a live check is not possible), with the result recorded.
- **SC-005**: Over at least 100 consecutive real blocks that include commitments, the gateway's verified price equals the full node's price for every one of them (or the document explains each difference), and every deliberately altered commitment is refused.
- **SC-006**: The research gives a yes or no answer, with evidence, to "can the gateway supply a verified oracle price", and a recorded decision for each item the oracle display shows.
- **SC-007**: No wallet screen, route or flow changes: a review of the change finds no user-reachable difference, and the existing wallet and gateway test suites still pass.

## Assumptions

- The DigiByte Core documentation (the wallet integration guide, the post-quantum mint plan, the oracle testing guide, the operations notes) and the Core source are available to read, and real mainnet mint and redeem transactions exist to capture. If no mainnet redeem or emergency-state redeem exists yet, testnet26 vectors stand in and the rule is marked accordingly.
- Mainnet DigiDollar is active, so real mints and redeems exist. The documented figures (35 oracle keys, 7 or 9 required signatures, 40-block epochs, 4-block update interval, 20-block freshness) are claims to confirm; the sources disagree on the threshold.
- The gateway's API is not shared with anyone outside the project, so a read-only, unadvertised endpoint on the live gateway carries no outside exposure; the project's own tests and comparisons are its only callers.
- The gateway already downloads full blocks and can see the coinbase, so reading a commitment needs no new network access. Checking the signature scheme may need more than the gateway has; that is what Story 4 finds out.
- The chain's price is the one the network checks mints and redeems against, so it is the right source for the wallet to use if verified; digibyte.io is a convenience copy.
- Wallet users are not affected in this spec. Spec 016 (price cross-check, position tracking and the wallet service) and spec 017 (screens, behind a switch) follow, and each gets its own spec and review.
- The cryptography layer's current dependencies suffice for the transaction shapes; any new dependency (for example for the signature scheme) is proposed in the plan and flagged as security-relevant under the project's rules.
- Testnet26 and test funds are available for the live check, per spec 001 FR-029.
