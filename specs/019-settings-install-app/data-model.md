# Data Model: Install App from Settings

No persisted data. The only entity is a transient, in-memory install state read from the browser each time Settings opens.

## Install state (snapshot)

| Field | Type | Meaning |
|---|---|---|
| `standalone` | bool | Wallet is running as an installed app |
| `ios` | bool | iPhone, iPad (including iPadOS in desktop-UA mode) |
| `android` | bool | Android device |
| `canPrompt` | bool | The browser has offered a native install prompt that is still available |
| `nonSafariIos` | bool | iOS browser other than Safari, or an in-app web view |
| `promptSeen` | bool | The browser offered a native prompt at some point in this page session (even if it has since been used or dismissed) |

## Derived row mode

| Mode | Rule (first match wins) | Row | Tap |
|---|---|---|---|
| `Hidden` | `standalone` | no | n/a |
| `Native` | `canPrompt` | yes | native prompt |
| `IosSteps` | `ios` | yes | iOS instructions (with Safari note if `nonSafariIos`) |
| `MenuSteps` | `android` or `promptSeen` | yes | browser-menu instructions (Android, or desktop after the prompt was used or dismissed) |
| `Hidden` | otherwise | no | n/a |

## State transitions

- `Native` → `Hidden`: the browser reports the install completed (Android, desktop), immediately, no reload.
- `Native` → `MenuSteps`: the prompt is used or dismissed (by the Settings row or by the top banner) without installing. A prompt can be shown only once, so the row falls back to menu instructions on Android and desktop alike. If the browser offers a new prompt later, the row returns to `Native`. The row never becomes a dead button and never vanishes after a cancel (FR-005, FR-008).
- `MenuSteps` → `Native`: the browser offers the prompt after Settings opened; the row switches to `Native` without reload.
- `IosSteps` → `Hidden`: only on a new launch from the home screen (`standalone` true). No signal exists earlier.

## Validation rules

- The row is never shown when `standalone` is true (FR-002).
- The row does not read or write the banner's dismiss flag (FR-006).
- Nothing is stored or sent (FR-010).
