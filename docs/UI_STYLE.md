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

The wallet balance card and its material finishes (`wallet-tex-*`) are a separate, deliberate
design and are not part of this.

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

## Confirm controls (candidates, not yet used on any page)

`SlideToConfirm` and `HoldToConfirm` (in `Components/Shared/`) replace the final button of an
action that cannot be undone. Both are in the gallery so one can be chosen; neither is wired
into a page.

**Use them only for irreversible actions where coins leave the wallet:** send, batch send,
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

`HoldToConfirm` takes the same parameters, so the two are interchangeable.

| Parameter | Meaning |
| --- | --- |
| `Label` | Visible text while idle ("Slide to send", "Hold to send"). |
| `BusyLabel` | Visible text after confirming. Default "Sending…". |
| `ActionDescription` | What is being confirmed, read by screen readers: include the amount and recipient. |
| `HoldMs` | How long a press or key must be held. Default 1000. |
| `Disabled` | Blocks the control. |
| `OnConfirmed` | Raised exactly once. The control then stays locked and busy until `Reset()`. |
| `Reset()` | Method: returns the control to idle. |

How each can be completed:

- **Pointer.** Slide: drag the thumb to the end; releasing before 90% snaps it back. Hold: press
  for `HoldMs`; releasing, or sliding off the button, drains the fill and cancels.
- **Keyboard.** Tab to the control and hold Enter or Space for `HoldMs`. The slide's thumb travels
  by itself while the key is down. Releasing early cancels. A quick tap does nothing.
- **Screen reader.** A screen reader's "activate" cannot be held, so it is a two-step: the first
  activation announces "Activate again within five seconds to confirm", the second confirms.
- Completion is announced politely ("Confirmed. Sending…") and the control sets `aria-busy`.

Both are 56px high. Place the slide at least 24px in from the screen edge, so the drag never
starts in the iOS swipe-back zone (inside a `card` on a normal page it is about 48px in).

Under reduced motion nothing travels or fills by itself: the drag or the one-second hold is still
required, and a held control shows a steady half-tint with "Keep holding…" instead of progress.

The slide needs one small script, `domHelpers.beginSlide` in `wwwroot/js/dom-helpers.js`, for
pointer capture. It is self-hosted and the Content Security Policy is unchanged.

## Bottom menu variants (previews only)

The gallery shows four static previews built by `Components/Dev/NavMenuPreview.razor`. The live
menu (`Layout/NavMenu.razor`), the layout and the routes are untouched. The `nav-float*` and
`nav-badge-*` classes in `app.css` exist only for these previews; delete them, or promote them,
once a variant is chosen.

| Variant | What it is |
| --- | --- |
| A | Today's menu, copied as a baseline. |
| B | The same five tabs as a floating bar: lifted off the bottom edge, rounded, soft shadow. The active tab has a filled icon on a tinted pill that slides between tabs. Badges: a dot (a payment is pending) and a count (a multisig needs your signature). |
| C | Rearranged in B's style: Wallet, Activity, a raised round Scan button, Request (today's Pay page), More. More opens a sheet with ID, Multisig, Contacts, Settings, Consolidate, Analytics, Sponsors. |
| C2 | C, keeping ID as a tab in place of Request (Request moves into More). |

The pill takes the active wallet's colour as a tint; the active icon and label use fixed colours,
because a wallet colour can be anything and could not be guaranteed to pass contrast as text.

Today's menu (A) draws inactive labels in slate-400, which is 2.6:1 on the light theme and fails
AA; B and C use slate-600 (7.6:1). Screenshots: `*-7-menu-a-b-c.jpg` and
`*-8-menu-c2-more-sheet.jpg` in `docs/media/ui-style/`.

Adopting B is styling only: `NavMenu.razor` and its bottom padding in `MainLayout.razor`, plus
two new pieces of state for the badges.

Adopting C or C2 is a navigation change, not a restyle:

- `NavMenu.razor` is rebuilt: Settings (and Pay or ID) stop being tabs.
- A new More sheet component, with focus trapping and its own route-active state, since five
  destinations now live inside it.
- The centre button opens `QrScannerModal` from the layout. Today the scanner is opened from
  inside Send and other pages, so its result handling (address, payment request, Digi-ID login)
  has to move somewhere shared.
- Pay is renamed Request: page title, header, Help text, translations in `wwwroot/locales`, and
  any onboarding copy that says "Pay".
- Every instruction that says "tap Settings" or "tap ID" in the bottom bar changes: Help,
  `OnboardingWalkthrough`, `GettingStarted`, README screenshots and the videos in `docs/media`.
- Existing users lose tabs they are used to: Settings moves one tap deeper in both, and either
  ID (C) or Request (C2) does too.

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

Nothing is converted yet. Convert one page per commit so each diff is easy to review, and delete
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
