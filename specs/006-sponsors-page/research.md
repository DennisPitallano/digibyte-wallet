# Research: Sponsors Page

All unknowns from the plan are resolved below. Repository facts were checked in the code; GitHub hosting behaviour is standard documented behaviour and is listed for confirmation in [quickstart.md](quickstart.md).

## R1. Where the list is read from

- **Decision**: The browser fetches `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/data/sponsors.json`; images come from `.../sponsors/data/images/<file>`.
- **Rationale**: The repository is public, so no token is needed. The raw host allows cross-origin reads and sends a short cache lifetime (about 5 minutes), which, together with the index rebuild, fits the spec's 10-minute bound without extra machinery.
- **Alternatives**: GitHub contents API (60 requests/hour per IP, base64-wrapped) is worse on shared mobile IPs; GitHub Pages adds a new origin and publish step; bundling contradicts the live-update decision.

## R2. Content security policy

- **Decision**: Add the exact prefix `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/` to `img-src` and `connect-src` in `wwwroot/index.html`. CSP paths ending in `/` match as a prefix, so only this branch is reachable.
- **Rationale**: Narrowest possible allowance. The submission endpoint needs nothing: `https://digibyte-api-production.up.railway.app` is already in `connect-src`.
- **Caveats**: Paths are case-sensitive; renaming the repo or branch needs a matching policy edit. CSP path matching is ignored after redirects, so the plan uses the final raw URL. `CspHashInject.targets` only rewrites the script hash.
- **Alternatives**: Whole raw host (too broad); proxy through our API (extra traffic and cache layer).

## R3. Freshness and last-good copy

- **Decision**: Rely on the browser HTTP cache honouring the raw host's lifetime. Do not add the raw URL to the service worker's cacheable patterns (checked: `service-worker.published.js` only caches a fixed allow-list). Keep an app-level last-good copy (list text plus fetch time) in local storage, written only after a fully valid parse.
- **Rationale**: One cache layer keeps the 5-minute bound true; the last-good copy powers the "may be out of date" state.
- **Alternatives**: Service worker cache (cannot tell the page it is stale); IndexedDB via `ISecureStorage` (reserved for encrypted wallet data).

## R4. Why entry files and a generated index

- **Decision**: Each submission is its own file `data/entries/<id>.json` (plus `data/images/<id>.<webp|png|jpg>`). A workflow on the `sponsors` branch rebuilds `data/sponsors.json` after every push, ordered by when each entry file was first added, and commits it with the Actions identity.
- **Rationale**: If the bot edited one shared `sponsors.json`, any two open pull requests would conflict as soon as one merged, and the bot cannot rebase. One file per sponsor means pull requests never touch the same file. Rebuilding from scratch each run is deterministic and needs no stored state (fine at ~1,000 entries).
- **Requirement**: The ruleset on `sponsors` must let the GitHub Actions identity bypass the "pull request required" rule so it can push the generated file. This is an operator step.
- **Alternatives**: Bot edits `sponsors.json` per pull request (conflicts); maintainer edits the file by hand (manual and error-prone); no index with the page reading the directory via the contents API (rate limits and N requests).

## R5. Submission endpoint host

- **Decision**: Add `POST /api/sponsors/submissions` to the existing `DigiByte.Api` (Railway), in `Endpoints/SponsorEndpoints.cs`, following the pattern of `DirectoryEndpoints` and `PriceEndpoints`.
- **Rationale**: Checked in `Program.cs`: the API already has a CORS policy for the wallet origin (`WasmClient`), forwarded-header handling for real client IPs behind Railway, in-memory caching, and `HttpClient` factory use; its host is already allowed by the wallet's `connect-src`. No new service or domain.
- **Note**: Existing endpoints have no auth or rate limiting; this one adds both abuse controls (R6) since it triggers external writes.
- **Alternatives**: `DigiByte.NodeApi` (node-specific, wrong concern); `DigiByte.Pay.Api` (merchant API with bearer-key auth, wrong audience); new service (more operations).

## R6. Abuse controls

- **Decision**:
  - Per-visitor rate limit of 3 submissions per hour using the built-in ASP.NET Core rate limiter, partitioned by client IP; it must be added after forwarded-header handling so the real IP is used.
  - Open-PR cap of 50: before creating anything, the server counts open pull requests whose head branch starts with `submission/` (one list call, cached for 60 seconds). Over the cap returns a clear "queue is full, try later" response.
  - Request size limit of 300 KB and a hidden honeypot field; any value in it is silently treated as success without calling GitHub.
  - Idempotency: the branch name is `submission/<first 16 hex of the transaction ID>`, so one donation can only have one open pull request. If the branch already exists, return `already_used`.
  - No third-party CAPTCHA scripts, because the wallet forbids them in its policy.
- **Rationale**: Matches FR-029 with no new infrastructure. In-memory limiter state is per instance; the API runs as one instance today and the open-PR cap and idempotency are global backstops regardless.

## R7. Bot identity and token

- **Decision**: A dedicated bot account with write access to this repository only, using a fine-grained personal access token limited to this repository with Contents: read/write and Pull requests: read/write (Metadata read is implicit). The token is supplied only as the environment variable `Sponsors__GitHubToken` on the API host, never in config files, logs, responses, or the wallet.
- **Rationale**: Matches the clarified decision (dedicated bot, not the maintainer's token, cannot merge). Because the bot has write access it could push to any unprotected branch, so rulesets on `main` and `sponsors` must require pull requests and restrict merges; the server code only ever writes to `submission/*` branches.
- **Alternatives**: GitHub App installation tokens (better rotation, more setup, noted as a later hardening); fork-based bot (fork upkeep, broader PR permissions); maintainer's personal token (rejected by the user).

## R8. Creating the pull request via the GitHub REST API

- **Decision**: Sequence, all with `HttpClient` and the bot token:
  1. Read the head SHA of `sponsors` (`GET /repos/{repo}/git/ref/heads/sponsors`).
  2. Create `submission/<id>` from it (`POST /git/refs`); a 422 "already exists" means duplicate.
  3. Add `data/entries/<id>.json` (and `data/images/<id>.<ext>`) with `PUT /contents/...` on that branch.
  4. Open the pull request into `sponsors` (`POST /pulls`) with a fixed title, a body holding the donation reference and a public-text notice, and the label `sponsor-submission`.
  5. For a removal request: read the claim file `data/donations/<txid>.txt`, and open a pull request that deletes the entry and image files it names; no claim file → refuse.
- **Rationale**: Only standard endpoints; no SDK dependency; every step maps to one unit-testable call.
- **Failure handling**: Any GitHub error after step 2 triggers best-effort deletion of the new branch; the visitor sees a generic "could not be sent" message, with details only in server logs (without the token or submitted content).

## R9. Shared validation rules

- **Decision**: One `SponsorRules` class in `src/DigiByte.P2P.Shared/Sponsors/` defines the limits and validators used by the wallet form, the wallet list parser, and the server: name 1–60 characters without control characters; link absolute `https`, ≤ 200 characters, no credentials; image file name pattern `^[a-z0-9][a-z0-9._-]{0,63}\.(png|jpe?g|webp)$` for the list, server-assigned `<id>.<ext>` (extension from the detected type) for new images; donation reference 6–200 characters, plain text; image ≤ 200 KB.
- **Rationale**: Principle IV (cross-cutting rules live in the lowest layer that owns the concept); `Api` and `Web` already reference this project.
- **Image checks**: The wallet resizes any chosen picture to at most 256×256 in the browser (canvas) and re-encodes it as WebP; browsers that cannot encode WebP (notably Safari on iPhone, to be confirmed on a device) return PNG for that request, so the helper checks the returned type and falls back to JPEG, which every browser encodes. The server independently checks the size and the signature bytes (PNG `89 50 4E 47`, JPEG `FF D8 FF`, WebP `RIFF....WEBP`) and rejects anything else; it names the stored file `<id>.<detected extension>`. Pixel dimensions on the server are not decoded (no image library); the branch workflow enforces ≤ 512×512 with ImageMagick.

## R10. Validation of list data in the browser

- **Decision**: A pure, never-throwing parser: wrong `version` or invalid JSON → unreadable; each entry validated separately with `SponsorRules`; duplicates = same name (case-insensitive) + link + image, first wins; the optional `id` field is accepted and ignored; cap at 1,000; reverse so the last entry in the file shows first.

## R11. Branch governance and workflows

- **Decision**: Orphan `sponsors` branch with rulesets: pull request required, merge restricted to maintainers (plus CODEOWNERS), the Actions identity allowed to bypass for the generated file, head branches deleted after merge. Two workflows:
  - `validate-sponsors` on pull requests to `sponsors`: schema for entry files, https links, one entry per file, image type/size ≤ 200 KB/dimensions ≤ 512×512, every image referenced and every referenced image present, no changes outside `data/` and the PR template for submission branches.
  - `build-sponsors-index` on push to `sponsors`: regenerate `data/sponsors.json` and commit if it changed.
- **Rationale**: Gives the maintainer an objective check next to the manual donation confirmation, and guarantees the live file is always a pure function of merged entries.

## R12. UI integration points found in the code

- Bottom tab bar has five fixed tabs, so Sponsors is reached from Settings (next to Roadmap), About, and Donation History (checked in the pages).
- Secondary pages like `/about` and `/roadmap` are in `HiddenNavPaths` in `MainLayout.razor`; `/sponsors` joins that list.
- The wallet already uses a named `HttpClient` per backend in `Program.cs`; sponsors adds two (raw reads, API submit).
- Locales: `wwwroot/locales/en.json` and `es.json`.

## R13. Privacy

- Loading the page contacts GitHub's raw host and submitting contacts our API; both see the visitor's IP, as the existing avatar, API, and price calls already do. No wallet data is sent. The entry text and transaction ID become public in a pull request, which the form states before sending (FR-030). The server does not store submissions; its logs keep the submission id and outcome only.

## R15. Automatic donation verification

- **Decision**: `DigiByte.Api` verifies the donation itself before opening a pull request, through two small explorer lookups of its own (`BlockbookTxLookup` for digibyteblockexplorer.com and `EsploraTxLookup` for digiexplorer.info, in `src/DigiByte.Api/Services/ExplorerTxLookup.cs`), tried in order. The wallet's explorer clients in `DigiByte.Wallet` were not reused: they turn every failure into "nothing", so they cannot tell a transaction that does not exist from an explorer that is down, and the spec requires that the check is never skipped or passed when no explorer can answer. The lookups return the transaction or null for a definite "not found" and throw otherwise; the verifier treats "not found" as trustworthy only when no explorer failed.
- **Rules**: transaction found; confirmations ≥ `Sponsors:MinConfirmations` (3, about 45 seconds on DigiByte's 15-second blocks); sum of outputs paying an address in `Sponsors:DonationAddresses` (the current `dgb1q...` address and the previous `D...` address, both taken from the Donation page) ≥ `Sponsors:MinDonationSatoshis` (1 DGB). Any explorer failure → `unavailable`; the check is never skipped.
- **Rationale**: Replaces the manual donation check, keeps maintainers to content review, and needs no new infrastructure or key.
- **Alternatives**: Own DigiByte node (node.dgbwallet.app; more authoritative but its readiness varies, per feature 005); free-text reference with manual check (rejected by the user); requiring an OP_RETURN message that binds the name to the transaction (stronger against reuse of someone else's public transaction ID but too demanding for donors, noted as a later hardening).

## R16. One donation, one entry (no database)

- **Decision**: The pull request branch name is `submission/<first 16 hex of txid>`, so two concurrent submissions with the same transaction collide on branch creation. The merged state is recorded by `data/donations/<txid>.txt` (content: the entry `id`), created by the pull request and checked by the server with one contents lookup on `sponsors`. A closed-unmerged pull request deletes its branch and frees the transaction.
- **Removal**: accepted only with the original transaction ID; the server reads the claim file to find the entry and image to delete. This replaces name-based removal and settles who may ask to remove an entry. The claim file stays after removal.
- **Known limit**: transaction IDs are public, so someone else could claim an unclaimed donation first; the maintainer's content review is the backstop, and the OP_RETURN binding above is the later hardening.

## R14. Duplicates and removal identity

- **Decision**: Duplicate entries in the list: same name (case-insensitive) + link + image. Removal is now by transaction ID through the donation claim (R16), not by name; the optional `id` on list entries remains for tooling.
