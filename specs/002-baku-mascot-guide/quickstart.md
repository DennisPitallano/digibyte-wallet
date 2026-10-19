# Quickstart: Validating Baku

## Prerequisites

- .NET 10 SDK and Node (for the Tailwind build), as for the rest of `DigiByte.Web`.
- Pose images generated into `src/DigiByte.Web/wwwroot/images/mascot/poses/`.

## Automated checks

```bash
dotnet build
dotnet test tests/DigiByte.Wallet.Tests --filter SendRiskEvaluator
```

Expected: build is clean. Evaluator tests pass, covering the 50% boundary (49.99% off, 50% on), zero balance, saved contact (no new-address flag), previously-sent address, unknown address, and DGB and DigiDollar parity.

## Manual scenarios

Run the web app via the existing dev-server configuration, then:

1. **Onboarding (US1)**: With no wallet, open Welcome. Baku greets. Walk through create and backup: guidance on each step, a celebration on confirmed backup. Repeat with Baku set to Off: every step still completes.
2. **Seed reveal (US2)**: Request the seed phrase. A protective warning needs explicit confirmation. Cancel hides the seed, confirm proceeds.
3. **Risky send (US2)**: On Send, enter an unknown address, then a saved contact, then an amount at 50% of balance. Warnings appear only for the unknown address and the 50% amount. Repeat on DigiDollar Send.
4. **P2P (US2)**: Open P2P Marketplace and confirm the verify-counterparty warning.
5. **Help Center (US3)**: Open Help. Baku's intro sits above the search and topics. Search for nonsense text and confirm the thinking pose with a suggestion. Topics still expand as before.
6. **Tips (US3)**: Open Home with an empty wallet and see the empty-state message. Dismiss the tip, reload, and confirm it stays hidden. Use Settings, then Reset tips, and confirm it returns.
7. **Presence (US4)**: Set Guardian only, so tips vanish while warnings remain. Set Off, so warnings show as text only with no image.
8. **Unlock**: Lock the wallet. Baku greets small and disappears once typing begins.

## Cross-cutting checks

- **Image failure**: Block the pose image requests and confirm the warning text still renders.
- **Responsive**: Check 360 px, tablet and desktop widths. Baku never covers balances, addresses, amounts or primary buttons.
- **Accessibility**: Use a screen reader to confirm messages are announced, and enable reduced-motion to confirm animations stop.
- **Offline**: Load the app, go offline, reload, and confirm poses still display.
- **Performance**: Compare first-load size and time before and after (target 5% or less increase).
- **Privacy**: Inspect `localStorage` and network traffic. Only `dgb-baku-mode` and `dgb-baku-dismissed` are stored, and no new requests go out.
