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

## Variants to choose from

The gallery shows three looks for the primary button and the card. Plain `btn-primary` and `card`
are the soft-shadow look (A). `btn-flat` / `card-flat` (B) and `btn-sheen` / `card-sheen` (C) are
modifiers that exist only so the three can be compared. Once one is chosen, fold it into the base
class and delete the other two blocks from `app.css` and the gallery.

Screenshots at 375px are in `docs/media/ui-style/` (`light-*` and `dark-*`).

Recommendation: **A (soft shadow) for both.** It gives the depth that was asked for and reads
well in both themes. B is what the app mostly has today. C's gradient looks good on one hero
button but busy when two or three buttons share a screen, and the card tint is barely visible in
light and slightly muddy in dark.

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
