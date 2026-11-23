# Quickstart: Checking the Multisig Wallet Experience

A walkthrough that proves the feature works. Details of each screen are in [contracts/ui-contracts.md](contracts/ui-contracts.md); rules are in [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK, and the repository restored.
- Testnet mode, with at least two personal wallets on the device and a 2-of-3 shared wallet that has a small balance. Three browser profiles (or devices) for the room checks.

## Automated checks

```powershell
dotnet build DigiByte.slnx
```

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Multisig|FullyQualifiedName~ShellContext|FullyQualifiedName~ScanRouter"
```

Expected: build succeeds; new tests for shell context, counts, grouping, card actions, card status, ordering, shortcut, room stage, invite placement, signer order, countdown and scan routing pass, and all existing multisig tests still pass.

## Run the app

Start the `DigiByte.Web` dev server from `.claude/launch.json` (preview) and view at phone width (375 px), in light and dark themes.

## Walkthrough

### 1. Context and menu (Story 1)

1. More → Multisig → open a shared wallet. Expect: context line "*name* · 2-of-3 · in *wallet*"; menu reads Overview, Activity, Scan, Pending, More with the name tab above it; dashboard shows only Send and Receive tiles.
2. Tap Pending, Activity, Overview. Expect: each stays inside the shared wallet; the current tab is marked.
3. Tap the context line (or the name tab) → "Back to *wallet*". Expect: personal home, personal menu, no shared-wallet traces.
4. Open `/multisig`, `/multisig/create`, `/multisig/import`. Expect: personal menu, More current.
5. Inside the shared wallet, tap Scan and scan a DigiByte address QR. Expect: the shared wallet's Send opens with the recipient filled in. Scan a Digi-ID code. Expect: today's behaviour with a notice that you are leaving the shared wallet.
6. Switch the active personal wallet while inside a shared wallet. Expect: land on that wallet's home.

### 2. Pending (Story 2)

1. Create a payment and do not sign on a second device; import it there. Expect on the second device: group "Needs you" first, card with one "Review & sign" button visible without scrolling.
2. Scroll the list part-way, then tap a card. Expect: the payment's own screen with full summary, signers, main button, and below it Sign together online, Share with co-signers, Add a signed copy, Cancel. Go Back. Expect: the list at the place you left it.
3. Sign. Expect: a brief confirmation message, the list unchanged in position, card now under "Waiting for others" with "Get signatures".
4. Reach the threshold. Expect: card "Review & broadcast"; the slide control exists only on the payment's screen. Slide. Expect: confirmation with transaction ID and a link to Activity; the payment is gone from Pending.
5. Cancel a payment. Expect: it appears under a folded "Cancelled" group; "Clear all cancelled" is on the list.
6. Open Pending with a payment whose coins were already spent (spend the same coins from another device first). Expect: before Pending is opened, the payment is still counted on More, Home and the Pending tab; once Pending has checked, it sits under "Can't go through" with one "Review & cancel" button, it is no longer counted anywhere, the wallet's card on the list no longer says it needs your signature, and a live payment beside it stays under "Needs you" and stays counted.
7. Tap "Add a proposal". Expect: Scan, Paste, Choose file, Join a room with a code in one sheet.
8. Empty list. Expect: one explanation and two actions.

### 3. Signing room (Story 3)

1. From a payment's screen choose Sign together online and open a room. Expect: no bottom menu; room bar with code, time left and connection; tracker on Invite; invite block directly under the headline; main button pinned to the bottom.
2. Join from a second profile by link. Expect: join screen (with menu) shows which wallet you join as; the six boxes accept a pasted code and a pasted invite link (its code is taken, not the start of the link), and a stray dash or space typed along the way does not use up a box. After joining: invite folded to one row, tracker moves to Sign once two are present.
3. Scroll the room. Expect: main button always visible. Sign on both. Expect: tracker on Broadcast, signer list ordered you, signed, online, offline, "2 of 2 signatures".
4. Open the room menu. Expect: Leave and (host only) Close room with their consequences; Close asks to confirm.
5. Broadcast. Expect: result screen with outcome, signatures kept, transaction ID with copy, one next button; leaving it brings the menu back. In a second room, have enough co-signers decline. Expect: the headline says it can't be completed and the button reads "Open payment", leading to that payment's screen where Cancel is offered.
6. Let a room run to under 5 minutes. Expect: time emphasised and announced once. Drop the connection. Expect: calm notice, main button disabled with a reason.

### 4. List (Story 4)

1. Open `/multisig` with several shared wallets. Expect: heading "Shared wallets in *wallet*"; cards appear within 1 second with name, scheme, co-signers and status (time it once with the browser's performance panel on a throttled mid-range phone profile and note the figure); balances fill in without moving cards; wallets that need you first.
2. Turn on hide balance on a dashboard, return. Expect: balances hidden on cards.
3. Go offline, reload the list. Expect: status lines still shown, balances "Unavailable", cards still open.
4. Check a never-backed-up wallet and a view-only one. Expect: "Not backed up" and "View only" with icon and text.
5. Rename from a card's more button. Expect: works within two taps.

### 5. Counts and return (Story 5)

1. With a payment waiting, go to the personal home. Expect: the same number on More, on the Multisig row inside More, and on the home Multisig shortcut.
2. Tap the home shortcut. Expect: the last-used shared wallet opens. Remove it and tap again. Expect: the list.
3. More → Multisig. Expect: always the list.
4. Sign the waiting payment. Expect: all counts drop without reloading.

### 6. Across everything

- Switch the app language to Spanish. Expect: Overview, Pending and count labels in Spanish; no raw keys.
- Reduced motion on: no sliding or pulsing in tracker, sticky button, toasts.
- Keyboard only: every control reachable; sheets trap focus and close with Esc.
- Screen reader: status changes, counts and the 5-minute warning are announced.
- Re-run the existing-actions checklist (sign, share by QR/copy/file, import signature by paste/scan/file, open/join/close/leave room, decline, broadcast, retry, cancel, restore cancelled, remove cancelled, clear all cancelled, rename, claim unassigned, drafts) and confirm each still completes (SC-012).
