# Feature Specification: Install App from Settings

**Feature Branch**: `019-settings-install-app`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "help me add install button on the setting if not yet installed. Make sure it works on iOS and Android."

## Clarifications

### Session 2026-11-18

- Q: On desktop browsers that can install the wallet as an app, should Settings show the "Install app" item? → A: Yes. Show it on any browser that supports install, including desktop, using the native prompt; manual instructions apply only to iOS and Android.
- Q: On iOS, where the browser gives no signal when "Add to Home Screen" completes, should the item stay visible after the instructions are closed? → A: Yes. It stays visible until the wallet is launched from the home screen; immediate hiding applies only where the browser reports the install (Android, desktop).

## Context

The wallet can be installed to a phone's home screen as an app. Today the only install offer is a top banner that appears on some screens and, once dismissed, stays hidden for the rest of the session. Someone who dismissed it, or never noticed it, has no way to find the install option again. This feature adds a permanent, discoverable "Install app" entry in Settings that is shown only while the wallet is not yet installed, and that works on both Android and iOS devices. The existing banner behavior is unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install on Android from Settings (Priority: P1)

An Android user browsing the wallet in their browser opens Settings and sees an "Install app" item. Tapping it shows the device's native install confirmation; accepting it installs the wallet to the home screen, and the item then disappears from Settings.

**Why this priority**: Android is the platform with one-tap installation and the largest share of users who dismissed the banner; this is the core value.

**Independent Test**: On an Android device with the wallet open in a browser tab (not installed), open Settings, tap "Install app", accept the prompt, and confirm the app is on the home screen and the Settings item is gone.

**Acceptance Scenarios**:

1. **Given** the wallet is open in an Android browser and not installed, **When** the user opens Settings, **Then** an "Install app" item is visible.
2. **Given** the item is visible, **When** the user taps it, **Then** the device's native install confirmation appears.
3. **Given** the native confirmation appeared, **When** the user accepts, **Then** the wallet installs and the Settings item is no longer shown without a manual reload.
4. **Given** the native confirmation appeared, **When** the user cancels, **Then** nothing is installed and the Settings item stays available (it shows menu instructions if the browser will not offer the prompt again).
5. **Given** the user previously dismissed the top banner, **When** they open Settings, **Then** the "Install app" item is still shown.

---

### User Story 2 - Install on iOS from Settings (Priority: P1)

On iPhone and iPad there is no one-tap install; the user must use the browser's share menu. A user opens Settings, sees "Install app", and taps it to see short step-by-step instructions (tap the Share icon, then "Add to Home Screen", then "Add") with the share icon shown so they can recognize it.

**Why this priority**: iOS users have no other path and cannot install without guidance; equally important as Android per the request.

**Independent Test**: On an iPhone in Safari with the wallet not installed, open Settings, tap "Install app", read the instructions, follow them, and confirm the app appears on the home screen and, when launched from there, the Settings item is absent.

**Acceptance Scenarios**:

1. **Given** the wallet is open in a browser on iOS and not installed, **When** the user opens Settings, **Then** an "Install app" item is visible.
2. **Given** the item is visible on iOS, **When** the user taps it, **Then** clear numbered instructions appear including the share icon and the "Add to Home Screen" wording, and can be closed.
3. **Given** an iOS browser other than Safari, **When** the user taps the item, **Then** the same steps are shown together with a note to open the wallet in Safari if they do not see "Add to Home Screen".

---

### User Story 3 - No install option once installed (Priority: P2)

When the wallet is running as an installed app (launched from the home screen), Settings does not show the "Install app" item, so the user is never offered something they already have.

**Why this priority**: This is the "if not yet installed" half of the request; it prevents confusion.

**Independent Test**: Launch the installed app from the home screen on Android and on iOS, open Settings, and confirm no install item is present.

**Acceptance Scenarios**:

1. **Given** the wallet is launched from the home screen on Android or iOS, **When** the user opens Settings, **Then** no "Install app" item is shown.
2. **Given** the user installs the wallet on Android or desktop while Settings is open, **When** installation completes, **Then** the item disappears.
3. **Given** the user adds the wallet to the home screen on iOS, **When** they return to the browser tab, **Then** the item is still shown, and it is absent once they launch the installed app.

---

### User Story 4 - Browsers that cannot install (Priority: P3)

On a desktop or mobile browser that offers no install capability at all, the item is not shown, rather than showing a button that does nothing. On an Android browser that supports install but has not yet offered the native prompt, the item still gives the user a way forward (brief instructions to use the browser menu's "Install app" / "Add to Home screen").

**Why this priority**: Polish for less common browsers.

**Independent Test**: Open Settings in an unsupported browser and in an Android browser without a captured prompt; verify the item is hidden or shows menu instructions respectively.

**Acceptance Scenarios**:

1. **Given** a browser with no install support, **When** the user opens Settings, **Then** no install item appears.
2. **Given** a desktop browser that supports install and the wallet is not installed, **When** the user opens Settings, **Then** the "Install app" item is shown and opens the browser's native install confirmation.
4. **Given** a desktop or Android browser whose install prompt was already used or dismissed (for example through the top banner), **When** the user taps the item, **Then** browser-menu instructions are shown instead of nothing happening.
3. **Given** an Android browser where the native prompt is not available yet, **When** the user taps "Install app", **Then** short instructions for the browser menu are shown instead.

### Edge Cases

- User opens Settings on Android before the browser has offered install: the item shows menu instructions (Story 4) rather than a dead button.
- User cancels the native prompt: the item remains available; if the browser will not offer the prompt again, it shows menu instructions instead.
- Wallet is installed but opened in a regular browser tab: the wallet cannot reliably tell, so the item may still show; the prompt or instructions behave harmlessly.
- User is offline: the item and instructions still display (no network needed).
- Both the banner and the Settings item are present: installing via either must hide both.
- Dark and light themes and small phone screens: the item and instructions remain legible and fit without horizontal scrolling.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Settings MUST show an "Install app" item whenever the wallet is not running as an installed app and installation is possible or guidable on the current device.
- **FR-002**: Settings MUST NOT show the item when the wallet is running as an installed app.
- **FR-003**: On Android and on desktop browsers that support install, tapping the item MUST open the browser's native install confirmation when available.
- **FR-004**: On iOS, tapping the item MUST show step-by-step instructions for adding the wallet to the home screen, including the share icon and exact menu wording, plus guidance to use Safari when the current browser cannot install.
- **FR-005**: Where native install is unavailable but the platform supports installing (Android, or a desktop browser whose install prompt was already used or dismissed), tapping the item MUST show browser-menu instructions, so the item never becomes a button that does nothing.
- **FR-006**: The item MUST remain visible after the user dismisses the top install banner, and MUST NOT depend on the banner's dismissal state.
- **FR-007**: Where the browser reports a completed installation (Android, desktop), the item MUST disappear immediately without requiring a reload. On iOS, where no such signal exists, the item MUST remain visible, with its instructions still reachable, until the wallet is launched from the home screen.
- **FR-008**: Cancelling the native prompt MUST leave the wallet in its prior state with the item still available.
- **FR-009**: The item and its instructions MUST be usable in light and dark themes and on small phone screens.
- **FR-010**: Existing banner behavior MUST remain unchanged, and the item MUST NOT collect or transmit any user data.

### Key Entities

- **Install state**: whether the wallet is currently installed, installable via the native prompt, installable via manual steps, or not installable on this device.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user on Android can go from opening Settings to a completed install in 3 taps or fewer.
- **SC-002**: A user on iOS can reach the instructions from Settings in 2 taps or fewer and complete installation by following them without outside help.
- **SC-003**: The install item is shown in 100% of non-installed sessions on supported Android and iOS devices and in 0% of sessions running as an installed app.
- **SC-004**: Verified on at least one real Android device and one real iOS device before release, covering install, cancel, and already-installed cases, plus one desktop browser that supports install.
- **SC-005**: Users who dismissed the banner can still install from Settings 100% of the time.

## Assumptions

- The wallet already meets browser installability requirements and already detects installed mode; this feature reuses that behavior.
- The install item is one entry in the existing Settings page, not a new page.
- Detecting an installed app that is opened inside a normal browser tab is not required.
- On iOS, install is only possible through the browser share menu's "Add to Home Screen"; instructions reflect this.
- Wording is English, matching the rest of the app.
