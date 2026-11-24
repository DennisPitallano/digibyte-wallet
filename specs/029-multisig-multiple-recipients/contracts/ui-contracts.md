# Contract: Screens

Each screen's expected behaviour, for review and manual checks. All text is in English and Spanish. Style follows `docs/UI_STYLE.md`.

## Shared wallet Send (`/multisig/{id}/send`, `MultisigSend.razor`)

| State | Shows |
|---|---|
| Default | Single-recipient form exactly as today, plus a **Multiple recipients** toggle and a "?" explainer, like the main Send page |
| Toggle on | `RecipientRows`: "Recipient 1", "Recipient 2" (each an address field with paste/scan/contacts and an amount field), a counter "N / 20 recipients", **+ Add recipient** (hidden at 20), and remove on each row when there are more than 2. Quick shares and Max are hidden. Fee rate (advanced) and memo stay |
| Typing | Row errors (from `ValidateRecipients`) appear once that row has something in it. Fee, total, total with fee and remaining update live. When the total plus fee is more than the balance: "Not enough in this shared wallet: X DGB short" |
| Toggle off and on again | Rows come back as they were |
| Scan for row k | The one scanner overlay opens. The result fills row k only (address, plus amount when it's a payment link) |
| Scan menu → Send with `?address=&amount=` | Fills the single-recipient form, as today |
| Review | A numbered list of every recipient (contact name from this device when known, plus the grouped address) and its amount, then the total, network fee, total with fee and remaining |
| Done | "Payment proposal created", the total, "to N recipients" with the list, the signatures needed, the notice "Co-signers need an up-to-date wallet to open a payment to several recipients" (only when N ≥ 2), then sign now / share / open room as today |

The Continue button is disabled while any row has an error, there are fewer than 2 rows, or the estimate fails.

## Pending list (`MultisigPending.razor`, `PendingCard.razor`)

- With one recipient: unchanged ("to dgb1q…xyz").
- With several: the total and "to N recipients". The screen reader label says the same.
- The received-proposal banner and the restore-cancelled prompt use the same wording.

## Pending detail (`MultisigPendingDetail.razor`)

- `PaymentSummary` lists every recipient, with a copy button on each address, and the change returning to the shared wallet (read from the payment; today this screen shows no change line).
- A rule-breaking line (received proposals only) shows an amber note under it: "Below the 0.001 DGB minimum", "This address is paid more than once", or, above the list, "This payment has more than 20 recipients".
- The sign confirm prompt reads "Sign the payment of X DGB to N recipients?" when N ≥ 2.
- Share: Copy and Save as file are always shown (as today). The QR code is shown only when `QrCapacity.FitsProposal(psbt)` (the existing 2,200-character limit). Otherwise the existing `ms.pendingDetail.share.too_large` line appears, reworded in both languages to "This proposal is too large for a QR code. Use Copy, Save as file, or sign together in a signing room."

## Signing room (`MultisigSigningRoom.razor`)

- The same `PaymentSummary`, read from the room's PSBT, with warnings.
- The confirm prompt has the same N-recipient wording.

## Activity (`MultisigActivity.razor`, `ActivityItem.razor`)

- Pending, sent, cancelled and failed proposal lines show the first address plus "+N more" (existing text).
- The detail sheet, for an entry with two or more `Recipients` (proposals, or sent transactions read from the chain), lists every address and amount under "To", then the total and the fee. Entries with one recipient look as they do today.
