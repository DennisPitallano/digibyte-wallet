# Research: Install App from Settings

No `NEEDS CLARIFICATION` items remained after `/speckit-clarify`. The decisions below close the open design questions.

## D1. Share the install prompt between banner and Settings

- **Decision**: Keep the single captured `beforeinstallprompt` in `pwa-install.js` and change `registerCallback` to add to a set of subscribers (and add `unsubscribe`), instead of overwriting one slot.
- **Rationale**: Today `window._pwaInstallCallback` holds exactly one reference. The banner registers it at app start, so Settings registering as well would replace it and silently break the banner (violates FR-010). A set lets both react to `OnInstallAvailable` / `OnAppInstalled`.
- **Alternatives**: a separate second capture of the event (impossible; `preventDefault` and the stored prompt are single-use); a Blazor singleton service wrapping the JS (more moving parts for one row; can be introduced later if a third consumer appears).

## D2. Detecting "installed"

- **Decision**: Reuse `pwaInstall.isStandalone()` (display-mode standalone or iOS `navigator.standalone`). Hide the row when true.
- **Rationale**: Already used by the banner, covers Android, desktop and iOS home-screen launches. Per spec assumptions, an installed app opened in a normal browser tab is not detected.
- **Alternatives**: `navigator.getInstalledRelatedApps()` (Chromium only, needs manifest related-apps entry, unreliable) rejected as out of scope.

## D3. Platform decision table

| Environment | Native prompt captured | Row | Tap behaviour |
|---|---|---|---|
| Installed (standalone) | n/a | Hidden | n/a |
| Android / desktop Chromium | Yes | Shown | Native prompt |
| Android, prompt not captured yet | No | Shown | Browser-menu instructions |
| iOS / iPadOS (incl. iPad desktop-mode UA) | n/a | Shown | Add to Home Screen steps; non-Safari note |
| Desktop whose prompt was already used or dismissed | No (was seen) | Shown | Browser-menu instructions |
| Desktop that never offered a prompt (Firefox, Safari desktop) | No | Hidden | n/a |

- **Rationale**: Matches FR-001 to FR-005 and Story 4. Desktop without a prompt is hidden because there is no reliable manual path to describe.
- **Platform detection**: reuse `pwaInstall.isIos()` (already handles iPadOS reporting as MacIntel with touch); add `isAndroid()` from the user agent.

## D4. iOS in non-Safari browsers

- **Decision**: Show the same steps everywhere on iOS, plus a one-line note "Don't see Add to Home Screen? Open this page in Safari." shown when the user agent indicates a non-Safari or in-app browser (tokens such as CriOS, FxiOS, EdgiOS, or no `Safari` token).
- **Rationale**: Since iOS 16.4 other browsers can add to home screen from their share menu, so hiding the steps would be wrong; the note covers older versions and in-app web views (FR-004).

## D5. iOS "installed" signal

- **Decision**: No signal exists in the browser on iOS (no `appinstalled`). The row stays until the app is launched from the home screen (spec clarification, FR-007).
- **Alternatives**: hide after the user closes the sheet (rejected in clarification); persist a "did it" flag (rejected; could hide the row from someone who cancelled).

## D6. Placement and look

- **Decision**: New row in the existing Settings list using the same card markup as its neighbours, under a new small "App" heading placed after "Display" and before "Network & advanced" (it fits none of the existing groups). Icon: download-to-device glyph in the brand colour; subtitle varies: "Add to your home screen" (iOS), "Install for quick access" (others).
- **Rationale**: Keeps discoverability without a new page (spec assumption) and without crowding the Wallet group.
- **Alternatives**: top of page promotional card (noisier, duplicates banner); inside "Network & advanced" (poor discoverability).

## D7. Instructions dialog

- **Decision**: Reuse the page's existing dialog pattern (fixed overlay, `role="dialog"`, `aria-modal`, click-outside and a Close button). Two variants: iOS (3 numbered steps with the Share glyph) and Android/other (menu steps: open the browser menu, choose "Install app" or "Add to Home screen").
- **Rationale**: Consistent look, works in both themes, no new dependencies, accessible.

## D8. Testing approach

- **Decision**: Manual device matrix (quickstart) because there is no component-test project for `DigiByte.Web` and standing one up is out of scope. Decision logic is kept as a small pure function (state snapshot in, row mode out) so it can be unit-tested later if a Web test project is added.
