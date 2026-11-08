# Contract: UI

## 1. `DigiIdAddressWarning` (shared component)

Parameters: none required. Optional `Compact` for the narrow modal.

Content (localized, key prefix `identity.addrWarning.`):

- States all three facts from FR-002: this is an identity address for signing in, the wallet does not watch it, and it must not be used to receive payments.
- Ends with a link "Sent funds here by mistake? Recover them" that goes to `/identity#recover-funds`.
- Uses the wallet's existing warning styling. It is always visible and not dismissible.

### Placements (FR-001, SC-001)

| # | Where | Anchor |
|---|---|---|
| 1 | Identity page, approval panel that shows `pendingAddress` | Directly under the address row, before the Approve button |
| 2 | Identity page, success panel that shows `lastAuthAddress` | Directly under the address row |
| 3 | `DigiIdLoginDetailModal`, the "Signing address" field | Directly under the field |
| 4 | Help, entry "Is Digi-ID safe?" and a new entry "Can I receive funds to my Digi-ID address?" | In the answer text, with a link to the recovery section |

Each placement sits in the same block as the `CopyValueButton`, so the warning is visible when the address is copied (scenario 6).

**Exempt (FR-001):** the Login History list row on the Identity page shows only a shortened address and cannot copy it. Tapping the row opens placement 3, which carries the warning.

The Help entry is hard-coded English like the rest of `Help.razor` (research R-9).

## 2. `DigiIdRecoverFunds` (section on the Identity page)

Rendered at the bottom of `Identity.razor` with element id `recover-funds`. Strings use keys under `identity.recover.`.

### States

| State | Shown when | Content | Actions |
|---|---|---|---|
| Locked | Wallet locked | "Unlock your wallet to check" | Unlock (existing unlock route, returning to `/identity#recover-funds`) |
| Unsupported | Watch-only or single-key (WIF) wallet | "Needs a wallet with a seed phrase" | None |
| Ready | Unlocked HD wallet | Short explanation, domain input, "Check for funds" | Check |
| Checking | A scan is running | Progress and the count of sites checked | Cancel |
| Nothing found | All findings `Empty` | "No funds found on N sites" | Check again |
| Results | At least one `Funds` finding | Per site: domain, address, confirmed, pending. Total. Failed lookups listed as "could not check" with a Retry for those only. | Select addresses, Sweep selected, Retry failed |
| Check failed | Every lookup failed | Clear error, never shown as "nothing found" | Retry |
| Review | A plan was built | Per item: amount, estimated fee, net, destination (each different). Totals. "N more after these confirm" when capped. Items excluded as "too small" listed with the reason. | Confirm, Cancel |
| Sending | Execution running | Per item status | None |
| Status | Sweep records exist | Per record: domain, amount, status (Sending, Pending, Confirmed, Failed) and the reason for Failed. Persists across reloads (FR-011a). | Retry for a failed item |

### Inputs

- **Domain field**: accepts a bare domain or a pasted URL. On invalid input, shows an inline message and does not add it (FR-004a). The entered domains are checked together with the login-history domains. An empty history shows a prompt to add domains by hand.

### Behaviour rules

- Opening the section runs `RefreshDigiIdSweepsAsync` so earlier sweeps show their current status.
- A "Refresh" button runs it on demand. While the section is open and at least one record is `Pending`, it also runs about every 30 seconds. The timer stops when the section closes or nothing is pending (FR-011, FR-014).
- If the wallet locks during a sweep, the items not yet sent show "Failed: wallet locked" with a Retry that asks for an unlock first.
- A finding with `OtherOutputsCount` above zero shows "N other outputs left untouched" next to it. These outputs are never part of an amount, a fee or a sweep.
- The Sweep button is disabled while a plan or execution is running (double click guard, FR-012).
- Cancelling at Review sends nothing and saves nothing.
- Addresses with an in-flight record are shown as "being swept" and cannot be selected.
- Unconfirmed amounts are labelled "pending, can be swept once confirmed".
- No private key, derivation path or raw transaction is ever displayed.

### Accessibility and layout

- Works at phone width with no horizontal scroll, matching the rest of the Identity page.
- Status uses text, not colour alone.
- Buttons are reachable by keyboard with visible focus, as the existing history rows are.

## 3. Localization keys

Added to `en.json` and `es.json` (other locales fall back to English):

- `identity.addrWarning.title`, `.body`, `.link`
- `identity.recover.*` for the section title, states, inputs, review and status labels, and error messages
