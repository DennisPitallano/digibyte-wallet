# Feature Specification: Sign an Unsigned Transaction (PSBT)

**Feature Branch**: `feature/psbt-signing` (not yet created; the spec directory is `007-psbt-signing`)

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Sign an unsigned transaction (PSBT, BIP 174) from a regular single-sig wallet (HD and WIF). A user pastes or uploads a PSBT (base64 or binary .psbt), reviews what it does (outputs, which are their own change, fee, and which inputs the wallet will sign), then signs with their own keys and either exports the signed PSBT or, if complete, finalizes and broadcasts it. Watch-only wallets cannot sign. Safety requirements: verify the amount and script of each of the wallet's inputs against the chain before signing; reject PSBTs requesting a non-ALL sighash; show a clear 'nothing to sign' error when no inputs belong to the wallet; PSBTs with some inputs belonging to others must sign only ours and stay incomplete for export. Scope limits: key search covers the wallet's existing range (receiving 0-19, change 0-9); legacy inputs without their previous transaction are rejected at first, while SegWit inputs on our own addresses can be filled from our UTXO data; DigiDollar and Taproot inputs are out of scope and are rejected rather than signed. Existing multisig flow stays unchanged. Interoperability with a PSBT from DigiByte Core must be verified on testnet before release."

## Background

A PSBT (Partially Signed Transaction, BIP 174) is a standard file format for passing a transaction between programs or people before it is fully signed. Today this wallet handles PSBTs only inside its multisig flow. A regular wallet holder who receives an unsigned transaction from another tool, such as a desktop node, a coordinator, or a counterparty, cannot sign it here.

A PSBT comes from outside the wallet and may be wrong or hostile, and its contents can misdescribe what a signature would do. A feasibility test on 2026-11-05 showed that the underlying signing capability works for native SegWit and legacy inputs, but does not protect the user by itself: it signs an input whose stated amount is false, reports the resulting wrong fee, silently replaces a requested signature type, and raises no error when there is nothing to sign. This feature therefore treats every PSBT as untrusted, and the checks below are as much a part of it as signing.

## Clarifications

### Session 2026-11-05

- Q: What should the user have to do to authorize signing a PSBT? → A: Every signing requires re-entering the wallet PIN, or biometrics if the user has enabled them. Broadcast needs only its own separate confirmation.
- Q: When another participant's input amount can't be checked, so the fee is unverified or unknown, should the wallet still allow signing? → A: Yes, but only after the user explicitly acknowledges that the fee could not be verified.
- Q: If a PSBT asks for a non-standard signature type on someone else's input only, should the wallet refuse the whole PSBT? → A: Yes. A non-standard request on any input blocks the PSBT.
- Q: Where should PSBT signing appear in the wallet? → A: In Settings, near Recovery Tools, and only in Advanced mode.
- Q: Should the send safety check that warns about unfamiliar addresses and unusually large amounts also run on a PSBT's outputs? → A: Run it on outside outputs when all inputs are the wallet's. For shared transactions, show a single note that outputs were not checked.
- Q: If a PSBT arrives already fully signed, should the wallet offer to broadcast it when none of its inputs belong to this wallet? → A: No. Broadcast is offered only when at least one input is this wallet's; otherwise the wallet shows "nothing to sign" and stops.
- Q: Should the wallet try to look up other participants' input amounts on the chain, so that a shared transaction's fee can be shown as verified? → A: Yes. A stated amount that disagrees with the chain blocks the PSBT; a failed lookup leaves the fee unverified, which needs the acknowledgment.
- Q: Should this version sign wrapped SegWit inputs (P2SH-P2WPKH) that belong to the wallet's keys? → A: No. They are left out of this version and treated as someone else's inputs.
- Q: What must happen before broadcasting a PSBT that arrived already fully signed, given that it never passes through signing? → A: The same acknowledgment, spending limit check, warnings, and PIN or biometric check as signing. This is the one exception to broadcast needing only its own confirmation.
- Q: Can a watch-only wallet broadcast an already signed PSBT? → A: Not in this version. It can review only.
- Q: What should the first release of PSBT signing include beyond review, refuse, sign and export? → A: Stories 3 and 5 as well, so it includes broadcast and PSBTs without input data. Story 4 comes later.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review and sign a PSBT, then export it (Priority: P1)

A wallet holder has an unsigned transaction from another tool. They paste it or upload the file, see in plain terms what it will do, confirm, and get back a signed PSBT to return to that tool.

**Why this priority**: This is the core of the feature. It is useful alone, because the tool that created the PSBT can finalize and broadcast it.

**Independent Test**: Load a PSBT that spends one of the wallet's coins, review it, sign it, export it, and confirm with a separate tool that the exported PSBT holds a valid signature for that input.

**Acceptance Scenarios**:

1. **Given** an unlocked wallet with spending keys, **When** the user pastes a valid PSBT in text form, **Then** the review shows each output's address and amount, which outputs return to this wallet, the fee, the total leaving this wallet, and which inputs the wallet will sign.
2. **Given** an unlocked wallet with spending keys, **When** the user uploads a valid `.psbt` file, **Then** the same review appears as for pasted text.
3. **Given** the review is showing, **When** the user confirms and re-enters their PIN (or passes biometrics, if enabled), **Then** the wallet signs the inputs that belong to it and offers the result to copy or download.
4. **Given** the review is showing, **When** the user cancels or leaves the page, **Then** nothing is signed and the PSBT is discarded.
5. **Given** a PSBT has been signed and exported, **When** the user looks at their balance and history, **Then** nothing has changed, because nothing was broadcast.
6. **Given** a PSBT whose inputs are all this wallet's and that pays an address the user has never used, **When** the user goes to sign, **Then** they see the same warning an ordinary send to that address would show.

---

### User Story 2 - Refuse PSBTs that are unsafe or misleading (Priority: P1)

A wallet holder is handed a PSBT crafted to trick them, for example one that understates what an input is worth so the fee looks small, or one that asks for a signature that would let someone change the outputs later. The wallet refuses it and explains why, before any signature exists.

**Why this priority**: Signing cannot be undone once the result leaves the device. Shipping Story 1 without these checks would put funds at risk, so the two ship together.

**Independent Test**: Load each PSBT from a prepared set of bad cases and confirm that each is refused with its specific reason and that no signature is produced.

**Acceptance Scenarios**:

1. **Given** a PSBT states an amount or script for one of this wallet's inputs that differs from the chain, **When** it is loaded, **Then** the wallet refuses to sign and names the mismatched input.
2. **Given** a PSBT where any input requests a signature type other than the standard "sign everything" type, **When** it is loaded, **Then** the wallet refuses it rather than signing with a different type.
3. **Given** a PSBT with no inputs belonging to this wallet, **When** it is loaded, **Then** the wallet shows a clear "nothing to sign" message and offers no sign action.
4. **Given** a PSBT with a Taproot or DigiDollar input, **When** it is loaded, **Then** the wallet refuses it and says this type is not supported.
5. **Given** an input of this wallet that the chain reports as already spent or unknown, **When** the PSBT is loaded, **Then** the wallet refuses to sign and names that input.
6. **Given** the wallet cannot reach chain data, **When** a PSBT is loaded, **Then** the review is shown labeled as unverified, signing is unavailable, and the user can retry.
7. **Given** the text or file is not a valid PSBT, **When** it is loaded, **Then** the wallet shows a plain error and does not continue.

---

### User Story 3 - Finalize and broadcast a complete PSBT (Priority: P2)

A wallet holder signs a PSBT where this wallet supplies every signature still needed. They can send the transaction directly from the wallet, without returning to the tool that created the PSBT.

**Why this priority**: It saves a step for the single-signer case, but exporting from Story 1 already gets the transaction sent.

**Independent Test**: Sign a PSBT whose inputs all belong to this wallet, broadcast it on regtest, and confirm the node accepts the transaction and it appears in history.

**Acceptance Scenarios**:

1. **Given** signing has left every input fully signed, **When** the result is shown, **Then** the user can broadcast or export it.
2. **Given** the user chooses to broadcast, **When** they confirm, **Then** the wallet checks the final transaction, sends it, shows the transaction ID, and records it in history.
3. **Given** the network rejects the transaction, **When** broadcast fails, **Then** the user sees the reason and can still export the signed PSBT.
4. **Given** a PSBT that arrives already fully signed and has at least one input of this wallet, **When** it is loaded, **Then** the user can review it and broadcast it without a new signature, after the same chain checks, acknowledgment, spending limit check, warnings, and PIN or biometric check as signing.
5. **Given** a PSBT that arrives already fully signed with no inputs of this wallet, **When** it is loaded, **Then** the wallet shows "nothing to sign" and does not offer broadcast.

---

### User Story 4 - Sign your part of a shared transaction (Priority: P2)

A wallet holder takes part in a transaction with inputs from other people. The wallet signs only what belongs to it and hands back a PSBT that is still incomplete, ready for the next signer.

**Why this priority**: It extends PSBT use to shared transactions. It relies on the review and signing from Story 1.

**Independent Test**: Load a PSBT with one input from this wallet and one from another key, sign it, and confirm only this wallet's input is signed and broadcast is not offered.

**Acceptance Scenarios**:

1. **Given** a PSBT with inputs from this wallet and others, **When** the review appears, **Then** each input is marked as this wallet's or someone else's, with the signing count shown.
2. **Given** the user signs that PSBT, **When** signing completes, **Then** only this wallet's inputs gain signatures, the result is marked incomplete, broadcast is not offered, and export is.
3. **Given** another participant's input amount cannot be checked on the chain, **When** the review appears, **Then** the fee is labeled as unverified, the total leaving this wallet remains shown as verified, and the user must acknowledge the unverified fee before signing.
4. **Given** another participant's input whose amounts all match the chain, **When** the review appears, **Then** the fee is labeled as verified and no unverified-fee acknowledgment is needed.
5. **Given** another participant's input whose stated amount disagrees with the chain, **When** the PSBT is loaded, **Then** the wallet refuses it and names that input.

---

### User Story 5 - Sign a PSBT that lacks input data (Priority: P3)

A wallet holder receives a minimal PSBT that lists inputs without their amounts. Where those inputs are SegWit coins on this wallet's own addresses, the wallet fills in the missing details from the chain and proceeds normally.

**Why this priority**: It widens compatibility with tools that produce minimal PSBTs, but most PSBTs already carry this data.

**Independent Test**: Load a PSBT without input data that spends this wallet's SegWit coin, and confirm the review shows the right amounts and signing succeeds.

**Acceptance Scenarios**:

1. **Given** a PSBT without input data that spends a SegWit coin on this wallet's address, **When** it is loaded, **Then** the wallet fills in the data from the chain and shows the normal review.
2. **Given** a legacy input of this wallet that lacks its previous transaction, **When** the PSBT is loaded, **Then** the wallet refuses it and explains that the creating tool must include the previous transaction.

---

### Edge Cases

- **Watch-only wallet**: signing and broadcast are unavailable, and the page says why. Review is still allowed.
- **Locked wallet**: the user must unlock before loading or signing, as with other wallet actions.
- **Wrong network**: the same key produces identical scripts across DigiByte networks, so a script match alone is not proof. A mainnet PSBT loaded in a testnet session (or the reverse) is refused because its inputs are not found on the active network's chain.
- **Coin outside the search range**: a coin of this wallet on an address the wallet doesn't currently scan is treated as someone else's, because the wallet can't tell it apart from another participant's coin. So whenever any input is treated as someone else's, the review notes that the wallet only recognizes addresses within its normal range.
- **Input with no data that can't be looked up**: its owner can't be determined, so signing is blocked and the user can retry.
- **Output to this wallet outside the range**: it is shown as an external payment, never as change. This errs on the side of warning the user.
- **Script-based input containing this wallet's key** (for example a multisig input): it is not signed here and is treated as someone else's. Multisig stays in the multisig flow.
- **Wrapped SegWit input (P2SH-P2WPKH)**: it is treated as someone else's in this version, because the wallet does not use this address type outside recovery.
- **Input already signed by this wallet**: it is not signed again, and the review marks it as already signed. If that leaves nothing new to sign, the user is told so.
- **Amounts that do not add up**: a PSBT whose outputs exceed its inputs, or with no inputs or no outputs, or the same input listed twice, is refused as invalid.
- **Unusually high fee**: the fee is flagged prominently and the user must explicitly acknowledge it before signing.
- **Unconfirmed input**: this is allowed if the chain reports it, and is labeled as unconfirmed.
- **Future lock time**: the lock is shown in the review, and a failed broadcast still leaves the signed PSBT available for export.
- **Output the wallet cannot show as an address**: data outputs are labeled as data, with text shown if readable. Other scripts are labeled as a non-standard output with the raw amount.
- **Oversized input**: text or a file above the size limit is refused before it is processed.
- **Stale PSBT**: a coin spent between review and signing is caught by re-checking the chain at the moment of signing.
- **Wallet gains overall**: in a shared transaction where this wallet's outputs exceed its inputs, the review shows the net amount as received rather than a negative total leaving.
- **Wallet locks during review**: the loaded PSBT is discarded, and the user loads it again after unlocking.
- **Direct link in Simple mode**: the page explains that the feature needs Advanced mode and offers no way to load a PSBT.
- **Repeated wrong PIN**: signing is throttled with the same increasing delay as the unlock screen.

## Requirements *(mandatory)*

### Functional Requirements

**Loading**

- **FR-001**: Users MUST be able to load a PSBT by pasting its text (base64) form or uploading a binary `.psbt` file.
- **FR-002**: System MUST refuse input that is not a valid PSBT, is structurally invalid, or exceeds 1 MB, with a plain-language reason.
- **FR-003**: System MUST make signing available only for an unlocked wallet with spending keys, and MUST explain the restriction for a watch-only wallet.
- **FR-030**: System MUST list this feature in Settings, near Recovery Tools, only when the wallet is in Advanced mode, and MUST NOT list it in Simple mode.

**Ownership**

- **FR-004**: System MUST decide whether an input belongs to the wallet solely by comparing its script with the addresses the wallet currently scans. For HD wallets, that is native SegWit across every scanned receiving and change index (always at least receiving 0-19 and change 0-9, growing as addresses are used) and legacy across receiving 0-19 and change 0-9. For WIF wallets, it is the imported key's addresses.
- **FR-005**: System MUST ignore ownership hints and key paths claimed inside the PSBT when deciding what to sign or what counts as change.
- **FR-006**: System MUST sign only inputs identified as the wallet's under FR-004, and MUST NOT add a signature to any other input, including a script-based input containing one of the wallet's keys.

**Safety checks**

- **FR-007**: Before signing, System MUST confirm from chain data on the active network that each wallet input exists unspent with the same amount and script the PSBT states, and MUST refuse to sign and name the input on any mismatch.
- **FR-008**: System MUST repeat the check in FR-007 at the moment of signing, not only when the PSBT is loaded.
- **FR-009**: System MUST refuse a PSBT if any input requests a signature type other than the standard "sign everything" type, and MUST NOT substitute a different type.
- **FR-010**: System MUST refuse a PSBT with any Taproot input or that it recognizes as a DigiDollar transaction, stating that the type is not supported.
- **FR-011**: System MUST refuse a PSBT with a wallet legacy input that lacks its previous transaction, or whose supplied previous transaction does not match the input.
- **FR-012**: System MUST show a clear "nothing to sign" message and offer no sign action when no input belongs to the wallet or all of its inputs are already signed.
- **FR-032**: System MUST attempt to check the stated amount of every input belonging to someone else against chain data, and MUST refuse the PSBT and name the input if the chain shows a different amount. When the PSBT states an amount for that input, a lookup that fails or finds nothing MUST NOT block signing by itself; it leaves the fee unverified under FR-016.
- **FR-035**: System MUST block signing and offer a retry when an input has no amount or script in the PSBT and its chain lookup fails or finds nothing, because its owner can't be determined.
- **FR-013**: System MUST refuse to sign whenever chain data for any of this wallet's inputs is unavailable. A failed lookup of another participant's input is handled under FR-032. System MUST keep these failures confined to this feature, so that balance, receive, send, and history keep working.

**Review**

- **FR-014**: System MUST show, before signing: every output with its full address and amount, whether it returns to this wallet, the number of inputs to be signed and the number belonging to others, the fee, and the total leaving this wallet.
- **FR-015**: System MUST calculate the total leaving this wallet from chain-verified input amounts, less outputs returning to this wallet, and MUST show it as the primary figure.
- **FR-016**: System MUST label the fee as verified only when every input amount, including those of other participants, was confirmed on the chain. Otherwise it MUST label the fee as unverified.
- **FR-017**: System MUST require explicit acknowledgment before signing when the fee is labeled unverified, or when the fee is more than 10% of the amount sent to outputs that do not return to this wallet. When every output returns to this wallet, the comparison is with the total input amount instead.
- **FR-018**: System MUST show any lock time set on the transaction, and label unconfirmed inputs and non-address outputs.
- **FR-031**: When every input belongs to this wallet, System MUST apply the same unfamiliar-address and large-amount warnings as an ordinary send to all outputs that do not return to this wallet. When any input belongs to someone else, System MUST instead show a single note that outputs were not checked. If the warning check itself cannot run, it MUST NOT block signing, as with an ordinary send.

**Signing and result**

- **FR-019**: System MUST require the user to re-enter the wallet PIN, or pass biometrics if enabled, for every signing, even when the wallet is already unlocked. A failed or cancelled check MUST produce no signature, and repeated wrong PINs MUST be throttled as on the unlock screen.
- **FR-033**: When the user has spending limits set, System MUST check the total leaving this wallet against them before signing, and MUST block signing if the limit would be exceeded, as an ordinary send does. When the wallet gains overall, the amount checked is zero.
- **FR-020**: System MUST perform all signing on the user's device, and MUST NOT send keys, seed material, or an unsigned PSBT to any server operated by this project as part of signing.
- **FR-021**: Users MUST be able to copy the signed PSBT as text and download it as a `.psbt` file.
- **FR-022**: System MUST report after signing whether the PSBT is complete, and how many inputs still need signatures.
- **FR-023**: System MUST fill in missing input data for SegWit inputs on the wallet's own addresses from chain data, and include that data in the exported PSBT.

**Broadcast**

- **FR-024**: System MUST offer broadcast only when the wallet has spending keys, at least one input belongs to this wallet, every input is fully signed, and the final transaction passes validation.
- **FR-025**: System MUST require a separate confirmation to broadcast, showing the same outputs and fee.
- **FR-026**: System MUST record a broadcast transaction in history and show its transaction ID.
- **FR-027**: System MUST keep the signed PSBT available for export after a failed broadcast, and show the failure reason.
- **FR-034**: Before broadcasting a PSBT that arrived already fully signed, System MUST apply the same acknowledgment (FR-017), spending limit check (FR-033), warnings (FR-031), and PIN or biometric check (FR-019) as signing, because that path never passes through signing.

**Privacy and existing behavior**

- **FR-028**: System MUST NOT store the PSBT once the user leaves the page, and MUST NOT write PSBT contents to logs, telemetry, or error reports.
- **FR-029**: System MUST leave the existing multisig PSBT flow unchanged.

### Key Entities

- **Loaded PSBT**: an untrusted transaction proposal, held in memory only. It has inputs, outputs, and any signatures already present.
- **Input Assessment**: for each input, whether it belongs to this wallet, its type, its amount with source (chain-verified or stated by the PSBT), its signature state, and any reason for refusal.
- **Output Assessment**: for each output, its destination or label, its amount, and whether it returns to this wallet.
- **Review Summary**: total leaving this wallet, fee with verification label, inputs to sign, inputs belonging to others, warnings, and whether signing is allowed.
- **Signing Result**: the signed PSBT, whether it is complete, and inputs still needing signatures.
- **Broadcast Result**: the transaction ID on success, or the reason on failure.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from having a PSBT to exporting a signed result in under 2 minutes.
- **SC-002**: 100% of PSBTs in the prepared bad-case set (false amount on a wallet input or another participant's input, false script, spent input, unsupported signature type, Taproot input, wrong network, missing previous transaction, malformed data) are refused with a specific reason and yield no signature.
- **SC-003**: In every test case with a chain-verifiable wallet input, the total leaving the wallet shown in review matches the amount the signed transaction actually takes from the wallet, to the exact unit.
- **SC-004**: In mixed-ownership tests, 0 signatures are added to inputs that do not belong to the wallet.
- **SC-005**: Before release, a PSBT created by DigiByte Core on regtest is signed in the wallet, returned to DigiByte Core, finalized there, and accepted by the regtest node. A second such PSBT, signed and broadcast from the wallet, is also accepted.
- **SC-006**: In a usability check, at least 9 of 10 test users correctly identify the amount leaving their wallet and the fee from the review alone.
- **SC-007**: With chain data unavailable, signing is blocked and the wallet's balance, receive, send, and history surfaces behave as they do without this feature.
- **SC-008**: All existing multisig tests pass unchanged.
- **SC-009**: The review appears within 5 seconds of loading a typical PSBT on a working connection.

## Assumptions

- **Users**: wallet holders using another tool that produces PSBTs. This is an advanced action, so it is reached from Settings in Advanced mode rather than the main send flow.
- **Trust model**: the PSBT and its source are untrusted, while chain data from the wallet's configured sources is trusted. Chain data sources could still misreport, which is the same exposure ordinary sends have today.
- **Signature type refusal applies to all inputs**: a non-standard request on another participant's input also blocks the PSBT. This is deliberately strict for the first version and may block some legitimate shared transactions.
- **Refusal is all-or-nothing**: a PSBT with any refused condition is not partly signed. The exception is inputs that simply belong to others, which are expected.
- **Fee warning threshold**: 10% is a starting default, chosen to catch obvious overpayment without troubling routine transactions.
- **Spending limits**: limits count transactions in the wallet's history, so a PSBT that is exported and broadcast elsewhere counts only once it appears there. A limit is still checked before every signing.
- **First release scope**: Stories 1, 2, 3 and 5. Until Story 4 ships, a PSBT with another participant's input still signs only this wallet's inputs and stays incomplete, but its fee is always labeled unverified and needs the acknowledgment, and the review shows the note that outputs were not checked. Other participants' amounts aren't compared with the chain until then, so FR-032 and the other-participant case in SC-002 apply from Story 4.
- **Release test network**: the public testnet explorer API isn't available, so the DigiByte Core round trip runs on regtest with a local node. This proves PSBT compatibility, not behavior against public explorers, which the mainnet review checks cover.
- **Size limit**: 1 MB comfortably exceeds any realistic PSBT for this feature.
- **Authorization**: signing asks for more than an ordinary send, which needs only a tap plus optional biometrics. No new credential is introduced, because the existing PIN and biometrics are reused.
- **Out of scope for this version**: signing multisig inputs through this flow, Taproot and DigiDollar inputs, wrapped SegWit inputs, recovery-path keys (BIP44 and BIP49), addresses beyond the current search range, fetching previous transactions for legacy inputs, signature types other than "sign everything", creating PSBTs, QR code transfer, and hardware signers.
- **Privacy tradeoff**: checking other participants' inputs tells the chain data source which other coins this wallet is asking about. This is accepted so that fee warnings stay meaningful on shared transactions.
- **Dependency**: chain data sources must report unspent coins per address, including amount and script, as they do for ordinary sends, and must be able to report the amount of a given transaction's output, which research R4 confirmed by reading the code of the explorer and node sources. It has not been tested against live sources. Refusing DigiDollar transactions relies on the wallet's existing ability to recognize their transaction marker.
- **Scanned range**: the wallet now discovers used addresses and saves its indexes, so its own change and receive coins normally fall inside the scanned range. If that discovery can't finish, ownership uses the saved range, and coins beyond it are treated as someone else's.
