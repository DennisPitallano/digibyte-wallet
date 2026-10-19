# Research: Baku Mascot — Guide & Guardian

## 1. Turning the supplied sheets into usable poses

- **Decision**: Cut each labelled pose from the three green-screen sheets into its own transparent WebP, using a one-off local script (chroma-key the green background, crop per pose, downscale to about 256 px and 512 px). Commit the outputs under `wwwroot/images/mascot/poses/`; keep the original sheets as source art but exclude them from the published/precached output.
- **Rationale**: FR-015 requires clean transparent per-pose images. The sheets are about 2752x1536 JPEGs and far too heavy to ship. A one-off script keeps the build dependency-free.
- **Alternatives**: Ship sheets as CSS sprites (heavy, green fringing, no transparency). Request redrawn vector art (slow, outside scope).
- **Open item**: Green-screen edges (spill on wings and glow effects) need a visual check. If keying quality is poor, fall back to a soft-edge matte or ask the artist for transparent exports. Sheets 1 and 2 are viewed. Sheet 3 is assumed to follow the same layout and must be confirmed when slicing.

## 2. Initial pose set (about 20)

- **Decision**: Map spec situations to poses by name: greeting, welcome, secure/protective, guard/watching, celebrating, confetti/party, thinking/confused, idea, trustworthy, proud, grateful, approve/OK, digital wallet, holding DGB, surprised, determined, relaxed, plus send/receive poses from the second sheet for send success and receive empty/success states.
- **Rationale**: Covers every story (welcome, backup, warnings, empty/error/success) without needing all poses.

## 3. Preference and dismissed-tip storage

- **Decision**: `localStorage` keys `dgb-baku-mode` (`full` | `guardian` | `off`) and `dgb-baku-dismissed` (comma-separated tip IDs), loaded and saved the same way `AppState` handles `dgb-*` keys.
- **Rationale**: Matches existing convention, device-local (FR-009/010/014), no encryption needed because nothing sensitive is stored.
- **Alternatives**: IndexedDB secure store (needless for non-secrets). Server sync (violates privacy posture).

## 4. Rendering and arbitration

- **Decision**: A single `BakuService` exposes `Request(message)` and a current-message event. A priority ladder (critical > warning > tip > info) decides which message is shown, so only one appears per screen (FR-016). `BakuMessage.razor` renders it inline. Warnings that require confirmation use `BakuWarningDialog.razor`, which resolves to proceed or cancel.
- **Rationale**: Keeps pages thin and makes the one-at-a-time rule testable in one place.
- **Alternatives**: Each page managing its own Baku state (inconsistent, duplicates the arbitration rule).

## 5. Presence modes and text fallback

- **Decision**: Modes: `full` (everything), `guardian` (warnings only), `off` (Info and Tip hidden; Warning and Critical shown as text-only banner/dialog without the image). Image `onerror` also falls back to text-only.
- **Rationale**: Directly implements FR-010 and FR-011, so security warnings can never be lost by mascot settings or load failures.

## 6. Large-send and new-address rule

- **Decision**: `SendRiskEvaluator.Evaluate(amount, balance, destination, knownAddresses)` returns flags `LargeAmount` (amount >= 50% of balance, balance > 0) and `NewAddress` (destination is neither a saved contact nor in previously-sent addresses). Same evaluator for DGB and DigiDollar sends (clarified). Known addresses are supplied by the caller from `ContactService` plus the wallet's outgoing history.
- **Rationale**: Pure logic means fast tests with negative paths (Principle III).
- **Open item**: Confirm during implementation where "previously sent addresses" can be derived from existing transaction history for both DGB and DigiDollar. No new persistence is planned. If a dedicated store is needed, it must hold addresses only.

## 7. Accessibility and motion

- **Decision**: Baku images get empty alt text when decorative (the message text carries the meaning) and the message region uses a polite live region (assertive for critical). Motion is limited to a subtle entrance transition and a short celebration effect (CSS only, no idle loop), both disabled under `prefers-reduced-motion` (FR-013). Layout uses reserved space and a small size on narrow screens, never overlaying inputs or buttons (FR-012/013).

## 8. Offline and CSP

- **Decision**: Pose images fall under existing static assets, so the PWA service worker precaches them. `img-src 'self'` already permits them, so no CSP change. Total pose payload is budgeted at about 300 KB.
- **Rationale**: Honors the constitution CSP rule and offline behavior.

## 9. Localization

- **Decision**: English-only catalog in `BakuCatalog`, separate from layout so keys can later be moved into the existing locale files.
