# Implementation Plan: Sponsors Page

**Branch**: `feature/sponsors-page` (spec directory `006-sponsors-page`) | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-sponsors-page/spec.md`

## Summary

Add a `/sponsors` page to the wallet that lists supporters read live from GitHub, plus a submission form so donors with no GitHub account can get listed.

- **Reading**: the page fetches `data/sponsors.json` and sponsor images from the `sponsors` branch of this repository via GitHub's raw-file host, validates every entry in the browser, shows newest first, and falls back to a last-good copy (labelled out of date) or a retry state.
- **Submitting**: the form posts to one new endpoint on the existing `DigiByte.Api` service. The server re-validates, checks the donation automatically on the public explorers (transaction exists, enough confirmations, paid to a project donation address, minimum amount, not already used), then uses a dedicated bot token (kept only in server configuration) to open a pull request on the `sponsors` branch containing one entry file (and optional image). Nothing is written to `main`, and the bot cannot merge.
- **Merging**: a maintainer reviews the entry's content and merges (the donation was already verified automatically). A workflow on the `sponsors` branch regenerates `sponsors.json` from the entry files, so concurrent submissions never conflict. The page shows the change within 5 minutes.
- **Security**: one path-scoped addition to the wallet's content security policy (raw-file prefix). Shared validation rules live in `DigiByte.P2P.Shared` so the wallet and the server enforce exactly the same limits.

## Technical Context

**Language/Version**: C# on .NET 10 (Blazor WebAssembly client; ASP.NET Core minimal API server); Tailwind CSS; small JS interop helpers (local storage, image resize); GitHub Actions workflow (shell + `jq`/ImageMagick) on the `sponsors` branch

**Primary Dependencies**: None new for the wallet. Server uses the built-in ASP.NET Core rate limiter and `HttpClient` against the GitHub REST API (no GitHub SDK package)

**Storage**: GitHub is the only store: entry files `data/entries/<id>.json`, images `data/images/<id>.<webp|png|jpg>`, generated `data/sponsors.json`, all on the `sponsors` branch. Browser local storage keeps only the last good list copy. The server keeps no database; rate-limit counters and an open-PR count cache live in memory

**Testing**: xUnit. `tests/DigiByte.Wallet.Tests` for shared rules, list parser and wallet services; `tests/DigiByte.Api.Tests` for the submission endpoint and GitHub client using fake HTTP handlers; fixture-driven checks for the branch workflows; manual run per [quickstart.md](quickstart.md)

**Target Platform**: Static Blazor WASM PWA (phone-first, light and dark) plus the existing `DigiByte.Api` container on Railway

**Project Type**: Web application with an existing API service; no new project or service

**Performance Goals**: List visible in under 3 seconds on a typical mobile connection; one list request per page open; a merged entry appears in about 5 minutes typically and within 10 at most (raw-host cache of about 5 minutes plus the index workflow run), matching the spec; a submission answers in under 8 seconds including the explorer lookup and the GitHub round trips

**Constraints**: Strict content security policy (one path-scoped addition); core wallet flows never depend on this feature; bot credential only in server configuration; no third-party scripts in the wallet; only https links; PNG/JPEG/WebP images ≤ 200 KB; request body capped at 300 KB

**Scale/Scope**: Up to ~1,000 entries parsed (48 shown at a time); 3 submissions per visitor per hour; at most 50 open submission pull requests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / rule | Status | Notes |
|------------------|--------|-------|
| I. Self-custody | Pass | The page and form never read or send wallet data, addresses, or keys (FR-015); the form works without a wallet |
| II. In-browser crypto; servers only static hosts, price/data proxies, public-chain relays, or public-contribution intake relays | **Pass under constitution 1.1.0** | The submission endpoint is a public-contribution intake relay: it accepts public sponsor text and opens a pull request. It is outside any signing flow, handles no secrets from users, holds a repo-scoped credential only in server configuration that cannot merge, validates and rate-limits and size-limits every submission, publishes nothing without maintainer review, and no core wallet flow depends on it |
| III. Test-first for crypto & wallet logic | Pass | No change to `DigiByte.Crypto` or `DigiByte.Wallet`; tests are still written first for new rules and the endpoint |
| IV. Boundary discipline | Pass | Shared rules go in `DigiByte.P2P.Shared`, which `Api` and `Web` already both reference; no new reference direction |
| V. Resilience | Pass | Page-only fetch; last-good copy; labelled stale/error states; submission failure is a clear message and never affects wallet flows |
| CSP: self-hosted images, "MUST NOT be relaxed to ship a feature" | **Pass under constitution 1.1.0** | The only change is the permitted path-scoped exception: `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/` in `img-src` and `connect-src`. The submission endpoint needs no policy change: its host is already in `connect-src`. Flag it as security-relevant in the PR |
| Secret hygiene | Pass with care | Bot token only in server environment configuration; never logged, returned, or committed. Never in the wallet |
| Auth boundary (state-changing wallet-to-backend calls MUST use a header-based key) | **Pass under constitution 1.1.0** | The permitted exception for anonymous intake endpoints applies: a key shipped inside a static PWA is not secret, so it would add no protection. The endpoint only creates a review item, its credential cannot merge or publish, and it is rate-limited, size-limited, deduplicated, validated with the client's shared rules, and checked against a real donation |
| Documentation | To do | `docs/SPONSORS.md`, `docs/ARCHITECTURE.md` note, README/CHANGELOG |
| Conventional commits, build/test gates | To do | Standard; flag CSP and server-role changes in the PR |

**Gate result**: Pass. The three rules that needed exceptions (server roles, CSP, auth boundary) are covered by constitution version 1.1.0 (commit `7d37bb6`), now on local `main` and on branch `docs/constitution-intake-amendment`. The amendment is not yet published, so it must reach `origin/main` before the feature pull request merges (see the last Polish task). The conditions the amendment attaches are all designed in: the bot credential cannot merge or publish, enforced by the rulesets on `main` and `sponsors`; the policy exception is path-scoped to this repository's `sponsors` branch; and the endpoint has the abuse controls listed above.

## Project Structure

### Documentation (this feature)

```text
specs/006-sponsors-page/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── sponsors-list.schema.json
│   ├── submission-api.md
│   └── hosting-and-ui.md
├── checklists/requirements.md
└── tasks.md             # regenerate with /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.P2P.Shared/Sponsors/
├── SponsorRules.cs                      # shared limits + validators (name, link, image name/type/size, txid)
└── SponsorSubmissionDtos.cs             # request/response shapes shared by wallet and server

src/DigiByte.Api/
├── Endpoints/SponsorEndpoints.cs        # POST /api/sponsors/submissions
├── Services/SponsorSubmissionService.cs # validate, build entry/image, drive GitHub client, idempotency
├── Services/ExplorerTxLookup.cs         # Blockbook + Esplora lookups (definite not-found vs outage)
├── Services/DonationVerifier.cs         # confirmations, recipient, amount, fallback order
├── Services/GitHubSponsorClient.cs      # REST calls: ref, branch, files, PR, open-PR count, claim lookup
├── Program.cs                           # register services, rate limiter, request size limit
└── appsettings.json                     # Sponsors section (repo, branch, limits) - no secrets

src/DigiByte.Web/
├── Pages/Sponsors.razor                 # list, states, join section, entry points
├── Components/Shared/SponsorSubmitForm.razor   # form + states
├── Services/SponsorModels.cs, SponsorListParser.cs, SponsorsService.cs, SponsorSubmissionClient.cs
├── Pages/About.razor, DonationHistory.razor, Settings.razor   # links
├── Layout/MainLayout.razor              # HiddenNavPaths
├── Program.cs                           # registrations
├── wwwroot/index.html                   # CSP: raw prefix in img-src and connect-src
├── wwwroot/appsettings.json             # Sponsors:BaseUrl, Sponsors:SubmitUrl
├── wwwroot/js/sponsors.js               # local storage + canvas resize (WebP, or JPEG where WebP cannot be encoded)
└── wwwroot/locales/{en,es}.json

tests/DigiByte.Wallet.Tests/             # SponsorRulesTests, SponsorListParserTests, SponsorsServiceTests, SponsorSubmissionClientTests
tests/DigiByte.Api.Tests/                # SponsorSubmissionServiceTests, SponsorEndpointsTests, GitHubSponsorClientTests, DonationVerifierTests

docs/SPONSORS.md

# On the dedicated `sponsors` branch (orphan; data, docs, workflows)
README.md
data/sponsors.json                       # generated; never hand-edited
data/entries/<id>.json                   # one per sponsor
data/donations/<txid>.txt                # donation claim (content: entry id)
data/images/<id>.<webp|png|jpg>
.github/workflows/validate-sponsors.yml  # PR checks
.github/workflows/build-sponsors-index.yml  # regenerates sponsors.json on push
.github/CODEOWNERS
.github/pull_request_template.md
```

**Structure Decision**: Reuse the existing API service for the single endpoint and put the shared rules in `DigiByte.P2P.Shared` (already referenced by both `Api` and `Web`) so client and server cannot drift. Keep sponsor data on an orphan `sponsors` branch as one file per sponsor; a workflow generates the single `sponsors.json` the wallet reads, which removes merge conflicts between concurrent pull requests.

## Complexity Tracking

| Item needing justification | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Bot with write access to the repository | Pull requests from branches in the same repo, no fork syncing | Fork-based bot needs fork upkeep and broader token scope. Mitigated by rulesets: `main` and `sponsors` both require pull requests and restrict merges, the token is repo-scoped (Contents and Pull requests only), and the endpoint writes only to `submission/*` branches |
