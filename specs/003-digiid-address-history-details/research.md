# Research: Digi-ID Address Visibility & Login History Details

## R1 — How to show the address before signing

- **Decision**: Add `DigiIdService.GetAddress(Key)` and make `Sign()` use it; add `WalletService.GetDigiIdAddress(string domain)` that does `DeriveSiteIndex` → `DeriveDigiIdKey` → `GetAddress`.
- **Rationale**: The key path `m/13'/siteIndex'/0'/0` is deterministic per domain, so no signing or network is needed. A single shared function prevents the pre-approval and signed addresses from diverging (FR-004).
- **Alternatives**: Sign first and then display (rejected: the address must be visible before consent); duplicate the derivation in the page (rejected: drift risk).

## R2 — What to store per history entry

- **Decision**: Add optional `CallbackUrl`, `Uri`, `Nonce`, `Signature` and `IsUnsecure` fields to the stored entry. Outcome is implicitly "Success" (only successes are recorded) and shown as such.
- **Rationale**: Covers every field the spec lists; signature and nonce are public data already sent to the site. Nullable fields keep existing stored JSON deserializing (FR-009).
- **Alternatives**: Store failed attempts too (rejected: out of scope); re-derive details on open (rejected: the record should reflect exactly what was sent, and the signature needs an unlocked wallet to recompute).

## R3 — Details UI pattern

- **Decision**: New modal component styled like `TransactionDetailModal` (overlay + card), with a per-row copy button using `navigator.clipboard.writeText` and a short "Copied" state; failures show a message and leave the text selectable.
- **Rationale**: Matches existing UX, CSP-safe, no new dependencies.
- **Alternatives**: Expand-in-place accordion (rejected: long signature and URL are cramped at 360px); separate route (rejected: heavier navigation for a read-only view).

## R4 — Pre-feature entries

- **Decision**: Treat missing fields as null and render "Not recorded"; no migration.
- **Rationale**: Nothing can be recovered for old entries; non-destructive.

All Technical Context items are resolved; no NEEDS CLARIFICATION remain.
