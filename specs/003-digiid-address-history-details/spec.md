# Feature Specification: Digi-ID Address Visibility & Login History Details

**Feature Branch**: `003-digiid-address-history-details`

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "on the Scan Digi ID QR Code, can we display the address that we're using and can be copy, also on the login history to have a details view to displays the whole details of the signing happens"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and copy the signing address before approving (Priority: P1)

After scanning a Digi-ID QR code (or pasting a Digi-ID link), the user sees the confirmation card for the site. Today it shows the site and callback address but not which DigiByte address will identify them. The user wants to see that address in full, and copy it, before tapping Approve, so they can verify it is the identity they expect (for example, to register it with the site or compare with an existing account).

**Why this priority**: The address is the user's identity on the site. Showing it before consent is the core transparency gain and the direct request.

**Independent Test**: Scan or paste a Digi-ID link, and confirm the confirmation card shows the full signing address with a copy control; copy it and paste elsewhere to verify it is exact.

**Acceptance Scenarios**:

1. **Given** the user has scanned a valid Digi-ID QR code, **When** the confirmation card appears, **Then** the full address that will be used to sign in to that site is displayed, labeled clearly (e.g., "Signing address").
2. **Given** the address is displayed, **When** the user taps the copy control, **Then** the full, untruncated address is placed on the clipboard and the user sees brief confirmation that it was copied.
3. **Given** the user later signs in to the same site again, **When** the confirmation card appears, **Then** the same address is shown as before (identity is stable per site).
4. **Given** the user scans codes for two different sites, **When** each confirmation card appears, **Then** each shows its own address.
5. **Given** the user cancels instead of approving, **When** the card closes, **Then** nothing is signed or sent, and no history entry is created.

---

### User Story 2 - Open a detailed view of any past login (Priority: P1)

In the Login History list, each entry shows only the site, a shortened address, and a time. The user wants to tap an entry and see the full details of that sign-in: what was signed and where it went, so they can audit their activity.

**Why this priority**: Equal to the direct request; gives the user an auditable record of every identity proof they have given.

**Independent Test**: Complete a sign-in, tap its entry in Login History, and verify the details view shows all recorded information and can be closed.

**Acceptance Scenarios**:

1. **Given** the Login History has entries, **When** the user taps an entry, **Then** a details view opens showing, at minimum: site/domain, full callback address (URL), full signing address, the challenge (nonce) that was signed, the signature, the exact date and time, and the outcome.
2. **Given** the details view is open, **When** the user taps the copy control next to a value (address, signature, challenge, callback URL), **Then** the full value is copied and the user sees confirmation.
3. **Given** the details view is open, **When** the user closes it, **Then** they return to the Login History list in the same position.
4. **Given** a site was accessed over an insecure (HTTP) connection, **When** the user views that entry's details, **Then** the insecure connection is flagged.
5. **Given** an entry was saved before this feature existed and lacks some fields, **When** the user opens its details, **Then** the available fields (site, address, time) are shown and missing ones are omitted or marked "Not recorded"; nothing breaks.

---

### User Story 3 - Copy address from the login success card (Priority: P3)

The "Authenticated!" success card shows the address truncated with no way to copy it. The user can copy the full address from the success card.

**Why this priority**: Convenience; the main needs are covered by Stories 1 and 2.

**Independent Test**: Complete a sign-in and copy the address from the success card.

**Acceptance Scenarios**:

1. **Given** a sign-in has just succeeded, **When** the success card is shown, **Then** the full address used is shown with a copy control.

---

### Edge Cases

- Scanned code is not a valid Digi-ID link: existing invalid-link error is shown; no address is displayed.
- Wallet is locked or no wallet exists when the confirmation card would show: no address is shown and the existing wallet-error behavior applies.
- Very long values (signature, callback URL) must be fully viewable in the details view (wrapped or scrollable) without breaking the layout on small phone screens.
- Clipboard access is denied or unavailable: the user is told the copy failed and the value remains selectable for manual copy.
- Failed sign-in attempts are not currently kept in history (only successful logins are listed); this feature does not change that.
- User clears history: all details are removed together with the list entries.
- History is capped at the most recent entries; details of dropped entries are removed with them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On the Digi-ID confirmation card, the system MUST display the full address that will be used to sign in to the requesting site, before the user approves.
- **FR-002**: The system MUST provide a copy control for that address that copies the full, untruncated value and shows brief feedback.
- **FR-003**: Displaying the address MUST NOT require signing, sending anything to the site, or exposing any private key or seed material.
- **FR-004**: The address shown before approval MUST equal the address actually used when the user approves.
- **FR-005**: Each Login History entry MUST be openable to a details view.
- **FR-006**: The details view MUST show: domain, full callback URL, full signing address, signed challenge (nonce), signature, exact date and time, whether the connection was secure, and the outcome (only successful sign-ins are recorded, so the outcome is always shown as "Success").
- **FR-007**: Each long value in the details view (signing address, challenge, signature, callback URL) MUST have its own copy control with feedback.
- **FR-008**: The system MUST persist the additional signing details with each successful login history entry, in the same device-local storage as the existing history.
- **FR-009**: History entries saved before this feature MUST remain visible and openable, with unavailable fields clearly omitted or marked.
- **FR-010**: The details view MUST be closable and return the user to the list; it MUST be usable on narrow phone screens and in both light and dark themes.
- **FR-011**: The success card MUST show the full address used with a copy control.
- **FR-012**: Signing details MUST remain on the user's device only; clearing login history MUST remove them.

### Key Entities

- **Signing Address**: The DigiByte address that identifies the user to one specific site; stable per site; derived from the wallet without revealing the wallet's main keys.
- **Login History Entry**: Record of one successful Digi-ID sign-in: domain, callback URL, signing address, challenge, signature, timestamp, security (HTTP/HTTPS) flag, outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Digi-ID confirmation cards for a valid code show the full signing address with a working copy control.
- **SC-002**: A user can open the details of any past login in two taps or fewer from the Identity screen.
- **SC-003**: Copied values match the displayed values exactly (character for character) in 100% of tests.
- **SC-004**: The address shown before approval matches the address recorded in history for that sign-in in 100% of cases.
- **SC-005**: Existing history entries from before this feature continue to display with no errors or data loss.
- **SC-006**: The details view is fully readable without horizontal page scrolling on a 360px-wide screen.

## Assumptions

- The signing address is deterministic per site, so it can be derived and shown before approval without any network call.
- Showing the address and signature locally does not weaken the self-custody model: the address and signature are public data already sent to the site; no private keys or seed are shown.
- Only successful logins appear in Login History today; adding failed-attempt history is out of scope.
- History remains device-local with the existing cap on number of entries; no cloud sync.
- Existing copy-to-clipboard feedback conventions elsewhere in the app will be reused for consistency.
- Applies to the web wallet's Identity screen only.
