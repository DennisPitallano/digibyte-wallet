# Feature Specification: Pull-Down Refresh on Multi-Sig Wallet Dashboard

**Feature Branch**: `021-multisig-pull-refresh`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "Help me implement a pull-down refresh on the multi-sig wallet dashboard"

## Clarifications

### Session 2026-11-18

- Q: Which screen gets pull-down refresh? → A: Single multi-sig wallet page only (`/multisig/{id}`); the wallet list is out of scope.
- Q: Pull during an in-progress refresh? → A: Ignore it; no queued follow-up refresh.
- Q: What does the user see when a pull refresh fails? → A: A brief "couldn't refresh" message; previously shown data stays on screen. The refresh button's behaviour is unchanged.
- Q: If the balance fetch fails on a pull refresh, keep the last balance or show "unknown"? → A: Keep the last known balance and show the message.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pull down to refresh the dashboard (Priority: P1)

A co-signer opens a multi-sig wallet's dashboard on their phone (installed app or mobile browser). They want to see the latest balance and recent activity without hunting for a refresh button. They pull the page down from the top, see a visual indicator, release, and the dashboard reloads its data.

**Why this priority**: Pulling down to refresh is the expected mobile gesture; the dashboard currently only offers a button-based refresh, and the wallet's transaction list already supports the gesture, so its absence on the dashboard is inconsistent.

**Independent Test**: Open a multi-sig wallet dashboard on a touch device, pull down past the threshold from the top of the page, release, and confirm the balance and activity update and the indicator disappears.

**Acceptance Scenarios**:

1. **Given** the dashboard is scrolled to the top, **When** the user pulls down past the refresh threshold and releases, **Then** a loading indicator is shown and the dashboard's balance and activity are reloaded.
2. **Given** a refresh completes, **When** new data is available, **Then** the dashboard shows the updated values and the indicator is dismissed.
3. **Given** the user pulls down but not past the threshold, **When** they release, **Then** no refresh happens and the indicator retracts.

---

### User Story 2 - No interference with normal scrolling (Priority: P2)

A user scrolls through the dashboard's activity. Scrolling up and down, and pulling inside the page content when not at the top, must never trigger a refresh accidentally.

**Why this priority**: An accidental or unreliable gesture would be worse than none.

**Independent Test**: Scroll the dashboard down, then scroll back up; confirm no refresh triggers until the user starts a pull from the very top.

**Acceptance Scenarios**:

1. **Given** the dashboard is scrolled partway down, **When** the user swipes down, **Then** the page scrolls normally and no refresh is triggered.
2. **Given** a modal, QR panel or other overlay is open on the dashboard, **When** the user swipes down, **Then** no dashboard refresh is triggered.

---

### User Story 3 - Clear feedback on refresh failure or repeat pulls (Priority: P3)

If the refresh cannot complete (offline, network error), the user is told, and the existing data stays visible. Pulling again while a refresh is in progress does not start a second refresh.

**Why this priority**: Robustness polish on top of the core gesture.

**Independent Test**: Go offline, pull to refresh, and confirm an error message appears with previous data intact.

**Acceptance Scenarios**:

1. **Given** the device is offline, **When** the user pulls to refresh, **Then** the indicator ends, existing data remains, and the user sees a brief "couldn't refresh" message.
2. **Given** a refresh is in progress, **When** the user pulls down again, **Then** no additional refresh starts.

---

### Edge Cases

- Desktop users with a mouse: the existing refresh button remains the way to refresh; the gesture is not required to work with a mouse.
- User uses the existing refresh button and the gesture in quick succession: only one refresh runs at a time.
- The dashboard is still loading for the first time when the user pulls: no overlapping load starts; the pull is ignored until the first load finishes.
- Wallet has no activity: pull-to-refresh still works and shows an empty state afterwards.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The multi-sig wallet dashboard MUST support a pull-down-from-top gesture on touch devices that triggers a data refresh.
- **FR-002**: The refresh MUST reload the same data as the dashboard's existing refresh action (balance and recent activity).
- **FR-003**: The system MUST show a visible pull indicator while pulling and a loading state while the refresh runs, and dismiss both on completion.
- **FR-004**: The gesture MUST only activate when the page is scrolled to the top, and MUST NOT trigger while an overlay or dialog is open. Expanding an inline panel or confirmation (for example the co-signers panel) does not trigger a refresh.
- **FR-005**: Releasing before the refresh threshold MUST cancel the gesture without refreshing.
- **FR-006**: Only one refresh MAY run at a time; additional pulls or button taps during a refresh MUST be ignored (not queued).
- **FR-007**: On failure, the system MUST keep previously shown data visible (including the last known balance, not "unknown") and show a brief "couldn't refresh" message. The existing refresh button's behaviour is not changed by this feature.
- **FR-008**: The gesture's look and feel MUST match the pull-to-refresh already used on the transaction list.
- **FR-009**: The existing refresh button MUST continue to work as it does today, except that it is ignored while another refresh is running (FR-006).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can refresh the multi-sig dashboard with one pull-and-release gesture, with no other taps needed (checked by quickstart steps 1-2).
- **SC-002**: Normal scrolling and swiping inside open panels or dialogs never trigger a refresh (checked by quickstart steps 3-4).
- **SC-003**: After a successful pull refresh, the dashboard shows data identical to what the existing refresh button would show.
- **SC-004**: The gesture behaves consistently with the transaction list's pull-to-refresh in side-by-side comparison on the same device.

## Assumptions

- "Multi-sig wallet dashboard" means the single multi-sig wallet's detail page (balance card and activity). The multi-sig wallet list page is explicitly out of scope (confirmed in clarification).
- The app's existing pull-to-refresh behaviour (used on the transaction list) is the reference for visuals and threshold.
- Primary target is touch devices (installed PWA and mobile browsers); desktop continues to use the refresh button.
- The existing refresh action reloads balance and activity only (not fiat price or proposals) and fails silently; the pull gesture reuses that data scope but adds failure feedback.
