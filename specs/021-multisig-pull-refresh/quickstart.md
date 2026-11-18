# Quickstart: Verify Pull-Down Refresh

Prerequisites: run the web app (`dotnet run --project src/DigiByte.Web`), create or import a multi-sig wallet, turn on touch device emulation in the browser's DevTools, and open `/multisig/<id>`.

1. **Happy path (US1)**: at the top of the page, drag down past the threshold and release. Expect a spinner, then updated balance and recent activity, then the spinner goes away.
2. **Short pull (US1, scenario 3)**: drag down a little and release. Expect no refresh and the indicator retracts.
3. **Mid-scroll (US2)**: scroll down, then swipe down. Expect normal scrolling and no refresh.
4. **Overlays (US2, scenario 2)**: open the Receive sheet, the Rename dialog, the expanded co-signers panel and the "Remove this multisig" confirmation, and swipe down inside each. Expect no refresh.
5. **Offline (US3)**: set the network to Offline and pull. Expect the spinner to end, the previous balance and activity still shown, and an error toast "Couldn't refresh".
6. **Repeat (US3)**: pull twice quickly, pull while tapping the balance card's refresh button, and pull immediately after the page first appears. Expect a single refresh each time.
7. **Button unchanged**: tap the refresh button online and offline. Expect the same behaviour as before this feature (no toast), except a tap is ignored while a refresh is already running.
8. **Scope check**: `/multisig` (the wallet list) has no pull-to-refresh.

Automated: `dotnet build` and `dotnet test` pass.
