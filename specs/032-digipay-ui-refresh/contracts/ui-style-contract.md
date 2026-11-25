# Contract C1: Shared UI Style Layer (`DigiByte.UI`)

This is the interface between the shared style source and the two apps that use it, the wallet (`DigiByte.Web`) and DigiPay (`DigiByte.Pay.Web`). A change to anything listed here is a change to both apps. It MUST be checked on both before release (FR-001).

## Asset paths (served by each app from its own origin)

| Path | Content |
|---|---|
| `_content/DigiByte.UI/css/ui.css` | Fonts, tokens and component classes |
| `_content/DigiByte.UI/fonts/inter-latin-wght-normal.woff2` | Inter variable, weights 100–900 |
| `_content/DigiByte.UI/fonts/jetbrains-mono-latin-wght-normal.woff2` | JetBrains Mono variable, weights 100–800 |
| `_content/DigiByte.UI/images/mascot/poses/*.webp` | Baku poses used by DigiPay's dashboard |

**Load order**: `tailwind.css`, then `_content/DigiByte.UI/css/ui.css`, then the app's own CSS (the wallet's `app.css`, DigiPay's component-scoped styles). The theme boot script runs synchronously in `<head>` before first paint.

## Theme hook

- **Dark theme**: the `dark` class on `<html>` turns it on. No class means light.
- **Variables**: every themed value is a CSS custom property on `:root` and `.dark`. Component rules never repeat themselves per theme.
- **Responsibility**: each app owns its own boot script and storage key. `ui.css` only reads the class.

## Tokens (custom properties)

| Group | Properties |
|---|---|
| Shape | `--ui-radius-sm`, `--ui-radius`, `--ui-radius-lg` |
| Surface and text | `--ui-surface`, `--ui-border`, `--ui-text`, `--ui-text-muted` |
| Focus | `--ui-focus`, `--ui-focus-ring` |
| Depth | `--ui-shadow-card`, `--ui-shadow-raised` |
| Primary | `--ui-primary`, `--ui-primary-hover`, `--ui-primary-shadow`, `--ui-primary-shadow-hover` |
| Danger | `--ui-danger` (red-600), `--ui-danger-hover`, `--ui-danger-shadow`, `--ui-danger-shadow-hover` |
| Secondary | `--ui-secondary`, `--ui-secondary-hover`, `--ui-secondary-text`, `--ui-secondary-border`, `--ui-secondary-shadow`, `--ui-secondary-shadow-hover`, `--ui-secondary-pressed` |
| Ghost | `--ui-ghost-text`, `--ui-ghost-hover`, `--ui-ghost-active`, `--ui-pressed-shadow` |
| Motion | `--motion-*`, `--ease-*` |

The full list is whatever is defined in `ui.css` at the time. Apps MAY read tokens. Apps MUST NOT redefine them, except DigiPay's POS keypad sizing, which uses its own layout classes and no token overrides.

## Component classes

| Class | Role | Size and behaviour guarantees |
|---|---|---|
| `btn-primary`, `btn-secondary`, `btn-ghost`, `btn-danger` | Buttons by role. At most one `btn-primary` per view. | 48px tall, 16px text. Focus ring from `--ui-focus-ring`. `disabled` and `aria-busy` styles. |
| `btn-sm` | Compact modifier | 44px tall, 14px text. No smaller size exists. |
| `card`, `card-raised`, `card-interactive` | Surfaces | `card-interactive` gets hover, press and focus states |
| `input`, `field`, `field-label`, `field-hint`, `field-error` | Text fields | 48px tall, 16px text (no zoom on phones). Border meets non-text contrast in both themes. `aria-invalid="true"` shows the error style. |
| `page-title`, `block-title`, `section-label` | Headings | |
| `segmented`, `choice`, `choice-pill` | Choices | Selected state comes from `aria-pressed="true"` (or `aria-selected` in tablists) |
| `icon-btn`, `back-btn` | Icon-only controls | 44px minimum. An accessible name is required (FR-031). |
| `qr-frame`, `qr-frame-sm` (+ logo slot) | QR display | Quiet zone kept. Logo at most 1/5 of the code's width. |
| `stagger` | List entrance | Turned off under reduced motion |
| `skeleton` | Loading placeholder | Turned off (static) under reduced motion |
| Bottom menu / rail classes (as in the current `app.css` "Bottom menu" block) | App menu | Bottom bar below 1024px, left rail from 1024px. Raised main button. Current item marked with `aria-current="page"`. |
| Toast classes | Notices | Tone colours meet AA. Positioned clear of the bottom menu and the safe area. |
| Baku bubble classes | Mascot message | DigiPay uses these only in the dashboard |

## DigiPay wrappers (in `DigiByte.Pay.Web/Components/Shared/Ui/`)

These are DigiPay's components. Their only obligation to this contract is to output the classes above:

- `CopyButton` uses `btn-ghost btn-sm` or `icon-btn`.
- `StatusBadge` uses badge tone classes from `ui.css`.
- `ConfirmDialog` uses `card-raised`, `btn-danger` and `btn-secondary`.
- `EmptyState` uses `card` and the Baku bubble.
- `Skeleton` uses `skeleton`.
- `ThemePicker` uses `segmented` / `choice-pill`.
- `Toast` uses the toast classes.

## Change rule

A pull request that edits `src/DigiByte.UI/**` MUST:

1. Include before and after screenshots of the wallet's style gallery (`/test-animations`), and of DigiPay's checkout and dashboard Overview, in light and dark.
2. Keep every guarantee in the table above, or update this contract and `docs/UI_STYLE.md` in the same PR.
