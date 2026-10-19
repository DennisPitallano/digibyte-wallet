# UI Contracts: Baku Mascot

The feature has no external API. These are the internal interfaces that pages rely on.

## `BakuService` (scoped, `DigiByte.Web/Services`)

| Member | Contract |
|---|---|
| `Mode` / `SetModeAsync(mode)` | Reads or persists the presence mode (`Full`, `GuardianOnly`, `Off`). Raises `OnChanged`. |
| `Current` | The single message to show now, or null. Applies mode filtering and severity priority. |
| `Request(messageId)` | Offers a message for the current screen. Ignored if dismissed (tips only) or filtered by mode (only Info and Tip are filtered; Warning and Critical are never filtered). |
| `Clear(messageId)` | Withdraws a message, for example on navigation. |
| `DismissAsync(messageId)` | Permanently dismisses a dismissible message. A no-op for Warning and Critical. |
| `ResetTipsAsync()` | Clears dismissed tips. |
| `ConfirmAsync(messageId)` | Shows the guardian dialog and returns true (proceed) or false (cancel). Always text-visible. |
| `OnChanged` | Event raised when `Current` or preferences change. |

**Guarantees**: One `Current` at a time. Warning and Critical text is always renderable in every mode. No member touches seed, password or keys.

## `BakuMessage` component

Parameters: `MessageId`, optional `Compact`. Renders the pose image (or text-only when the mode is `Off`, the image fails to load, or the message is Warning or Critical in `Off` mode), title and body, and a dismiss control for dismissible messages. It reserves its own space and never overlays inputs. It uses a polite live region (assertive for Critical). Entrance transition and the success celebration are CSS-only and suppressed under `prefers-reduced-motion` (FR-013).

## `BakuWarningDialog` component

Shown by `ConfirmAsync`. Offers "Go back" and "I understand, continue". Escape and back-navigation count as cancel. The continue action performs the original action unchanged.

## `SendRiskEvaluator` (`DigiByte.Wallet`)

```
Evaluate(decimal amount, decimal balance, string destination,
         IReadOnlySet<string> knownAddresses) -> SendRiskAssessment { LargeAmount, NewAddress }
```

- `amount` excludes the network fee. `LargeAmount` is true when `balance > 0` and `amount / balance >= 0.5`.
- `NewAddress` is true when `destination` is not in `knownAddresses` (saved contacts plus previously sent addresses, compared case-appropriately per network).
- Pure and deterministic. It performs no I/O and no address validation (callers validate first).

## Screen hooks (behavioral contract)

| Screen | Message(s) |
|---|---|
| Welcome | Greeting (info) |
| CreateWallet / RecoverWallet | Step guidance (info) |
| BackupSeed | Why-backup (info); reveal warning (critical, confirmation) |
| BackupSeed confirmed / tx sent | Celebrating (info) |
| Unlock | Small greeting, hidden once password entry starts |
| Home / Receive / DigiDollar | Empty-state and tips (tip) |
| Send / DigiDollarSend | New-address and large-amount warnings (warning, confirmation) |
| P2PMarketplace | Verify-counterparty and scam warning (warning) |
| Error states | Thinking pose with friendly explanation (info) |
| Settings | Presence mode selector, "Reset tips" action |
