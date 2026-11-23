# UI style layer

Shared classes for buttons, cards, text inputs, QR frames and motion. They live at the end of
`src/DigiByte.Web/wwwroot/css/app.css` and are shown, in every state and both themes, in the style
gallery at `/test-animations`.

**Status: defined, not rolled out.** No page uses these classes yet except the gallery. The
rollout order is at the end of this file.

## How it is built

- Plain CSS in `app.css`, self-hosted like everything else. No new files, fonts or scripts, and
  no change to the Content Security Policy.
- Every colour, shadow and radius is a `--ui-*` custom property, set once on `:root` (light) and
  once on `.dark`. The component rules have no per-theme copies, so a colour change is one line.
- The classes own colour, shape, depth and motion. Layout stays with Tailwind: add `w-full`,
  `flex-1`, `mt-3`, `p-4` and so on beside them. Do not add Tailwind colour, radius, padding or
  shadow utilities to a button or input that uses these classes: `app.css` loads after
  `tailwind.css`, so the component class wins and the utility is dead weight.

## Buttons

| Class | Use it for |
| --- | --- |
| `btn-primary` | The one main action on a screen or dialog: Send, Continue, Confirm, Create. At most one per view. |
| `btn-secondary` | The alternative next to a primary (Cancel, Back, Share), or a standalone action that is not the main one. |
| `btn-ghost` | Low-emphasis actions inside a card or row: "Show more", "Download QR", "Replay". No resting background. |
| `btn-danger` | Destructive, hard-to-undo actions: Delete wallet, Remove contact. Pair with a `btn-secondary` Cancel. |
| `btn-sm` | Modifier for any of the four. Compact rows and inline actions. |

Default height is 48px with 16px text; `btn-sm` is 44px with 14px text. 44px is the minimum touch
target, so there is no smaller size.

```razor
<button class="btn-primary w-full" @onclick="Send">Send DGB</button>
<button class="btn-secondary btn-sm" @onclick="Cancel">Cancel</button>
<a class="btn-ghost btn-sm" href="help">Learn more</a>
```

States:

- **Hover** (pointer devices only): primary, secondary and danger lift 1px and deepen their
  shadow; ghost gains a tinted background. Touch screens never get a stuck hover.
- **Pressed**: scales to 98% and swaps the drop shadow for an inset one.
- **Focus**: a 2px ring, offset 2px, shown for keyboard focus only (`:focus-visible`).
- **Disabled**: the `disabled` attribute, or `aria-disabled="true"` on a link. 45% opacity, no
  shadow, no pointer events.
- **Loading**: add `is-loading` or `aria-busy="true"` (prefer the attribute, it is announced).
  A spinner appears before the label and the button stops taking clicks. Change the label too
  ("Sending…") where that helps.

```razor
<button class="btn-primary w-full" aria-busy="@(sending ? "true" : null)" disabled="@(!canSend)">
    @(sending ? "Sending…" : "Send DGB")
</button>
```

`btn-danger` uses red-600 (`#dc2626`), not the red-500 most hand-written danger buttons use
today: white on red-500 is 3.8:1 and fails AA, white on red-600 is 4.8:1.

## Headings

| Class | Use it for |
| --- | --- |
| `page-title` | The one title at the top of a page (`h2`). `PageHeader` uses it. |
| `block-title` | The title of a card, dialog or section. `block-title-lg` for a dialog or a hero block, `block-title-sm` inside a dense card. |
| `section-label` | The small capitals above a group of rows ("Security", "Display"). |

Add only layout beside them (`mb-2`, `text-center`, `px-1`). Colour comes from the theme variables.

## Tabs and choices

```razor
<div class="segmented">
    <button type="button" class="segmented-item" aria-pressed="@(mode == A ? "true" : "false")" @onclick="…">Recovery phrase</button>
    <button type="button" class="segmented-item" aria-pressed="@(mode == B ? "true" : "false")" @onclick="…">Private key</button>
</div>

<button type="button" class="choice" aria-pressed="@(fee == Fast ? "true" : "false")" @onclick="…">Fast</button>
<button type="button" class="choice choice-pill" aria-pressed="…">Received</button>
```

- `segmented` switches between views of the same thing. The selected item is a raised tile.
- `choice` is one of several options (fee speed, PIN or passphrase); `choice-pill` is the rounded
  form for filters. Selected is filled.
- State is the text value of `aria-pressed` (`"true"` / `"false"`), which both styles the control
  and announces it. A bare boolean (`aria-pressed="@flag"`) renders without a value and will not
  be styled.

## Icon buttons

`icon-btn` is a 44px square holding one icon: a close button or a header action.
`icon-btn-primary` is the page's add action (the "+" beside a title). `icon-btn-sm` (36px) is only
for dense rows. Every icon button needs an `aria-label`.

`tools/header-tab-style-rollout.py` applied the headings, tabs, choices and icon buttons, and
reports what it skips: toggles and icon buttons with a colour or position of their own.

## Back button

`back-btn` is the one way back at the top of a page: a chevron and a label in a 44px-high pill.
`Components/Shared/PageHeader.razor` uses it, so most pages get it for free; pages with a header of
their own use the same class. `back-btn-icon` is the chevron alone, for a header that puts the
title beside it, and needs an `aria-label`.

```razor
<button type="button" class="back-btn" @onclick="GoBack">
    <svg aria-hidden="true" …chevron…></svg>
    <span>Back</span>
</button>
```

Negative margins keep the label where the old, smaller text link sat, so adopting it does not
shift a page. A "Back" step inside a multi-step form is not this: use `btn-ghost btn-sm`.

## Cards

| Class | Use it for |
| --- | --- |
| `card` | The standard surface: list rows, settings groups, info blocks. |
| `card-raised` | A surface that sits above the page: hero blocks, dialogs, bottom sheets. Use sparingly; if everything is raised nothing is. |
| `card-interactive` | Add to a `card` that is itself a link or button. Hover lifts it 2px to the raised shadow; it has a press state and a focus ring. |

One radius (1rem), one border and one elevation each. Padding is not included, so add `p-4`
(rows) or `p-5` (blocks).

```razor
<div class="card p-4">…</div>
<a class="card card-interactive p-4 flex items-center gap-3" href="contacts">…</a>
```

The wallet balance card is a separate, deliberate design and is not a `card`. It is drawn as a
closed wallet: `wallet-card` is the body (proportions, corners, a shadow in the wallet's colour),
`wallet-tex-*` is the material (`smooth` leather by default, or pebbled leather, cotton, plastic,
metal), `wallet-strap` is the tab across the right edge, and `wallet-snap` is the metal stud on
it. The snap is a real button, "Lock wallet", and calls `AppState.LockNow()`. The wallet is always
a coloured surface with light text, in both themes.

## Text inputs

```razor
<div class="field">
    <label class="field-label" for="addr">Recipient address</label>
    <input id="addr" class="input" type="text" @bind="address"
           aria-invalid="@(addressError is null ? null : "true")"
           aria-describedby="addr-msg" />
    @if (addressError is null)
    {
        <p id="addr-msg" class="field-hint">Starts with dgb1, D or S.</p>
    }
    else
    {
        <p id="addr-msg" class="field-error">
            <svg …warning icon… aria-hidden="true"></svg>
            <span>@addressError</span>
        </p>
    }
</div>
```

- `input` works on `<input>`, `<textarea>` and `<select>`. 48px high, 16px text (anything smaller
  makes iOS zoom the page on focus).
- **Focus**: the border takes the focus colour and a 3px soft ring appears.
- **Error**: set `aria-invalid="true"` (preferred) or add `input-error`. Always pair it with a
  `field-error` message that carries an icon as well as the red text, so the error does not rely
  on colour alone.
- **Disabled**: the `disabled` attribute.
- Numeric fields (`inputmode="decimal"`, `type="number"`) keep JetBrains Mono from the existing
  rule in `app.css`.
- The border is darker than today's `border-slate-200` on purpose: a field boundary needs 3:1
  against its background (WCAG 1.4.11) and slate-200 on white is about 1.2:1.

`PinInput` and `CredentialInput` have their own layouts and stay as they are.

## QR frame

```razor
<div class="qr-frame">
    <img src="@qrUrl" alt="QR code for your receive address" class="w-44 h-44" />
    <span class="qr-frame-logo"><DigiLogo Size="28" /></span>
</div>
```

White in both themes, so scanners always get a clean quiet zone; in dark it gains a soft blue
halo. The logo slot is optional. Keep the logo under about a fifth of the code's width, or it
eats into the error correction and some phones stop reading the code.

## Motion

| Token | Value | Use it for |
| --- | --- | --- |
| `--motion-fast` | 120ms | Press feedback. |
| `--motion-base` | 200ms | Hover, focus, colour and shadow changes. |
| `--motion-slow` | 360ms | Entrances. |
| `--ease-out` | `cubic-bezier(0.22, 1, 0.36, 1)` | The default curve (already the app's de-facto curve). |
| `--ease-spring` | `cubic-bezier(0.34, 1.56, 0.64, 1)` | A small overshoot, for celebrations only. |

`stagger` on a list container makes its first ten children rise in 40ms apart; later children
arrive together, so a long list never takes more than about 0.75s to settle. It leaves no
transform behind, so dialogs opened from inside a row still cover the screen.

```razor
<div class="stagger flex flex-col gap-2">
    @foreach (var c in contacts) { <div class="card p-3">…</div> }
</div>
```

Under `prefers-reduced-motion: reduce` the lifts, presses, entrance and sheen are switched off.
Colours and shadows still change, instantly. The loading spinner rests as a static ring.

New animations should use these tokens instead of new hard-coded durations.

## Chosen look

Three looks were compared for the primary button and the card: soft shadow, flat, and gradient
sheen. The owner chose **soft shadow**, which is what `btn-primary` and `card` now are. The other
two were removed from `app.css` and the gallery (they are in the branch history at commit
`197dd73`, with screenshots `*-1-primary-variants.jpg` and `*-3-cards.jpg` in
`docs/media/ui-style/`).

## Slide to confirm

`SlideToConfirm` (in `Components/Shared/`) replaces the final button of an action that cannot be
undone. The owner chose it over a press-and-hold button, which was removed (it is in the branch
history at commit `e0dd75e`, with screenshots `*-6-confirm-controls.jpg`). It is used on Send
(single and batch), DigiDollar send, Consolidate, Digi-ID fund recovery and multisig broadcast.

**Use it only for irreversible actions where coins leave the wallet:** send, batch send,
multisig broadcast, consolidate, Digi-ID fund recovery. **Never for Unlock, and never for routine
confirmations** (saving a setting, closing a dialog, deleting a contact). A deliberate gesture
only protects the user while it is rare; on every dialog it becomes a habit and protects nothing.

```razor
<SlideToConfirm @ref="confirm" Label="Slide to send"
                ActionDescription="@($"Send {amount} DGB to {recipient}")"
                OnConfirmed="SendAsync" />

@code {
    private SlideToConfirm? confirm;

    private async Task SendAsync()
    {
        try { await DoSend(); }
        catch { confirm?.Reset(); throw; }   // unlock the control again if the send failed
    }
}
```

| Parameter | Meaning |
| --- | --- |
| `Label` | Visible text while idle ("Slide to send"). |
| `BusyLabel` | Visible text after confirming. Default "Sending…". |
| `ActionDescription` | What is being confirmed, read by screen readers: include the amount and recipient. |
| `HoldMs` | How long Enter or Space must be held. Default 1000. |
| `Disabled` | Blocks the control. |
| `OnConfirmed` | Raised exactly once. The control then stays locked and busy until `Reset()`. |
| `Reset()` | Method: returns the control to idle. |

How it can be completed:

- **Pointer.** Drag the thumb to the end; releasing before 90% snaps it back.
- **Keyboard.** Tab to the control and hold Enter or Space for `HoldMs`. The thumb travels
  by itself while the key is down. Releasing early cancels. A quick tap does nothing.
- **Screen reader.** A screen reader's "activate" cannot be held, so it is a two-step: the first
  activation announces "Activate again within five seconds to confirm", the second confirms.
- Completion is announced politely ("Confirmed. Sending…") and the control sets `aria-busy`.

The control is 56px high. Place it at least 24px in from the screen edge, so the drag never
starts in the iOS swipe-back zone (inside a `card` on a normal page it is about 48px in).

Under reduced motion nothing travels or fills by itself: the drag or the one-second key hold is
still required, and a held control shows a steady half-tint with "Keep holding…" instead of
progress.

It needs one small script, `domHelpers.beginSlide` in `wwwroot/js/dom-helpers.js`, for
pointer capture. It is self-hosted and the Content Security Policy is unchanged.

## Bottom menu

The owner chose **variant C**, and `Layout/NavMenu.razor` now uses it: Wallet, Activity, a raised
round **Scan** button, **Request**, **More**, styled with the `nav-float*` classes (`nav-float-live`
pins the bar to the bottom of the app column). `MainLayout.razor` reserves room for it with
`main-with-nav`. Icon paths live in `Layout/NavIcons.cs`.

How it stays unconfusing:

- **One Scan.** The centre button is the only app-wide scanner (Home's own Scan button was
  removed), and `Services/ScanRouter.cs` decides where a code goes: a DigiByte
  address or payment request opens Send, a Digi-ID login opens Digi-ID, a multisig proposal opens
  the multisig it spends from, a redeem script opens Import multisig, this wallet's own room
  invite opens the room, and anything else opens nothing and says so. A code is never pasted into
  Send unless it is a valid address, and no other web link is followed.
- **One More.** The More tab is the only More (Home's own More button was removed); its list is
  `Layout/MoreMenu.cs`.
  Digi-ID and Settings are its first two rows because they used to be tabs. The More tab is drawn
  as the current tab on every page its list leads to, and the list marks the current page.
- **Request means the page it opens.** The Payments page is titled "Request a payment", and opens
  straight onto the form when there are no requests to list.
- **Translated.** The menu labels, the More list, the tip and the scan messages come from
  `wwwroot/locales` (`nav.*`, `more.*`, `scan.*`), with the English text as a fallback so the menu
  never shows a raw key.
- **A one-time tip for returning users.** "Digi-ID and Settings moved. They are under More now."
  appears once above the More tab on a device that was in use before; opening More or tapping
  "Got it" dismisses it for good. A wallet created on this menu never sees it.

From 1024px wide the same menu becomes a vertical rail hugging the left side of the app column
(one media query on `nav-float-live`; the pill slides up and down, and the page drops its bottom
padding). Below that, phones and tablets keep the bottom bar. The bar has a glass look: a
translucent fill over blurred content, a light rim and a layered shadow (`--ui-nav-*` tokens).

### Inside a shared wallet (multisig)

While one multisig is open, the menu keeps its shape and its five places but acts on that
multisig (spec 026):

| Place | Personal wallet | Inside a multisig |
|-------|-----------------|-------------------|
| 1 | Wallet | **Overview** (the multisig's dashboard) |
| 2 | Activity | Activity (the multisig's) |
| 3 | Scan | Scan (an address or payment request pays **from this multisig**) |
| 4 | Request | **Pending** (with a count) |
| 5 | More | More |

- **The address decides the mode.** `Layout/ShellContext.cs` is the only place that knows which
  pages are "inside a multisig": `/multisig/{id}` and its `send`, `pending`, `pending/{id}` and
  `activity` pages. The list, Create, Import, the guide and the room's join screen keep the
  personal menu. Deciding from the address means the menu and the page cannot disagree, also
  after a reload or a link straight to a page.
- **Never colour alone.** Three things say which mode it is: a small **name tab** on the bar's
  top edge with a key icon and the multisig's name (`nav-shared-tab`; it sits on the left, clear
  of the raised Scan button), the labels themselves (Overview, Pending), and the **context
  line** on the page (`Components/Multisig/SharedWalletContextLine.razor`): the multisig's name,
  its scheme, and "in *wallet*" with that wallet's colour.
- **One way out.** The context line and the name tab open the switcher
  (`SharedWalletSwitcher.razor`): "Back to *wallet*" first, then the wallet's other multisigs.
  The menu owns the switcher so it opens above the bar; a page asks for it through
  `ShellActions.RequestSwitcher`.
- **No repeats.** A multisig's dashboard does not offer a button for a place the menu already
  has under the same name, so it keeps Send and Receive only.

### Counts

`nav-badge-count` is live for multisig: `Services/MultisigAttentionState.cs` is the one owner of
"payments that need you" (your signature, ready to broadcast, or a broadcast to retry), worked
out from payments saved on the device with no network call. In the personal menu the count
sits on **More** and on the Multisig row of its list, and on Home's Multisig shortcut; inside a
multisig it sits on **Pending** for that multisig only. `CountBadge.Text` caps it at "99+", and
the count is always in the control's accessible label. `nav-badge-dot` is still unused.

### When the menu steps aside

A page can take the whole screen through `Services/ShellChrome.cs`. Two things use it: the
signing room while it is live or showing its result, so its one button owns the bottom of the
screen, and a sheet opened from inside a page (Add a proposal), so the sheet is not under the
bar. The room's join screen keeps the menu.

### A pinned main button

`sticky-action` (`Components/Multisig/StickyAction.razor`) keeps a screen's one main button at
the bottom of the visible page while the content above it scrolls, and lets it rest in its own
place once the page is scrolled that far. It needs no offset for the menu: it is measured inside
the page's own bottom padding, which already clears the bar. A confirm step takes the button's
place, so it appears where the person just tapped.

The gallery still shows four static previews built by `Components/Dev/NavMenuPreview.razor`, for
comparison:

| Variant | What it is |
| --- | --- |
| A | The previous menu, kept as a baseline. |
| B | Replaced by C. The old five tabs (Wallet, Pay, Activity, ID, Settings) as a floating bar: lifted off the bottom edge, fully rounded ends, soft shadow. The active tab has a filled icon on a small tinted pill (behind the icon only, not the label) that slides between tabs. Badges: a dot (a payment is pending) and a count (a multisig needs your signature). |
| C | **Adopted.** Rearranged in B's style: Wallet, Activity, a raised round Scan button, Request (the old Pay page), More. More opens a list: Digi-ID, Settings, Multisig, Contacts, Consolidate, Analytics, P2P Exchange, Donate, Roadmap. In the desktop rail, Scan is a smaller disc in line with the tabs. |
| C2 | Not adopted. C, keeping ID as a tab in place of Request (Request moves into More). |

The pill takes the active wallet's colour as a tint; the active icon and label use fixed colours,
because a wallet colour can be anything and could not be guaranteed to pass contrast as text.

The previous menu (A) drew inactive labels in slate-400, which is 2.6:1 on the light theme and
fails AA; B and C use slate-600 (7.6:1). Screenshots: `*-7-menu-a-b-c.jpg` and
`*-8-menu-c2-more-sheet.jpg` in `docs/media/ui-style/`.

Still to follow up after adopting C:

- README screenshots and the videos in `docs/media` still show the old five tabs.
- The scan icon inside Send, Contacts and the multisig pages stays each page's own on purpose: it
  fills in that page's field. Only the app-wide Scan uses `ScanRouter`.
- The one-time "moved" tip can be removed a few releases from now.

## Accessibility checks (measured in the gallery)

| Pair | Light | Dark |
| --- | --- | --- |
| Primary button text | 5.6:1 | 5.6:1 |
| Danger button text | 4.8:1 | 4.8:1 |
| Secondary button text | 14.6:1 | 13.3:1 |
| Ghost button text | 7.3:1 | 8.6:1 |
| Input text | 17.9:1 | 18.4:1 |
| Placeholder | 4.8:1 | 7.2:1 |
| Field label / hint / error | 9.9 / 7.2 / 6.2 | 15.0 / 7.2 / 9.7 |
| Input border against field | 3.5:1 | 4.0:1 |
| Focus ring against page | 5.3:1 | 6.1:1 |

Text needs 4.5:1 and boundaries 3:1 for AA. Button heights measure 48px and 44px.

## Rollout

**Buttons: done for the standard cases.** `tools/button-style-rollout.py` moved 159 hand-written
primary, secondary and danger buttons in 56 files to `btn-primary`, `btn-secondary` and
`btn-danger` (`btn-sm` where the original was a compact button). Run it without `--apply` to see
what it would change. It deliberately leaves these alone, and they are still hand-written:

- buttons whose class is built with a Razor expression (26), such as toggles and tabs;
- icon-only buttons with a fixed size, pills and chips;
- text-only link buttons, which would become taller as `btn-ghost`;
- buttons in a colour of their own (green approve, amber warning, indigo).

**Text fields: done for the standard cases.** `tools/input-card-style-rollout.py … inputs` moved 46
inputs and textareas in 24 files to `input`. Left hand-written: fields inside a composite box (the
amount box with its currency badge, search boxes), PIN and code boxes, fields with an icon inside,
selects (the shared class removes the native arrow and has no replacement yet) and fields whose
class is built with a Razor expression. A composite amount box next to shared fields therefore
still looks slightly different (lighter border, 40px high).

**Cards: done for the standard surface.** `… cards` moved 93 blocks in 25 files to `card`, and
to `card card-interactive` where the block is itself a link or button. Left hand-written: dialogs
and sheets (they would be `card-raised`), blocks with a border colour of their own (warnings,
errors, selected states), and Home's round quick-action tiles.

**QR codes: done.** Every QR code sits in `qr-frame`, including the shared `QrImage` component;
`qr-frame-sm` is the compact frame on the empty-Home card and the sponsor form.

**Slide to confirm: done for Send, DigiDollar send, Consolidate, Digi-ID fund recovery and
multisig broadcast.** Still a tap: broadcasting from the sign-together room (its one headline
button changes role) and signing a PSBT (it ends with the PIN).

Not used anywhere yet: `stagger` and the `field` / `field-label` / `field-hint` / `field-error`
wrappers. The table below lists pages by priority for those.

Converted so far: the bottom menu, and the confirm card of Send (slide to confirm and its Cancel
button). Everything else is still hand-written. Convert one page per commit so each diff is easy to review, and delete
the hand-written class strings as you go. The order below is by expected traffic: it follows the
bottom navigation and the main flows, and is an estimate, not measured usage. The numbers are
rough counts of `<button>` and form elements in each file today.

| # | Page or component | Buttons | Fields | Notes |
| --- | --- | --- | --- | --- |
| 1 | `Pages/Unlock.razor` | 15 | 2 | Every session starts here. PIN boxes stay as they are. |
| 2 | `Pages/Send.razor` + `Components/Shared/TransactionDetailModal.razor` | 36 | 7 | The main money flow; best test of `input` error states. |
| 3 | `Pages/Payments.razor` | 8 | 3 | First `qr-frame` conversion. |
| 4 | `Pages/Settings.razor` | 22 | 3 | Rows become `card card-interactive`. |
| 5 | `Pages/Identity.razor` + `Components/Shared/DigiIdRecoverFunds.razor` | 21 | 3 | |
| 6 | `Pages/Welcome.razor`, `CreateWallet.razor`, `RecoverWallet.razor`, `BackupSeed.razor`, `ImportXpub.razor` | 41 | 12 | Onboarding: seen once per user, but it is the first impression. |
| 7 | `Pages/WalletManagement.razor`, `Contacts.razor`, `Components/Shared/WalletPickerSheet.razor` | 27 | 6 | Uses `btn-danger`. |
| 8 | `Pages/DigiDollar.razor`, `DigiDollarSend.razor`, `DigiDollarReceive.razor`, `Components/DigiDollar/*` | 17+ | 2 | Second `qr-frame`. |
| 9 | Multisig: `MultisigWallets`, `MultisigDetail`, `MultisigCreate`, `MultisigSend`, `MultisigPending`, `MultisigSigningRoom`, `MultisigRoomPage`, `MultisigImport`, `MultisigActivity`, `MultisigGuide`, `PsbtSign`, `Components/Multisig/*` | 150+ | 25+ | The largest block; split into several commits. |
| 10 | `ConsolidateUtxos`, `SpendingLimits`, `Recovery`, `ExportXpub`, `NetworkStatus`, `DevNode` | 30 | 9 | Tools. `ExportXpub` has a QR. |
| 11 | `Help`, `About`, `Sponsors`, `DonationHistory`, `Roadmap`, `Analytics`, `DeploymentInfo`, `Remittance`, `P2PMarketplace`, `Components/Shared/SponsorSubmitForm.razor` | 35 | 15 | Low traffic. |
| 12 | Shared chrome: `PwaInstallBanner`, `PwaUpdateBanner`, `BakuWarningDialog`, `OnboardingWalkthrough`, `GettingStarted`, `BiometricNotice`, `Components/Wallet/WalletProtectionCard.razor` | 26 | 0 | |

**After `feature/first-receive-flow` merges** (these files have unmerged work on that branch, so
converting them now would conflict):

| Page or component | Notes |
| --- | --- |
| `Pages/Home.razor` | Highest traffic of all; do it first once the branch lands. Has a QR dialog. |
| `Pages/Receive.razor` | The main `qr-frame` use. |
| `Pages/Transactions.razor` (Activity) | Good first use of `stagger` on the list. |
| `Components/Wallet/ReceiveEmptyState.razor` | |
| `Components/Shared/FirstReceiveCelebration.razor` | |

When the last page is converted, `press-effect` and `card-hover` in `app.css` have no users left
and can be removed.
