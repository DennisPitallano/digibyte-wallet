# Feature Specification: Baku Mascot — Guide & Guardian

**Feature Branch**: `002-baku-mascot-guide`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "I added a mascot for this app, let us call him Baku. Let us use it in the app as guide and guardian."

## Clarifications

### Session 2026-10-19

- Q: When should Baku treat a send as "large" and show the extra warning? → A: When the send is 50% or more of the current balance
- Q: Should Baku's messages be translated into the app's other supported languages at launch? → A: English only for now; other languages in a follow-up
- Q: After a user dismisses a non-critical tip, should it stay hidden or return? → A: Hidden permanently; a "reset tips" action in Settings restores all tips
- Q: Should guardian warnings also cover Multisig and P2P Marketplace in this release? → A: Core screens plus a basic verify-counterparty/scam warning on P2P Marketplace; Multisig deferred
- Q: Should Baku appear on the unlock screen for returning users? → A: Yes, a small greeting pose that hides once the user starts typing their password
- Q: Should the new-address warning apply to addresses saved in Contacts? → A: No; skip it for saved contacts and previously-used addresses, warn for all others
- Q: Should the large-send and new-address warnings also cover DigiDollar sends? → A: Yes; DGB and DigiDollar sends get the same warnings
- Q: Should Baku have any motion? → A: Subtle entrance transition plus a short celebration effect, both disabled under reduced-motion

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Baku welcomes and guides new users (Priority: P1)

A first-time visitor lands on the wallet's welcome and onboarding flow (create wallet, recover wallet, back up seed phrase). Baku appears as a friendly guide: greeting them, explaining each step in short, plain language, and reacting to progress (e.g., celebrating when the wallet is created and the backup is confirmed).

**Why this priority**: Onboarding is where users are most uncertain and where mistakes (lost seed phrases) are costly. A consistent, friendly guide delivers the most value and gives the mascot its identity.

**Independent Test**: Start with no wallet, walk through create-wallet and backup. Baku appears on each step with an appropriate expression and a short guidance message, and every step remains completable with Baku dismissed or hidden.

**Acceptance Scenarios**:

1. **Given** a visitor with no wallet, **When** they open the welcome screen, **Then** Baku appears in a greeting pose with a short welcome message.
2. **Given** a user on the seed-backup step, **When** the step is displayed, **Then** Baku appears with a message explaining why the phrase must be written down and kept private.
3. **Given** a user who has just confirmed their backup, **When** the success state shows, **Then** Baku appears in a celebrating pose.

---

### User Story 2 - Baku guards against risky actions (Priority: P1)

When a user is about to do something risky — sending 50% or more of the balance, sending to a new address, revealing or exporting the seed phrase, or opening the P2P Marketplace — Baku appears in a protective pose with a clear, calm warning explaining the risk and what to check. Baku never permanently blocks a legitimate action the user confirms; he informs and asks for deliberate confirmation.

**Why this priority**: "Guardian" is half of the requested role, and it directly supports the wallet's security reputation.

**Independent Test**: Trigger each defined risk situation (e.g., reveal seed phrase, send to first-time address) and verify Baku's warning appears with relevant text and the user can still proceed after an explicit confirmation.

**Acceptance Scenarios**:

1. **Given** a user about to reveal their seed phrase, **When** they request it, **Then** Baku appears in a protective pose with a warning that nobody legitimate will ever ask for it.
2. **Given** a user sending to an address they have never used before, **When** they review the transaction, **Then** Baku prompts them to double-check the address.
3. **Given** a user who acknowledges a Baku warning, **When** they confirm, **Then** the action proceeds normally.

---

### User Story 3 - Baku gives contextual tips and feedback (Priority: P2)

Across the main wallet screens (Home, Send, Receive, DigiDollar, Settings), Baku provides occasional contextual tips, and the Help Center gets its own Baku introduction and no-results state, empty-state companionship (e.g., "no transactions yet"), error empathy (thinking pose when something fails), and success feedback (approve/celebrate after a sent transaction). Tips are brief and can be dismissed permanently.

**Why this priority**: Adds delight and clarity beyond onboarding and warnings but is not required for the core guide/guardian value.

**Independent Test**: Visit each listed screen with a funded and an empty wallet; Baku's pose and message match the screen state, and dismissing tips prevents them from reappearing.

**Acceptance Scenarios**:

1. **Given** a wallet with no transactions, **When** the user opens Home, **Then** Baku appears with an encouraging empty-state message.
2. **Given** a failed or rejected action, **When** the error is shown, **Then** Baku appears in a thinking/confused pose beside a helpful, non-technical explanation.
3. **Given** a user who dismisses tips, **When** they return later, **Then** non-critical tips no longer appear.
4. **Given** a user opening the Help Center, **When** the page loads, **Then** Baku appears at the top in a welcoming pose offering help, and the help topics stay fully usable below him.
5. **Given** a Help Center search with no matching topics, **When** the empty result is shown, **Then** Baku appears in a thinking pose suggesting different search words.

---

### User Story 4 - User controls Baku's presence (Priority: P3)

In Settings the user can choose how much of Baku they see: Full (guide, tips, and guardian), Guardian only (warnings only), or Off (no tips or guidance). Warnings and critical security messages remain visible in text form in every mode, even when the mascot imagery is off.

**Why this priority**: Respects users who prefer a minimal interface, but is secondary to the core experience.

**Independent Test**: Switch each preference level and verify the corresponding appearances show or hide, and that the choice persists across sessions.

**Acceptance Scenarios**:

1. **Given** the "guardian only" setting, **When** the user visits Home with an empty wallet, **Then** no tip appears, but a risky action still shows Baku's warning.
2. **Given** Baku is turned off, **When** a risky action occurs, **Then** the warning text still appears without the mascot image.

---

### Edge Cases

- Mascot image fails to load or the user is offline: guidance text is still shown and layout does not break.
- Small phone screens: Baku scales down or collapses so he never covers form fields, balances, or action buttons.
- Screen-reader users: guidance messages are announced as text; mascot imagery is decorative or has a meaningful description.
- Users who prefer reduced motion: any animation is disabled or minimized.
- Multiple warnings at once: only one Baku message is shown at a time, prioritizing the most severe.
- Localization: Baku messages are English only in this release, even if the rest of the app is shown in another language; messages are kept separate from layout so translations can be added later.
- Baku must never appear to speak for an operator or imitate a support agent requesting secrets.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST present "Baku" as a named character with a consistent set of poses drawn from the supplied mascot artwork (e.g., greeting, protective, celebrating, thinking, guard, wallet, welcome, proud, grateful).
- **FR-002**: The app MUST display Baku with a greeting message on the welcome screen for users without a wallet.
- **FR-002a**: The app MUST show a small Baku greeting on the unlock screen for returning users, hidden once the user begins entering their password, and never obstructing the password field.
- **FR-003**: The app MUST show Baku with step-specific guidance in the create-wallet, recover-wallet, and seed-backup flows.
- **FR-004**: The app MUST show Baku in a celebratory pose upon successful wallet creation, backup confirmation, and completed transactions.
- **FR-005**: The app MUST show a protective Baku warning before the seed phrase is revealed or exported.
- **FR-006**: The app MUST show a Baku warning on both DGB and DigiDollar sends when sending to an address that is neither a saved contact nor one the user has sent to before, and when a send is 50% or more of the current balance (measured on the amount sent, excluding the network fee; sending the maximum counts as 100%). When both warnings apply to one send, they are shown together in a single confirmation listing each reason.
- **FR-006a**: The app MUST show a Baku warning on the P2P Marketplace reminding users to verify the counterparty and never share secrets or pay outside the trade flow.
- **FR-007**: Baku warnings MUST require deliberate user confirmation but MUST NOT permanently block a legitimate action.
- **FR-008**: The app MUST show contextual tips, empty-state messages, and error-state messages with appropriate Baku poses on the main wallet screens, including a Baku introduction and a no-results state on the Help Center that leave its topics and search fully usable.
- **FR-009**: Users MUST be able to dismiss tips and have the dismissal remembered on their device, and MUST be able to restore all dismissed tips with a "reset tips" action in Settings.
- **FR-010**: Users MUST be able to choose Baku's presence level (Full, Guardian only, Off — where Off hides Info and Tip appearances only) in Settings, persisted on their device.
- **FR-011**: Warning and Critical messages MUST remain visible as text regardless of Baku's presence setting or image-loading failure.
- **FR-012**: Baku imagery MUST NOT obscure balances, addresses, amounts, or primary action controls on any screen size.
- **FR-013**: Baku messages MUST be accessible: readable by assistive technology, with sufficient contrast, and respecting reduced-motion preferences. Baku MAY use a subtle entrance transition and a short celebration effect on success, with no continuous idle animation, and all motion MUST be disabled when the user prefers reduced motion.
- **FR-014**: Baku content MUST NOT collect, display, or transmit seed phrases, passwords, or private keys, and no mascot feature may add behavior tracking beyond local, on-device preferences.
- **FR-015**: Mascot artwork MUST be provided as clean, background-transparent images per pose, sized for fast loading.
- **FR-016**: Only one Baku message MUST be shown at a time per screen, prioritizing security warnings over tips.

### Key Entities

- **Baku Pose**: A named expression of the mascot (e.g., greeting, protective, celebrating) with its image and the situations it fits.
- **Baku Message**: A short guidance, tip, warning, or feedback text tied to a screen/situation, a severity (info, tip, warning, critical), and a pose.
- **Baku Preference**: The user's device-local choice of presence level and the set of dismissed tips.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of onboarding steps (welcome, create, recover, backup) display Baku guidance, and every step is completable with Baku hidden.
- **SC-002**: 100% of defined risky situations show a warning that remains visible as text even with mascot imagery turned off.
- **SC-003**: In user testing, at least 90% of first-time users complete wallet creation and seed backup without outside help.
- **SC-004**: At least 80% of test users correctly describe Baku's role as helper/protector after a short session.
- **SC-005**: Adding the mascot increases initial page load time by no more than 5% on a typical mobile connection.
- **SC-006**: Baku never covers balances, addresses, amounts, or primary buttons on phone, tablet, and desktop widths.
- **SC-007**: Users can change or turn off Baku's presence in under 15 seconds from Settings, and the choice persists after reload.

## Assumptions

- The supplied artwork is three green-background pose sheets; individual transparent images per pose will be prepared as part of this feature.
- Baku messages are English only at launch.
- Baku is a mostly static character (images with only a subtle entrance transition and a short celebration effect); no voice, chat, or AI-generated responses in this release.
- All Baku text is predefined by the project, not generated or sent to any external service.
- Preferences and dismissed tips are stored locally on the user's device only, consistent with the project's self-custody principles.
- The 50%-of-balance threshold for large-send warnings is fixed in this release and not user-configurable.
- Initial scope covers the core screens plus a basic P2P Marketplace scam warning; Multisig and other advanced pages are out of scope and may follow.
- The name "Baku" is final for user-facing copy.
