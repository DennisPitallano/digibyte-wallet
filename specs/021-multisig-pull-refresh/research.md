# Research: Pull-Down Refresh on Multi-Sig Dashboard

## R1. Reuse the existing component
- **Decision**: Reuse `Components/Shared/PullToRefresh.razor` (already used by `/transactions`).
- **Rationale**: It already arms only at the top of the scroll (`domHelpers.isScrolledToTop` on `<main>`), has an 80px threshold, a spinner and a haptic tick, and ignores new touches while refreshing. That covers FR-001, FR-003, FR-005 and FR-008, and part of FR-006.
- **Alternatives**: New gesture code (rejected: duplicates logic and drifts from the Activity page).

## R2. Failure signalling
- **Finding**: `LoadBalance` and `LoadActivity` in `MultisigDetail.razor` swallow exceptions (balance becomes `null`/"unknown"; activity sets `activityPartial`), and `Refresh()` returns nothing, so failure is invisible and a failed balance fetch blanks the balance.
- **Decision**: Add a pull-specific routine that runs the same two loads, detects failure, leaves the previous balance and activity in place (via `keepPreviousOnFailure`), and shows one `Notifications.Error("Couldn't refresh")` toast. The existing button path (`Refresh`) keeps its current behaviour (spec FR-007, FR-009).
- **Implementation note**: Prefer extracting each load into a method that returns success or failure, called by both paths; the button path ignores the result.
- **Open check (task T003)**: `GetActivityAsync` has a partial-result path (`IsPartial = true`), so offline may not throw. Confirm which, and treat a partial result with no entries as a failure.
- **T003 finding**: offline, `GetActivityAsync` does not throw. It returns local proposals only with `IsPartial = true`. The page treats `IsPartial` as the failure signal, so no change to `DigiByte.Wallet` was needed.
- **No flash of "unknown"**: the load methods take a `keepPreviousOnFailure` option, so the old balance is never overwritten and then restored.
- **Alternatives**: Change `LoadBalance` globally to keep the old value (rejected: alters button behaviour, out of scope).

## R3. Overlays
- **Finding**: The component arms from touches on its wrapper `div`. The dialogs (`ReceiveSheet`, `RenameDialog`) render after the page body, but the co-signers panel and the inline remove confirmation sit inside it. Touches inside the wrapper can arm the gesture, which is harmless unless the user is at the top of the page with one of them open.
- **Decision**: Keep the dialogs as siblings after `</PullToRefresh>` (as `/transactions` does with its detail modal) and confirm manually (quickstart step 4). Only if that fails, touch the shared component to ignore touches that start inside an overlay.

## R4. One refresh at a time
- **Finding**: The component blocks repeat pulls while it is refreshing, but a button tap and a pull can overlap.
- **Decision**: Add a page-level `isRefreshing` flag checked by both the pull routine and the button path, so only one refresh runs (FR-006).

## R5. Data freshness
- **Finding**: Balance reads go through a 30-second cache in `FallbackBlockchainService`, and `IBlockchainService` exposes no way to clear it.
- **Decision**: Accept the same freshness as the existing button ("same data as the existing refresh action", FR-002). Clearing the cache is out of scope; it can be a follow-up if a pull right after receiving funds feels stale.

## R6. Scope of data
- **Decision**: Reload balance and recent activity only. Fiat price and pending-proposal counts are not part of the existing refresh and stay out of scope (spec Assumptions).

## R7. Testing
- **Finding**: No bUnit or browser-test project in the repo.
- **Decision**: Keep the page logic small; verify the gesture manually in a touch-emulated viewport using the quickstart. If the "restore previous values on failure" logic is extracted into a plain class, add a unit test for it.
