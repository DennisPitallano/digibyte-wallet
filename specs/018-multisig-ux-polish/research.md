# Research: Multisig UX Polish

No NEEDS CLARIFICATION remained after clarification. Decisions below come from reading the existing code and specs 016/017.

## 1. Where do statuses come from?
- **Decision**: Use only data already on the wire or in storage. Signing room: `SigningParticipantView.Online`, `.Action` (None/Signed/Declined), `SigningRoomView.SignedKeys`, `.Unreachable`, `.Status`. Creation room: `MultisigParticipant.IsReady`, the join list, `Status`. Lists: `PendingMultisigTransaction.Status`, `.SignedBy`, `.RequiredSignatures`.
- **Rationale**: FR-018 and clarification 1 forbid new shared data. "Reviewing" is dropped.
- **Alternatives**: Add a "viewing" flag to the hub (rejected: changes the contract and what is shared).

## 2. Online/offline hold of about 10 seconds
- **Decision**: A client-side debouncer. A participant flipping to offline is displayed as offline only after staying offline for about 10 s; coming back is shown at once.
- **Rationale**: Absorbs reconnect blips with zero server change; the hub already pushes presence updates.
- **Alternatives**: Server-side grace (rejected: server change, affects the 017 contract and tests).

## 3. Shared vocabulary
- **Decision**: One state set (NeedsYourSignature, YouSigned, WaitingForOthers, ReadyToBroadcast, LiveRoom, Sent, CancelledOrExpired, Failed) mapped once to label, icon and primary action; every screen calls it.
- **Rationale**: SC-006 requires identical wording everywhere; one mapping makes that testable.
- **Alternatives**: Per-page strings (current state; rejected: drift).

## 3b. Attention counts
- **Decision**: Compute from saved pending items per wallet (status AwaitingSignatures, local key not in `SignedBy`, wallet has a local co-signer key), read the same way the Pending list reads them.
- **Open check for tasks**: confirm pending items are readable for a locked wallet. If not, show the count only for unlocked wallets and add a "locked" hint (documented fallback; the clarified intent is to work while locked).

## 4. Icons and avatars
- **Decision**: Inline SVG icons in the Heroicons style already used across pages. Avatars are initials on a color derived from the public key (decoration only, never the sole signal). A 4-character key fingerprint disambiguates equal names.
- **Alternatives**: Icon font or a new package (rejected: new dependency and PWA cache cost).

## 5. Motion and accessibility
- **Decision**: Reuse the existing `slide-up` and `press-effect` classes; any new motion sits behind `prefers-reduced-motion`. Status changes go in an `aria-live="polite"` region; every status has icon plus text.

## 6. Testing approach
- **Decision**: Test-first xUnit on the pure services (Principle III); manual quickstart for visuals. Component-test tooling is out of scope.
