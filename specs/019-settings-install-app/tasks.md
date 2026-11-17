---

description: "Task list for Install App from Settings"
---

# Tasks: Install App from Settings

**Input**: Design documents from `/specs/019-settings-install-app/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/ui-and-interop.md](contracts/ui-and-interop.md), [quickstart.md](quickstart.md)

**Tests**: No automated test tasks. The spec requests none and `DigiByte.Web` has no component-test project (plan, Testing). Verification is the manual device matrix in [quickstart.md](quickstart.md), included as tasks below.

**Organization**: Grouped by user story. Both code files (`pwa-install.js`, `Settings.razor`) are shared across stories, so stories build on the same row and are not parallel to each other; `[P]` marks only tasks touching different files.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Create the working branch `feature/settings-install-app` from `main` (constitution: Branching), then confirm baseline: read `src/DigiByte.Web/wwwroot/js/pwa-install.js` and `src/DigiByte.Web/Components/PwaInstallBanner.razor` and note the banner's calls (`registerCallback`, `prompt`, `isStandalone`, `[JSInvokable] OnInstallAvailable / OnIosDetected / OnAppInstalled`) that must keep working unchanged

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Make the install state shareable by more than one consumer without breaking the banner (FR-006, FR-010).

- [X] T002 In `src/DigiByte.Web/wwwroot/js/pwa-install.js`, replace the single `window._pwaInstallCallback` slot with a set of subscribers: `registerCallback(dotnetRef)` adds to the set (still calls `OnInstallAvailable` at once if a prompt is held, and `OnIosDetected` on iOS when not standalone); `beforeinstallprompt` and `appinstalled` notify every subscriber, and one subscriber throwing must not stop the others
- [X] T003 In `src/DigiByte.Web/wwwroot/js/pwa-install.js`, add `unsubscribe(dotnetRef)` (safe to call twice), `isAndroid()` (user-agent check) and `getState()` returning `{ standalone, ios, android, canPrompt, nonSafariIos, promptSeen }` as booleans, synchronous, no side effects; `promptSeen` is true once a `beforeinstallprompt` was captured in this page session, even after the prompt is used; `nonSafariIos` is true for iOS user agents with CriOS, FxiOS, EdgiOS, OPiOS, or no `Safari` token
- [X] T004 In `src/DigiByte.Web/wwwroot/js/pwa-install.js`, keep `prompt()` returning whether the install was accepted (it already clears `deferredPrompt`), and after it is used without an install, notify every subscriber with `OnPromptUsed`, invoked defensively so a subscriber without that method (the banner) is ignored and does not break; keep `appinstalled` notifying `OnAppInstalled`

**Checkpoint**: Banner behaviour unchanged (verify later in T019); `getState()` usable from Settings.

## Phase 3: User Story 1 - Install on Android from Settings (Priority: P1) 🎯 MVP

**Goal**: An Android (and installable desktop) user taps "Install app" in Settings, sees the native prompt, installs, and the row disappears.

**Independent Test**: Quickstart scenario A (and C step 1).

- [X] T005 [US1] In `src/DigiByte.Web/Pages/Settings.razor` add a small "App" group heading (same markup as the other `<h3>` group headings) after the "Display" group and before "Network & advanced", rendered only when the install row is visible (contract: UI)
- [X] T006 [US1] In `src/DigiByte.Web/Pages/Settings.razor` add the "Install app" row as a button using the same card markup as neighbouring rows, with an inline download-to-device SVG, title "Install app", subtitle "Install for quick access", accessible name equal to the title
- [X] T007 [US1] In the `@code` block of `src/DigiByte.Web/Pages/Settings.razor`, add install state and a `DotNetObjectReference` to this page; in `OnAfterRenderAsync(firstRender)` read `pwaInstall.getState()`, compute the row mode from the data-model table (Hidden, Native, IosSteps, MenuSteps; first match wins) and call `pwaInstall.registerCallback`
- [X] T008 [US1] In `src/DigiByte.Web/Pages/Settings.razor` implement the `Native` tap: call `pwaInstall.prompt`; on accept hide the row; on cancel re-read `getState()` so the row falls back to `MenuSteps` on Android and desktop alike (never `Hidden`, never a dead button; FR-005, FR-008); this task also builds the `MenuSteps` dialog "Install this app" it falls back to (same dialog pattern as T012: `role="dialog"`, `aria-modal="true"`, close button, tap-outside and Escape to close), with steps "Open the browser menu (three dots)", "Choose Install app, Add to Home screen or Install DGB Wallet", "Confirm"; the same dialog opens when the row is tapped in `MenuSteps` mode on Android
- [X] T009 [US1] In `src/DigiByte.Web/Pages/Settings.razor` add `[JSInvokable] OnInstallAvailable` (switch to `Native` without reload), `[JSInvokable] OnAppInstalled` (hide row immediately, FR-007), `[JSInvokable] OnPromptUsed` (re-read `getState()` and switch to `MenuSteps`, so a prompt used through the top banner does not leave a stale `Native` row) and `[JSInvokable] OnIosDetected` (no-op or mode refresh), plus `IDisposable`/`IAsyncDisposable` that calls `pwaInstall.unsubscribe` and disposes the reference
- [ ] T010 [US1] Verify on a real Android device and on desktop Chrome or Edge per quickstart A and C step 1: row visible after dismissing the banner (FR-006), native prompt opens, cancel keeps the row usable, accept hides it without reload, launching the installed app shows no row; also dismiss the prompt through the top banner and confirm the Settings row switches to menu steps, and confirm Settings is reached in 3 taps or fewer from opening the wallet menu to a completed install (SC-001); do not merge or release this MVP alone, because on iOS the row would do nothing until T011 to T014 land (US2 ships with US1)

**Checkpoint**: Android and desktop install work from Settings; MVP shippable.

## Phase 4: User Story 2 - Install on iOS from Settings (Priority: P1)

**Goal**: iPhone and iPad users see the row and get clear Add to Home Screen steps.

**Independent Test**: Quickstart scenario B.

- [X] T011 [US2] In `src/DigiByte.Web/Pages/Settings.razor` make the row subtitle "Add to your home screen" when the mode is `IosSteps`, and make the tap open an instructions dialog instead of calling `prompt`
- [X] T012 [US2] In `src/DigiByte.Web/Pages/Settings.razor` add the iOS instructions dialog following the existing language-toast dialog pattern (`role="dialog"`, `aria-modal="true"`, fixed overlay, close button and tap-outside to close, dark-mode classes): title "Install on iPhone / iPad", numbered steps 1 "Tap the Share icon" (inline Share glyph), 2 "Scroll and tap Add to Home Screen", 3 "Tap Add"; when `nonSafariIos` also show "Don't see Add to Home Screen? Open this page in Safari."; the dialog also closes with the Escape key
- [X] T013 [US2] In `src/DigiByte.Web/Pages/Settings.razor` ensure that on iOS the row stays visible after the dialog is closed and after returning from the share sheet (FR-007 iOS clause) and only hides when `getState().standalone` is true on a fresh launch
- [ ] T014 [US2] Verify on a real iPhone or iPad in Safari and in Chrome for iOS per quickstart B: row visible, steps readable, row stays after returning, row absent when launched from the home screen, non-Safari note appears in Chrome for iOS, and the steps are reached in 2 taps or fewer from Settings (SC-002)

**Checkpoint**: iOS flow complete; stories 1 and 2 together cover the headline request on both platforms.

## Phase 5: User Story 3 - No install option once installed (Priority: P2)

**Goal**: Never offer install inside the installed app.

**Independent Test**: Launch the installed app on Android and iOS, open Settings, no row and no "App" heading.

- [X] T015 [US3] In `src/DigiByte.Web/Pages/Settings.razor` confirm the `Hidden` mode removes both the row and the "App" heading when `standalone` is true, and that `OnAppInstalled` while Settings is open removes them live
- [ ] T016 [US3] Verify per quickstart A step 4 and B step 4 on real devices that no install row appears in the installed app on Android or iOS

## Phase 6: User Story 4 - Browsers that cannot install (Priority: P3)

**Goal**: No dead buttons; helpful guidance where a prompt is not available.

**Independent Test**: Quickstart C step 2, plus Android without a captured prompt.

- [X] T017 [US4] In `src/DigiByte.Web/Pages/Settings.razor` confirm the `MenuSteps` dialog built in T008 is used on Android without a captured prompt and on desktop after the prompt was used or dismissed (subtitle "Install for quick access"), and that the row is not rendered at all on desktop browsers that never offered a prompt (Firefox, desktop Safari)
- [ ] T018 [US4] Verify per quickstart C step 2 and an Android browser without an offered prompt: no row on desktop browsers that never offered a prompt, menu steps on Android without a prompt and on desktop after the prompt was dismissed

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Regression check of `src/DigiByte.Web/Components/PwaInstallBanner.razor` (unchanged file) per quickstart D: banner still appears, installs and dismisses on Android and desktop, and installing through either the banner or Settings hides both (FR-010)
- [ ] T020 [P] Layout and theme check per quickstart E: row and both dialogs in light and dark themes at 320 px width with no horizontal scroll, and offline after load (FR-009)
- [X] T021 [P] Search `README.md` and `docs/` (for example `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`) for mentions of PWA install and update any that describe how to install the wallet to include the Settings entry; if none exist, note that in the pull request (constitution: Documentation)
- [ ] T022 Record device, OS and browser versions used for Android, iOS and desktop in the pull request description (SC-004), and use a Conventional Commit title such as `feat(settings): add install app option`
- [ ] T023 Run `dotnet build` for `src/DigiByte.Web` and `dotnet test` for the affected solution to meet the merge gates in the constitution

## Dependencies & Execution Order

- Phase 1 → Phase 2 → stories. Phase 2 blocks everything.
- US1 (T005–T010) is the MVP and builds the row, state, and event handling. US2, US3, US4 extend the same row and dialogs in `Settings.razor`, so do them after US1 and in order; they are not parallel with each other.
- US3 (T015) is mostly confirmation of rules implemented in T007 and T009.
- Polish after all stories; T019, T020 and T021 are [P] (separate checks, no file edits).

## Parallel Opportunities

- Little code parallelism exists because only two files change, and Phase 2 (JS) must precede the Razor work.
- T019 and T020 can run at the same time on different devices.
- Device verification tasks (T010, T014, T016, T018) can be split between people holding an Android phone, an iPhone and a desktop.

## Implementation Strategy

- **MVP**: Phases 1 to 3 (T001 to T010): Settings row with native install on Android and desktop.
- **Then**: US2 for iOS (also P1 per the request to work on both platforms; do not release without it), US3, US4, polish.
- Each phase ends with a device checkpoint, so a regression is caught before the next layer is added.
