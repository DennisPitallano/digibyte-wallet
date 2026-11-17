# Quickstart: Validating Multisig UX Polish

## Prerequisites
- Two or three browser profiles (or devices) holding keys of the same 2-of-3 multisig on the test network.
- The API (for rooms) and the Web app running; see the repo README for the usual start commands.

## Automated

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~MultisigUx"
```

Expect all state, headline, presence, attention, and scheme tests to pass.

## Manual walkthrough
1. **Signing room (US1)**: Open a room from a pending payment on A and join from B. Expect cards with initials, "You", and Online badges, and on B one headline "Your signature is needed" with a single "Review & sign". Close B's tab: A shows B Offline in about 10 s (reopen within 10 s: no flip). Sign on B: the card turns Signed within 5 s, B's primary button is gone, and reaching the threshold shows "Ready to broadcast" with Broadcast.
2. **Decline path**: With 3 co-signers, decline twice. The headline says the payment can't be completed and the primary action is Go to Pending.
3. **Payment summary (US2)**: Compare the Pending item, the room, and the offline sign screen. Same fields, same order, full address copies, Details collapsed, one-line cue, and the confirmation restates amount and recipient.
4. **Creation (US3)**: Create a 2-of-3 wallet online and by manual keys. A step indicator is present, each step has one instruction and one primary action, the scheme text updates ("Any 2 of 3 ..."), and the room shows Joined or Ready cards, "Not joined yet" slots, and a waiting message.
5. **Lists (US4)**: With payments in each state, confirm the same label and icon in Pending, Activity, and wallet cards. Wallet cards show counts and the multisig home shows the total (also check a locked wallet).
6. **Calm and accessible (US5)**: Phone width (375 px), light and dark themes, reduced motion on, keyboard only, screen reader announcing status changes. Empty Pending and Activity show one sentence and one action.
7. **Regression**: Offline share/import still works end to end, and a room ending still saves collected signatures.

## Done when
All steps pass and success criteria SC-006 to SC-008 hold. SC-001 to SC-005 and SC-009 are assessed in a short moderated session.
