# Contract: Settings UI and browser interop

This feature exposes no network API. The contracts are the UI the user sees and the JavaScript surface Settings uses.

## UI contract (Settings page)

- A group heading "App" appears after the "Display" group and before "Network & advanced", only when the row is visible.
- Row title: "Install app". Subtitle: "Add to your home screen" on iOS; "Install for quick access" otherwise.
- The row is a button with an accessible name equal to its title; it has the same card styling as neighbouring rows in light and dark themes.
- Tap, mode `Native`: browser install confirmation opens. On accept the row disappears; on cancel it stays (or falls back per [data-model.md](../data-model.md)).
- Tap, mode `IosSteps`: dialog "Install on iPhone / iPad" with numbered steps:
  1. Tap the Share icon (glyph shown) in the browser toolbar.
  2. Scroll and tap "Add to Home Screen".
  3. Tap "Add".
  Plus, when `nonSafariIos`: "Don't see Add to Home Screen? Open this page in Safari."
- Tap, mode `MenuSteps`: dialog "Install this app" with steps: open the browser menu (three dots), choose "Install app", "Add to Home screen" or "Install DGB Wallet" (wording varies by browser), confirm. Used on Android and on desktop after the prompt was used or dismissed.
- Dialogs: `role="dialog"`, `aria-modal="true"`, close by a Close button, by tapping outside, or with the Escape key; fit a 320 px wide screen with no horizontal scroll.

## JavaScript interop contract (`window.pwaInstall`)

Existing members keep their behaviour: `registerCallback(dotnetRef)`, `prompt()`, `isStandalone()`, `isIos()`.

| Member | Change | Contract |
|---|---|---|
| `registerCallback(dotnetRef)` | Behaviour widened | Adds the reference to a set of subscribers instead of replacing a single one. Still invokes `OnInstallAvailable` immediately if a prompt is already held, and `OnIosDetected` on iOS when not standalone. |
| `unsubscribe(dotnetRef)` | New | Removes the reference; safe to call twice. |
| `getState()` | New | Returns `{ standalone, ios, android, canPrompt, nonSafariIos, promptSeen }` (booleans), synchronous, no side effects. |
| `isAndroid()` | New | User-agent check for Android. |

Callbacks invoked on the .NET side: `OnInstallAvailable`, `OnAppInstalled` (existing names) and `OnPromptUsed` (new: the held prompt was used or dismissed without installing, so other subscribers can refresh). Subscribers that do not implement `OnPromptUsed` (the banner) must not break: the JS invokes it defensively and ignores a missing method. Settings implements all three; the banner is unchanged.

## Compatibility

The banner's calls (`registerCallback`, `prompt`, `isStandalone`) keep the same signatures and results. Subscribers are notified in registration order; one subscriber's failure must not stop the others.
