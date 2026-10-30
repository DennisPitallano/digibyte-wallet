---

description: "Task list for the Sponsors Page feature"
---

# Tasks: Sponsors Page

**Input**: Design documents from `/specs/006-sponsors-page/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The plan commits to xUnit tests (written first, confirmed failing) in `tests/DigiByte.Wallet.Tests` and `tests/DigiByte.Api.Tests`, plus fixture checks for the branch workflows.

**Organization**: Grouped by user story so each can be built and tested on its own. Spec stories: US1 see sponsors (P1), US2 become a sponsor (P1), US3 works when GitHub is unreachable (P2), US5 submit from the wallet (P2), US4 sponsors' control and branch governance (P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unfinished dependency)
- **[Story]**: US1–US5 from the spec
- App code is C# / Blazor in `src/DigiByte.Web/`, API code in `src/DigiByte.Api/`, shared rules in `src/DigiByte.P2P.Shared/`
- **Safety rules**: never put the bot token in any file, log, response or the wallet. Do not push, open pull requests, or change GitHub settings without the user's explicit go-ahead; steps marked **(manual, GitHub)** are for the repository owner. Test submissions against a throwaway repository, not the live one

---

## Phase 1: Setup

**Purpose**: Configuration and shared strings

- [x] T001 Add `Sponsors:BaseUrl` (default `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/data/`) and `Sponsors:SubmitUrl` (default `https://digibyte-api-production.up.railway.app/api/sponsors/submissions`) to `src/DigiByte.Web/wwwroot/appsettings.json` and `src/DigiByte.Web/wwwroot/appsettings.Development.json` (development submit URL points to the local API)
- [x] T002 [P] Add a `Sponsors` section without any secret to `src/DigiByte.Api/appsettings.json`: `Repository` = `DennisPitallano/digibyte-wallet`, `Branch` = `sponsors`, `MaxOpenSubmissions` = 50, `PerVisitorPerHour` = 3, `DonationAddresses` = `dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa` and `DPSNEw5UmtUMQwTZPNoiUFDmCLu3Tp5SrK`, `MinConfirmations` = 3, `MinDonationSatoshis` = 100000000 (1 DGB); document that `Sponsors__GitHubToken` is environment-only
- [x] T003 [P] **Not needed, skipped by design**: the app's pages (About, Roadmap, Settings) use inline English and have no locale keys, and the spec says English at launch, so no keys were added. Original text: add the `sponsors.*` string keys (title, intro, empty state, stale note, error, retry, show more, become a sponsor, donate DGB, add me to the list, contact the maintainer, form labels, public-text notice, result messages) to `src/DigiByte.Web/wwwroot/locales/en.json` and `src/DigiByte.Web/wwwroot/locales/es.json`

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: Shared rules, data types, list parser, and the security-policy change

**CRITICAL**: No story work starts until this phase is done

- [x] T004 **Done locally**: committed as `7d37bb6` (constitution version 1.1.0) on `docs/constitution-intake-amendment` and fast-forwarded onto local `main`, not pushed. The gated tasks (T011, T034–T038) are unblocked for local work. Before the feature pull request is merged, the amendment must reach `origin/main` (push `main` or merge the branch by pull request; owner step). The amendment text: one MINOR constitution amendment in `.specify/memory/constitution.md` covering all three deviations recorded in plan.md: a permitted server role for public-contribution intake (Principle II), path-scoped same-repository GitHub raw URLs in the content security policy, and an anonymous intake endpoint where a header key would add no protection (Auth boundary); include the Sync Impact Report and the version bump per the governance rules. **The CSP edit and every endpoint task wait for this to merge**; the user may instead explicitly accept the deviations in writing

- [x] T005 [P] Write `tests/DigiByte.Wallet.Tests/SponsorRulesTests.cs` first and confirm they fail: name empty, whitespace, 61 characters, control characters; link `http://`, `user@host`, over 200 characters, valid https; txid not 64 hexadecimal characters (too short, too long, non-hex) and a valid 64-hex ID accepted; image list-name pattern with slash, `..`, uppercase, `.svg`, `.gif`; image size over 200 KB; image signature check for PNG, JPEG and WebP (valid and invalid bytes, mismatched type); `id` derivation is stable for identical content and different when name, link or image differ; the branch suffix is the first 16 hex characters of the transaction ID
- [x] T006 Create `src/DigiByte.P2P.Shared/Sponsors/SponsorRules.cs` with the limits and validators, quoting data-model.md: `name` "Trimmed, 1–60 characters, no control characters"; `link` "Absolute `https` URL, ≤ 200 characters, no embedded credentials"; `image` list-name pattern `^[a-z0-9][a-z0-9._-]{0,63}\.(png|jpe?g|webp)$`; image "≤ 200 KB"; `txid` "64 hexadecimal characters" (a DGB transaction ID); image signature checks for PNG (`89 50 4E 47`), JPEG (`FF D8 FF`) and WebP (`RIFF....WEBP`) that return the detected type; entry `id` = "first 12 hex characters of SHA-256 over lower-cased name, link, and image hash"; make T005 pass
- [x] T007 [P] Create `src/DigiByte.P2P.Shared/Sponsors/SponsorSubmissionDtos.cs`: request fields (`type` add|remove, `name`, `link?`, `image?`, `txid`, `consent` boolean required for add, `website` honeypot) and result states `created`, `duplicate`, `invalid` (with field list), `not_verified` (with reason `not_found`, `too_few_confirmations`, `wrong_recipient`, `below_minimum`), `already_used`, `rate_limited`, `queue_full`, `unavailable` per data-model.md; `name` is not required when `type` is `remove`
- [x] T008 [P] Create `src/DigiByte.Web/Services/SponsorModels.cs`: `SponsorEntry` (`Name`, `ImageUrl?`, `Link?`), `SponsorList` (`Entries` newest-first, `FetchedAt`, `SkippedCount`), `SponsorLoadResult` with states `Fresh`, `FreshEmpty`, `Stale`, `Unavailable`
- [x] T009 [P] Write `tests/DigiByte.Wallet.Tests/SponsorListParserTests.cs` first and confirm they fail: valid list; name-only entry; invalid entries skipped individually; duplicates (same name case-insensitive + link + image) shown once, same name with different link kept; wrong or missing `version`; malformed JSON; optional `id` accepted and ignored; unknown fields (amount, tier, date) ignored and never exposed; 1,000+ entries capped; newest-first order; removing an entry from the list no longer yields it; markup in names left as plain text
- [x] T010 Create `src/DigiByte.Web/Services/SponsorListParser.cs`: pure, never-throwing; `version` required and must be `1` (otherwise unreadable); each entry validated with `SponsorRules`; invalid entries skipped; duplicates dropped, first wins; cap at 1,000 entries; reverse so the last entry in the file is first; image names resolved against `Sponsors:BaseUrl`; make T009 pass (depends on T006, T008)
- [x] T011 Edit `src/DigiByte.Web/wwwroot/index.html` (T004, the constitution amendment, is applied locally; it must be published before the feature pull request merges): add `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/` to both `img-src` and `connect-src`; change nothing else in the policy (the API host is already in `connect-src`)
- [x] T012 Register only the named `HttpClient` "SponsorsRead" (reading `Sponsors:BaseUrl`) in `src/DigiByte.Web/Program.cs`; the service and submission-client registrations are added by the tasks that create those classes; nothing runs at app start (FR-011)

**Checkpoint**: Rules and parser tests pass; app builds with the new policy

---

## Phase 3: User Story 1 - See who supports the project (Priority: P1) — MVP

**Goal**: `/sponsors` shows the live list with picture, name, optional link, newest first, with placeholders and paging

**Independent Test**: Point `Sponsors:BaseUrl` at a folder or branch holding 3 entries; open `/sponsors`; see 3 cards, newest first, link works, entry without image shows a letter placeholder

- [x] T013 [P] [US1] Write `tests/DigiByte.Wallet.Tests/SponsorsServiceTests.cs` (fake `HttpMessageHandler`) first: success returns `Fresh` with newest-first entries; empty list returns `FreshEmpty`; request goes only to the configured base URL; exactly one request per load
- [x] T014 [US1] Implement `LoadAsync()` in `src/DigiByte.Web/Services/SponsorsService.cs` (and register it as scoped in `src/DigiByte.Web/Program.cs`): one request for `sponsors.json`, parse with `SponsorListParser`, return `Fresh` or `FreshEmpty`; no query-string cache busting; timeout so a hung request ends in a state (depends on T010, T012)
- [x] T015 [US1] Create `src/DigiByte.Web/Pages/Sponsors.razor` at `@page "/sponsors"`: intro text (FR-007), responsive card grid (fixed square picture box, name as plain text, link with `target="_blank" rel="noopener noreferrer"`), `loading="lazy"` images, first 48 entries then "Show more" adding 48, loading state; inject no wallet service (FR-015)
- [x] T016 [US1] Create `src/DigiByte.Web/Components/Shared/SponsorAvatar.razor`: image with fixed size, letter placeholder when there is no image or on load error, no layout shift (FR-010), legible in light and dark
- [x] T017 [P] [US1] Add `"/sponsors"` to `HiddenNavPaths` in `src/DigiByte.Web/Layout/MainLayout.razor`
- [x] T018 [P] [US1] Add a Sponsors row next to Roadmap in `src/DigiByte.Web/Pages/Settings.razor`

**Checkpoint**: Page works end to end with live data; MVP complete

---

## Phase 4: User Story 2 - Support the project (Priority: P1)

**Goal**: Clear ways to give and a visible entry to the submission form

**Independent Test**: "Donate DGB" lands on `/donations`; with an empty list the page shows "be the first to sponsor"

- [x] T019 [US2] In `src/DigiByte.Web/Pages/Sponsors.razor` add the join section: a primary "Donate DGB" link to `/donations` (there is no GitHub Sponsors profile, so there is no "Become a sponsor" button), an "Add me to the list" action that reveals the form area, and a visible "Contact the maintainer" link (to `https://dennispitallano.github.io`, the maintainer link already on the About page, and to the repository's GitHub issues page) for donors the form cannot verify (FR-035) (form built in US5)
- [x] T020 [US2] Add the friendly empty state for `FreshEmpty` with the same call to action in `src/DigiByte.Web/Pages/Sponsors.razor` (FR-008)
- [x] T021 [P] [US2] Add a Sponsors link to `src/DigiByte.Web/Pages/About.razor` and `src/DigiByte.Web/Pages/DonationHistory.razor`
- [x] T022 [P] [US2] Write `docs/SPONSORS.md`: how to get listed through the wallet form (donate DGB, wait for 3 confirmations, paste the transaction ID; the donation is checked automatically and a maintainer reviews the entry), that donors who gave through GitHub Sponsors or another channel use the "Contact the maintainer" link on the Sponsors page, what is public (name, link, picture and transaction ID appear in a pull request), limits ("PNG, JPEG, or WebP; ≤ 200 KB; ≤ 512×512 pixels"), how to request removal (submit the original transaction ID), and the privacy note that loading the page contacts GitHub's raw host and submitting contacts the project's API (IP visible, no wallet data sent)

**Checkpoint**: Visitors can find both ways to give

---

## Phase 5: User Story 3 - Works when GitHub is unreachable (Priority: P2)

**Goal**: Labelled out-of-date list or a retry state; core wallet flows unaffected

**Independent Test**: Load once online; go offline and reload → list with "may be out of date"; clear site data and stay offline → error with Retry; Balance, Receive, Send and History still work

- [x] T023 [P] [US3] Extend `tests/DigiByte.Wallet.Tests/SponsorsServiceTests.cs` first: failed fetch with a stored copy returns `Stale` with the stored fetch time; failed fetch with no copy returns `Unavailable`; HTTP 429/5xx/timeout count as failure; a copy is written only after a fully valid parse; a corrupt stored copy is ignored; an unreadable (not just empty) response keeps the previous good copy
- [x] T024 [P] [US3] **Not needed**: the service uses `localStorage.getItem` and `localStorage.setItem` through `IJSRuntime` directly (as `BakuService` does), inside try/catch, so no separate JS was required. Original text: create the local-storage part of `src/DigiByte.Web/wwwroot/js/sponsors.js` (try/catch around every read and write; works when storage is blocked), following the interop pattern used by `ThemeService`, and load it from `src/DigiByte.Web/wwwroot/index.html` as a self-hosted script (no inline script, policy unchanged)
- [x] T025 [US3] In `src/DigiByte.Web/Services/SponsorsService.cs` save the last-good copy (raw text + fetch time) after a valid parse and fall back to it on failure, returning `Stale` or `Unavailable` (depends on T014, T024)
- [x] T026 [US3] In `src/DigiByte.Web/Pages/Sponsors.razor` render `Stale` (list plus "may be out of date" with the fetch time) and `Unavailable` (message plus Retry that re-runs the load); never a broken-image icon or blank grid (FR-009, FR-010)
- [x] T027 [US3] Leave `cacheableApiPatterns` in `src/DigiByte.Web/wwwroot/service-worker.published.js` unchanged and add a comment explaining sponsors rely on the browser cache plus the app's last-good copy

**Checkpoint**: Failure states verified; wallet flows unaffected

---

## Phase 6: User Story 5 - Submit my own entry from the wallet (Priority: P2)

**Goal**: A donor with no GitHub account submits from a form; a bot opens a pull request on the `sponsors` branch; nothing reaches `main` or the live list without a maintainer merge

**Independent Test**: Submit a valid entry from the form; one pull request appears from `submission/<id>` into `sponsors` containing only that entry file (and image); the page shows nothing new until a maintainer merges it, then within 10 minutes it appears

### Tests for User Story 5 (write first, confirm failing)

- [x] T028 [P] [US5] Write `tests/DigiByte.Api.Tests/GitHubSponsorClientTests.cs` with a fake handler: sequence read head SHA → create `submission/<first 16 hex of txid>` → commit entry file, claim file `data/donations/<txid>.txt` (content: the entry id) and optional image → open pull request with labels `sponsor-submission` and `donation-verified`; "branch exists" (422) maps to already used; a claim-file lookup on `sponsors` (404 = unused, 200 = used); reading a claim file returns the entry id for removal; open-PR count only counts head branches starting with `submission/`; failure after branch creation triggers best-effort branch delete; the token appears only in the Authorization header and never in any exception message
- [x] T029 [P] [US5] Write `tests/DigiByte.Api.Tests/SponsorSubmissionServiceTests.cs`: an add with `consent` false or missing returns invalid with field `consent` and opens nothing; the pull request body records the consent statement; valid add produces entry JSON with only `id`, `name`, optional `image` (`<id>.<detected extension>`) and `link`; the transaction ID goes only in the pull request body and the claim file; invalid fields return the field names without echoing content; honeypot value returns success with zero GitHub calls; identical content gives the same `id`; remove requires an existing claim file for the transaction ID, deletes the entry and image it names, and opens nothing when no claim exists; an add with a used transaction returns already used; verification failures return the matching reason and open nothing; cap of 50 open submission pull requests returns `queue_full`
- [x] T030 [US5] Prepare the API test host: add the package `Microsoft.AspNetCore.Mvc.Testing` (version matching the project's .NET 10 packages) to `tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj`, and expose the entry point by adding `public partial class Program { }` at the end of `src/DigiByte.Api/Program.cs` (no other change to that file in this task); confirm a trivial in-process request to `/api/health` passes before the endpoint tests are written
- [x] T031 [US5] Write `tests/DigiByte.Api.Tests/SponsorEndpointsTests.cs`: 201 created, 200 duplicate, 400 invalid with field names, 413 over 300 KB, 409 already used, 422 not verified for each reason, 429 with `Retry-After` on the 4th request in an hour from one IP, 503 queue full, 502 when the token is missing, GitHub fails, or no explorer can be reached; no response body or captured log line contains the token, the submitted name, link or reference
- [x] T032 [P] [US5] Write `tests/DigiByte.Wallet.Tests/SponsorSubmissionClientTests.cs` with a fake handler: each server status maps to the right result state; a network error maps to `unavailable`; the request goes only to `Sponsors:SubmitUrl`; nothing wallet-related is attached
- [x] T033 [P] [US5] Write `tests/DigiByte.Api.Tests/DonationVerifierTests.cs` first (fake explorer clients): confirmed transaction paying a project address ≥ 1 DGB passes; unknown transaction → `not_found`; 2 confirmations → `too_few_confirmations`; paid to another address → `wrong_recipient`; 0.5 DGB → `below_minimum`; payment to the older donation address counts; outputs to the project address are summed; first explorer failing falls back to the second; every explorer failing is an error (never a pass)

### Implementation for User Story 5 — server

- [x] T034 [US5] Create `src/DigiByte.Api/Services/GitHubSponsorClient.cs` using `HttpClient` and the GitHub REST API (no SDK): get head ref of `sponsors`, create branch, put contents (add) and delete contents (remove), create pull request with title `Sponsor submission: <name>` (name sanitised), label, and a body holding the donation reference and a public-text notice, count open `submission/*` pull requests (cached 60 seconds), look up and read claim files, commit the claim file with the entry, delete branch; token read from `Sponsors__GitHubToken` and sent only in the Authorization header (depends on T006, T028)
- [x] T035 [US5] Create `src/DigiByte.Api/Services/DonationVerifier.cs` (with `ExplorerTxLookup.cs`: own small `BlockbookTxLookup` and `EsploraTxLookup`, because the wallet's explorer clients turn every failure into "nothing" and cannot tell "not found" from an outage; the check must never pass or skip when explorers are down); try the explorers in order; check `Confirmations` ≥ `Sponsors:MinConfirmations`, sum of `Outputs` whose `Address` is in `Sponsors:DonationAddresses` ≥ `Sponsors:MinDonationSatoshis`; return the reason on failure; throw on no reachable explorer so the caller maps it to unavailable (depends on T033)
- [x] T036 [US5] Create `src/DigiByte.Api/Services/SponsorSubmissionService.cs`: validate with `SponsorRules`, honeypot, image size and signature checks (PNG, JPEG, WebP), store the file as `<id>.<detected extension>`, derive `id`, check the claim, enforce the open-PR cap, verify the donation with `DonationVerifier` before creating anything, build the entry JSON, drive `GitHubSponsorClient`, handle removal lookup through `data/sponsors.json`, map every failure to a result state; log only `id`, type and outcome (depends on T029, T034)
- [x] T037 [US5] Create `src/DigiByte.Api/Endpoints/SponsorEndpoints.cs` with `POST /api/sponsors/submissions` (multipart), request size limit 300 KB, status mapping from `contracts/submission-api.md`, response bodies containing only state and field names (depends on T031, T036)
- [x] T038 [US5] Edit `src/DigiByte.Api/Program.cs`: register `GitHubSponsorClient`, `SponsorSubmissionService`, `DonationVerifier`, the explorer clients (`BlockbookApiService` for `https://digibyteblockexplorer.com` and `BlockchainApiService` for `https://digiexplorer.info`, in the wallet's fallback order) and the donation options, and a named `HttpClient` for GitHub (user agent, API version header, timeout), add the built-in rate limiter with a per-client-IP policy of `Sponsors:PerVisitorPerHour` placed after the existing `UseForwardedHeaders`, map the endpoint group `/api/sponsors`, keep the existing CORS policy for the wallet origin

### Implementation for User Story 5 — wallet

- [x] T039 [P] [US5] (verify on a real iPhone: still open) Add the image helper to `src/DigiByte.Web/wwwroot/js/sponsors.js`: take the picked file, resize to at most 256×256 with a canvas, re-encode as WebP, and if the browser returns a different type (Safari) re-encode as JPEG, return the bytes, the final type and a preview URL; reject non-image files; verify on an iPhone
- [x] T040 [US5] Create `src/DigiByte.Web/Services/SponsorSubmissionClient.cs`: build the multipart request, post to `Sponsors:SubmitUrl`, map responses and network errors to result states; register the named `HttpClient` "SponsorsSubmit" and this client as scoped in `src/DigiByte.Web/Program.cs` (depends on T007, T012, T032)
- [x] T041 [US5] Create `src/DigiByte.Web/Components/Shared/SponsorSubmitForm.razor`: request type (add or remove), name, optional link, optional picture with preview, donation transaction ID (64 hex characters, with help text on where to find it), hidden honeypot, a required consent confirmation for add requests ("I am this person, or I have their permission to be listed") that blocks sending until ticked, and for the removal type only the transaction ID; inline validation with `SponsorRules` before sending; visible notice that the name, link, picture and donation reference appear publicly in a pull request; result messages for `created`, `duplicate`, `invalid`, `not_verified` (each reason, with "try again shortly" for too few confirmations), `already_used`, `rate_limited`, `queue_full`, `unavailable`; no wallet service injected; double-tap protection (depends on T039, T040)
- [x] T042 [US5] Wire the form into the "Add me to the list" area of `src/DigiByte.Web/Pages/Sponsors.razor`; a failure here must not change the sponsor list or any other page (FR-030)

**Checkpoint**: A submission produces exactly one pull request; failures are clear and isolated

---

## Phase 7: User Story 4 - Sponsors choose how they appear (Priority: P3)

**Goal**: Only agreed, donation-confirmed, valid entries appear; removal is a normal reviewed request; the `sponsors` branch enforces it

**Independent Test**: A test pull request with an oversized image, a duplicate, an `http` link, or an SVG fails `validate-sponsors`; a valid entry passes; after merge `build-sponsors-index` regenerates `sponsors.json`; a merged removal makes the entry disappear from the page

Work in this phase happens on the dedicated `sponsors` branch, not `main`. Prepare it locally in a separate worktree; do not push without the user's go-ahead.

- [x] T043 [US4] Create an orphan local `sponsors` branch (separate worktree) with `data/sponsors.json` (`{"version":1,"sponsors":[]}`), `data/entries/.gitkeep`, `data/images/.gitkeep`, and a `README.md` pointing sponsors to the wallet form and `docs/SPONSORS.md`
- [x] T044 [P] [US4] On the `sponsors` branch add `.github/workflows/validate-sponsors.yml` (runs on pull requests to `sponsors`): entry files validate against `specs/006-sponsors-page/contracts/sponsors-list.schema.json` (copy the schema into the branch); one entry per file and file name equals `id`; every `data/donations/<txid>.txt` holds a 12-character entry id (a claim may outlive its removed entry, so the donation can never be reused) and every entry has exactly one claim file; https-only links; no duplicates by name+link+image; images are PNG/JPEG/WebP, "≤ 200 KB", "≤ 512×512 pixels"; every referenced image exists and every image is referenced; submission branches change nothing outside `data/`
- [x] T045 [P] [US4] On the `sponsors` branch add `.github/workflows/build-sponsors-index.yml` (on push to `sponsors`): rebuild `data/sponsors.json` (the generated list; hand edits are overwritten, and this workflow is the single permitted non-maintainer writer to `sponsors`, per FR-022) from `data/entries/*.json`, ordered by when each entry file was first added (ties by `id`), commit only when the content changed, using the Actions identity
- [x] T046 [P] [US4] On the `sponsors` branch add `.github/CODEOWNERS` (maintainer owns everything) and `.github/pull_request_template.md` (what the maintainer checks: the `donation-verified` label is present, the consent statement is in the pull request body and the name is plausible for the donor, name and link acceptable, picture acceptable)
- [x] T047 [US4] Add fixture checks for both workflows on the `sponsors` branch: a valid sample passes; invalid samples (oversized image, 600×600 image, duplicate, `http` link, SVG, missing image file, unreferenced image, entry without a claim file, claim pointing to a missing entry) each fail; index rebuild order matches add order and a second run without changes commits nothing
- [ ] T048 **(manual, GitHub)** [US4] Create the bot account with write access to this repository only and a fine-grained token (Contents and Pull requests read/write, this repository only); set it as the environment variable `Sponsors__GitHubToken` on the API host; create the `sponsor-submission` label
- [ ] T049 **(manual, GitHub)** [US4] Push the `sponsors` branch and set rulesets on both `main` and `sponsors`: pull request required, merges restricted to maintainers, head branches deleted after merge; allow the GitHub Actions identity to bypass on `sponsors` only; confirm the bot cannot merge or push to `main` or `sponsors` directly
- [ ] T050 [US4] Verify the full removal path against the throwaway repository: a `remove` submission with the transaction ID that created the entry opens a pull request deleting the right entry and image (and a transaction ID that created nothing opens nothing), merging it regenerates the list without that sponsor, and the page stops showing them within 10 minutes

**Checkpoint**: Branch governance verified; the whole flow works end to end

---

## Phase 8: Polish & Cross-Cutting

- [x] T051 [P] Add a short note about the submission endpoint, the bot, and the `sponsors` branch to `docs/ARCHITECTURE.md`
- [x] T052 [P] Add a `CHANGELOG.md` entry and a README mention linking to `docs/SPONSORS.md`
- [x] T053 (done: Api tests 65/65 pass; Wallet tests 1004 pass, 6 live-internet `LiveExplorerBenchmarks` fail with explorer 502s, unrelated to this feature) Run `dotnet build` and `dotnet test` for `tests/DigiByte.Wallet.Tests` and `tests/DigiByte.Api.Tests`; fix any failures
- [ ] T054 Run every scenario in `specs/006-sponsors-page/quickstart.md` in the browser (including 360 px width, light and dark, offline, and the path-scoping check that another raw-host path is blocked) and note results; also record the measured checks: time to first visible list under Chrome "Fast 4G" throttling with an empty cache (SC-001, under 3 seconds); stopwatch time from opening the form to the confirmation (SC-002, under 2 minutes); at 360 px width in light and dark, confirm `document.documentElement.scrollWidth <= window.innerWidth` and that text and pictures stay legible (SC-006, FR-014); and that each error message is plain language, 20 words or fewer, and says what to do next (FR-030)
- [ ] T055 Verify no console policy violations on `/sponsors` and that requests go only to the app origin, the allowed raw prefix, and the API host (SC-007)
- [x] T056 Search the whole diff, API logs from the test run, and every API response for the token value, the submitted names, links and full transaction IDs (outside the pull request body and claim file); none may appear (SC-009, FR-028)
- [ ] T057 When the PR is opened, flag in its description that it changes the content security policy and adds a server-side submission endpoint with a bot credential (constitution merge gate)
- [ ] T058 Before the feature pull request is merged, confirm constitution version 1.1.0 (commit `7d37bb6`, `.specify/memory/constitution.md`) is on `origin/main`: `git fetch` then check `git log origin/main -- .specify/memory/constitution.md` and the version line; if not, publish it first through its own pull request from branch `docs/constitution-intake-amendment` (owner step; do not push without the owner's go-ahead)

- [x] T059 [US5] Add the donate-first step to `src/DigiByte.Web/Components/Shared/SponsorSubmitForm.razor` (step 1 of 2: QR code of a BIP21 link with the minimum amount, the address with a Copy button, "I've donated, continue"; step 2 of 2: the form, with a way back; "Remove me" skips step 1), keep the donation address in one place in `src/DigiByte.Web/Services/DonationInfo.cs` (also used by `src/DigiByte.Web/Pages/DonationHistory.razor`), and add `tests/DigiByte.Wallet.Tests/DonationInfoTests.cs` including a check that the address is one the API accepts (FR-036)

- [x] T060 [US2] Add a "Sponsors" link after "Roadmap" in the footer links of `src/DigiByte.Web/Pages/Welcome.razor` and `src/DigiByte.Web/Pages/Unlock.razor` (FR-001)

- [x] T061 [US1] Add the `maxAge` option to `LoadAsync` in `src/DigiByte.Web/Services/SponsorsService.cs` (a saved copy younger than the limit is returned without any request) with three tests in `tests/DigiByte.Wallet.Tests/SponsorsServiceTests.cs` (FR-038, SC-012)
- [x] T062 [US1] Add a "Newest supporters" spotlight (three newest, larger tiles, "All sponsors" heading above the grid) to `src/DigiByte.Web/Pages/Sponsors.razor`, and `SizeClass`/`LetterClass` parameters to `src/DigiByte.Web/Components/Shared/SponsorAvatar.razor` (FR-037)
- [x] T063 [US1] Create `src/DigiByte.Web/Components/Shared/SponsorsStrip.razor` (newest six, "+N", "See all", loads after first render with a 30-minute saved copy, hidden on any problem) and place `<SponsorsStrip />` in `src/DigiByte.Web/Pages/Home.razor` between the DigiDollar line and the transaction area (FR-038)
- [x] T064 [US2] Make the Sponsors links stand out: rose accent with a heart in the footer links of `src/DigiByte.Web/Pages/Welcome.razor` and `src/DigiByte.Web/Pages/Unlock.razor`; rose border and a thank-you line in `src/DigiByte.Web/Pages/Settings.razor` and `src/DigiByte.Web/Pages/About.razor` (FR-039)

- [x] T065 [US1] Add the Baku message `sponsors.thanks` (Info, pose `celebrating`) to `src/DigiByte.Web/Services/BakuCatalog.cs` and place `<BakuMessage MessageId="sponsors.thanks" />` under the intro in `src/DigiByte.Web/Pages/Sponsors.razor` (FR-040; the existing catalog tests already check the pose image exists)
- [x] T066 [US2] Add the "Where your donations go" section (hosting, our own DigiByte node, maintenance and security, new features; no amounts; link to `/donations`) above the join section in `src/DigiByte.Web/Pages/Sponsors.razor`, visible in every page state (FR-041)
---

## Dependencies & Execution Order

- **Phase 1** → **Phase 2** → stories. Phase 2 blocks everything.
- **US1** (MVP) after Phase 2. **US2** needs the page (T015). **US3** needs the service (T014) and page (T015).
- **US5** server work (T028–T038) depends only on Phase 2 and can run alongside US1–US3; wallet form work (T039–T042) needs the page (T015) and the shared DTOs (T007).
- **US4** branch preparation (T043–T047) is independent of app code and can start after Phase 2; end-to-end US5 verification needs T048 and T049 (owner steps) and the branch pushed.
- The constitution amendment (T004) is applied locally, so the CSP edit (T011) and the endpoint, verifier and API registration tasks (T034–T038) can proceed; it must be published to `origin/main` before the feature pull request merges (task T058).
- `DonationVerifier` (T035) comes before the submission service (T036) uses it.
- Within a story: tests first (confirm failing), then services, then UI.
- Live loading in US1 needs the security-policy edit (T011), so, until the amendment is published, T011 is local work only when run against the real `sponsors` branch; a local same-origin test folder lets US1 be built and tested before T004 merges.
- Polish after the stories being shipped.

## Parallel Opportunities

- Phase 1: T002 and T003 alongside T001.
- Phase 2: T005, T007, T008, T009 together; T006 after T005; T010 after T006 and T008; T011 is unblocked by T004 (applied locally).
- US1: T013 and T017 and T018 once T010 exists.
- US2: T021 and T022 together after T019.
- US3: T023 and T024 together.
- US5: T028, T029, T032, T033 together; T031 after T030; T039 alongside server work.
- US4: T044, T045, T046 together after T043; US4 branch work in parallel with US1–US5 app code.

## Implementation Strategy

1. **MVP**: Phases 1, 2 and 3 — live list on `/sponsors`. Stop and validate.
2. Add **US2** (join paths and docs), then **US3** (failure handling): the read-only page is then shippable.
3. Build **US5** server and wallet form, and prepare **US4** branch contents in parallel; they only meet at the end-to-end test.
4. Owner steps **T048 and T049** gate the submission feature going live; do them against a throwaway repository first.
5. Finish with Polish and the PR security flags; the constitution amendment (T004) is done earlier, before the code that depends on it.
