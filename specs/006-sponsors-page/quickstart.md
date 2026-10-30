# Quickstart: Validate the Sponsors Page

Validation guide only; implementation steps belong in `tasks.md`. Contracts: [hosting-and-ui.md](contracts/hosting-and-ui.md), [submission-api.md](contracts/submission-api.md), schema: [sponsors-list.schema.json](contracts/sponsors-list.schema.json).

## Prerequisites

- .NET 10 SDK, the repository checked out.
- A **test repository or throwaway branch setup** is strongly recommended for submission testing so real pull requests are not opened against the live repository. Set `Sponsors:Repository` to the test repo for these runs.
- One-time operator setup in GitHub (not app code):
  1. Create the orphan `sponsors` branch with `data/sponsors.json` (`{"version":1,"sponsors":[]}`), `data/entries/`, `data/images/`, both workflows, CODEOWNERS, a PR template and a README.
  2. Create the bot account, give it write access to the repository only, and create a fine-grained token (Contents and Pull requests read/write, this repository only).
  3. Rulesets on `main` and `sponsors`: pull request required, merges restricted to maintainers; allow the GitHub Actions identity to bypass on `sponsors`; delete head branches after merge; create the `sponsor-submission` label.
  4. Set `Sponsors__GitHubToken` as an environment variable on the API host (never in a file).

## 1. Automated checks

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Sponsor"
dotnet test tests/DigiByte.Api.Tests --filter "FullyQualifiedName~Sponsor"
```

Expected: shared-rule, parser and service tests pass (valid entries, duplicates, over-length names, non-https links, bad image names, wrong `version`, malformed JSON, 1,000+ entries; fresh/empty/stale/unavailable). Endpoint tests with fake GitHub handlers pass: created, duplicate, invalid fields, oversized body, rate limit on the 4th request, queue full, honeypot (no GitHub call), GitHub failure with branch cleanup, token never present in any response or log output.

## 2. Run locally

```bash
dotnet run --project src/DigiByte.Api
dotnet run --project src/DigiByte.Web
```

Point `Sponsors:SubmitUrl` at the local API and open `/sponsors`.

## 3. Scenarios (map to the spec)

| # | Do | Expect |
|---|----|--------|
| 1 | Open `/sponsors` with 3 entries on the branch | Three cards, newest first; links open in a new tab (US1) |
| 2 | Entry with and without an image | Image shown / letter placeholder (US1) |
| 3 | Submit a valid add form with no GitHub account | "Submitted, awaiting review"; a pull request from `submission/<id>` into `sponsors` containing only that entry file (and image); `main` unchanged (US5) |
| 4 | Merge it, wait up to 10 minutes, reload `/sponsors` | `build-sponsors-index` regenerates `sponsors.json`; the sponsor appears (FR-004, FR-020) |
| 5 | Submit the identical form twice | One pull request; second shows "already submitted" (FR-029) |
| 6 | Submit with bad name, `http://` link, a transaction ID that is not 64 hex characters | Fields highlighted; no request reaches GitHub (FR-025) |
| 6e | Try to send an add request without the consent confirmation | The form refuses and explains; nothing is sent (FR-034) |
| 6f | Open the page and choose Add me to the list | Step 1 shows the donation QR, the address with Copy, and "I've donated, continue"; continuing shows the form; Back returns to the QR; Remove me shows no QR (FR-036) |
| 6g | Open the page with 5+ sponsors | A "Newest supporters" spotlight shows the newest three above the full grid (FR-037) |
| 6h | Open Home with sponsors listed; then reload within 30 minutes | A "Supported by" strip with up to six avatars appears after Home loads; the reload makes no request for the list (FR-038, SC-012) |
| 6i | Open Home offline with no saved copy, or with an empty list | No strip, no error, nothing else changes (FR-038) |
| 6j | Open the page (any state: list, empty, offline) | Baku shows with "Thank you, supporters!"; set Baku to Off in Settings and the mascot image and message disappear; "Where your donations go" is visible in every state (FR-040, FR-041) |
| 6a | Submit a real donation transaction (3+ confirmations, to a project address, at least 1 DGB) | Accepted; pull request carries label `donation-verified`; `data/donations/<txid>.txt` is in the pull request (FR-031) |
| 6b | Submit a transaction that is unconfirmed / paid elsewhere / below 1 DGB / unknown | `not_verified` with the right reason; nothing opened (FR-031) |
| 6c | Reuse the same transaction ID for a second name | Refused as already used; after the first pull request is closed unmerged, it can be used again (FR-032) |
| 6d | Block both explorers and submit | "Could not be sent, try again later"; the check is not skipped (FR-033) |
| 7 | Choose a large photo | Wallet shrinks it to ≤ 256×256 (WebP, or JPEG on browsers that cannot encode WebP; check an iPhone) before sending (R9) |
| 8 | Send 4 submissions within an hour from one network | The 4th is refused with a clear message (FR-029) |
| 9 | Fill the hidden honeypot via dev tools | Looks successful; no pull request appears |
| 10 | Stop the API, submit | "Could not be sent, try again later"; every other wallet page works (FR-030, FR-011) |
| 11 | Remove the token setting, submit | `unavailable` message; no secret or GitHub detail in the browser response |
| 12 | Submit a removal with the transaction ID that created an entry | A pull request deleting that entry's files; merge; the list drops it; a transaction ID that never created an entry opens nothing (US4, FR-032) |
| 13 | Empty list | "Be the first to sponsor" with the join section (FR-008) |
| 14 | Offline after a successful load | List shown with "may be out of date" and time (US3) |
| 15 | Clear site data, stay offline | Error with Retry; Balance, Receive, Send, History work normally (FR-009, FR-011) |
| 16 | Entry with `<script>` text name, `http://` link, SVG image name | Plain text name; no link; placeholder (FR-013, FR-021) |
| 17 | 100+ entries at 360 px width, light and dark | 48 then "Show more"; no horizontal scroll (SC-006) |
| 19 | Open the join section | A "Contact the maintainer" link for donors the form cannot verify is visible and opens in a new tab (FR-035) |
| 20 | Reload `/sponsors` with an empty cache under "Fast 4G" throttling | The list is visible in under 3 seconds (SC-001) |
| 21 | Time the form from opening it to the confirmation | Under 2 minutes with a confirmed donation (SC-002) |
| 22 | Trigger each failure state and read the message | Plain language, 20 words or fewer, says what to do next (FR-030) |
| 18 | Browser console on `/sponsors` | No CSP violations; requests only to the app origin, the raw prefix, and the API host (FR-012, SC-007) |

## 4. Security-policy check

Confirm in the console that loading a file from a different path on `raw.githubusercontent.com` (for example another repository) is blocked, proving the policy is path-scoped.

## 5. Branch and workflow checks

- Open a test pull request to `sponsors` with an oversized image, a duplicate entry, an `http` link, and an SVG; `validate-sponsors` must fail each. A valid entry passes.
- Push a merged entry; confirm `build-sponsors-index` commits a `sponsors.json` whose order matches merge order, and a second run with no changes commits nothing.
- Confirm the bot's token cannot merge a pull request or push to `main` or `sponsors` directly (rulesets).
