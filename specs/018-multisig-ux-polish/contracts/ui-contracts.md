# UI Contracts: Multisig UX Polish

No API, hub, or storage contract changes. These are the component and service contracts the pages rely on.

## Services (`DigiByte.Wallet`)
- `PendingStateClassifier.Classify(pending, localKey, hasLiveRoom) -> MultisigState`
- `MultisigStateVocabulary.For(state) -> (Label, Icon, Tone, PrimaryAction)`: the single source for all screens.
- `SigningRoomHeadline.For(view, localKey, isWatchOnly) -> RoomHeadline`
- `SignerCards.ForSigningRoom(view, config, localKey, presence) -> IReadOnlyList<SignerCardModel>`
- `SignerCards.ForCreationRoom(room, localKey, totalSigners) -> IReadOnlyList<SignerCardModel>` (no presence; adds "Not joined yet" slots)
- `PresenceDebouncer.Observe(key, online, now)` and `.IsOnline(key, now)`; the clock is injected.
- `AttentionCounter.Count(pendingItems, localKeysByWallet) -> (perWallet, total)`
- `SchemeExplainer.Describe(required, total) -> string`

## Components (`DigiByte.Web/Components/Multisig`)
| Component | Parameters | Behavior |
|---|---|---|
| StatusBadge | State, or Status and Presence | icon plus text, `aria-label`, never color-only |
| SignerCard / SignerList | cards, viewer | one card per co-signer, truncates long names, announces changes in a polite live region |
| RoomHeadlineCard | RoomHeadline | one title, optional detail, exactly one primary button (or none) |
| PaymentSummary | summary, ShowCheckCue | fixed field order, copyable full address, collapsed Details, one-line cue |
| StepIndicator | steps, current | compact, text plus number, not color-only |
| AttentionBadge | count | hidden when 0 |
| ConnectionBanner | isReconnecting | "Reconnecting. Your signatures are saved."; disables only connection actions, with a reason |
| EmptyState | message, action | one sentence, one action |

## Behavioral rules
- A viewer who has signed is never offered Sign again (FR-004).
- At most one primary button per room view (FR-003).
- The same `MultisigState` renders the same label and icon on every screen (FR-010).
- Reduced motion: no animation when `prefers-reduced-motion: reduce`.
