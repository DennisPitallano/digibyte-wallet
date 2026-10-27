# UI Contract: Identity page

## Confirmation card (pending Digi-ID request)

- Shows label "Signing address", the full address (wrapped, monospace) and a copy button.
- Address comes from `WalletService.GetDigiIdAddress(domain)`. If unavailable (locked or watch-only) the block is hidden and the existing wallet error applies on Approve.
- Copy: clipboard receives the exact full address; button shows "Copied" for about 2s; on failure it shows "Copy failed" and the text stays selectable.

## Success card

- "Address used" row shows the full address (wrapped) with a copy button.

## Login History row

- Whole row is a button (keyboard-focusable, `aria-label` "View details for {domain}"); tap opens the details modal. Existing row content is unchanged.

## Details modal (`DigiIdLoginDetailModal`)

Parameters: `IsOpen`, `Entry`, `OnClosed`.

Fields, each with label and wrapped value; copy button where noted:

Site (domain) · Outcome ("Success") · Date & time (full, with seconds) · Connection (Secure / Insecure flag) · Callback URL (copy) · Signing address (copy) · Challenge/nonce (copy) · Signed URI (copy) · Signature (copy).

Missing values display "Not recorded" with no copy button. Close via button, overlay tap or Escape; focus returns to the originating row.
