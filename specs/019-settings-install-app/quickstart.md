# Quickstart: Validating Install App from Settings

## Prerequisites

- The wallet served over HTTPS (installability requires it), for example a deployed preview or a tunnel to a local run. `localhost` works for desktop Chrome.
- Start locally: `dotnet run --project src/DigiByte.Web`
- Fresh browser profile or site data cleared, so the wallet is not already installed.

## Scenarios

Contract and state rules: [contracts/ui-and-interop.md](contracts/ui-and-interop.md), [data-model.md](data-model.md).

### A. Android (real device, Chrome)

1. Open the wallet, dismiss the top banner, go to Settings. Expect: "Install app" row is present (FR-006).
2. Tap it. Expect: native install confirmation. Cancel. Expect: row still usable (menu instructions or prompt).
3. Tap again and accept. Expect: row disappears without reload; icon on home screen.
4. Launch from the home screen, open Settings. Expect: no row.

### B. iOS (real iPhone/iPad, Safari)

1. Open the wallet, go to Settings. Expect: "Install app" row with the "Add to your home screen" subtitle.
2. Tap it. Expect: three steps with the Share glyph. Close it; row remains.
3. Follow the steps. Return to the browser tab. Expect: row still shown (no signal on iOS).
4. Launch from the home screen, open Settings. Expect: no row.
5. Repeat step 1 to 2 in Chrome for iOS. Expect: same steps plus the "open in Safari" note.

### C. Desktop (Chrome or Edge)

1. Open Settings. Expect: row present when the browser offers install; tap shows the native prompt; accepting hides the row.
2. Dismiss the native prompt, then tap the row again. Expect: the row stays and shows menu instructions.
3. In Firefox or desktop Safari. Expect: no row.

### D. Banner regression

1. On Android and desktop, confirm the top banner still appears, installs, and dismisses as before, and that installing through either the banner or Settings hides both.

### E. Layout and themes

1. In light and dark themes at 320 px width, confirm the row and both dialogs fit with no horizontal scroll and are readable.
2. Offline (airplane mode after load): row and dialogs still open.

## Expected outcomes

Every "Expect" above holds. Record device, OS version and browser version for Android, iOS and desktop (SC-004).
