# Feature Specification: Sponsors Page

**Feature Branch**: `feature/sponsors-page` (spec directory `006-sponsors-page`)

**Created**: 2026-10-29

**Status**: Draft

**Input**: User description: "Help me create a sponsors page so it will list all folks who donate to the app in support of maintaining it and covering costs and development. But since we're not using a database here, can we use GitHub instead for the list and the avatars?" (Clarified later: GitHub is the database storage, a JSON file plus image files in the repository, with no GitHub usernames.)

## Findings From Initial Research

- **No database exists**: the wallet is a static, browser-only app. Anything shown on a sponsors page must come from a source that already exists outside the app, not from storage we operate. Because the app cannot write to GitHub on its own, the in-wallet form needs a small server-side step, which is the one place the project runs new code for this feature.
- **Existing donation surface covers on-chain gifts only**: the Donation History page shows DGB sent to the project's donation address. Those donors are anonymous addresses with no name or picture, so they cannot be listed as "folks" on a sponsors page.
- **GitHub is already a trusted outside source for the wallet**: the app already shows GitHub-hosted avatar images and already talks to GitHub's public API, so no new third party is introduced by using GitHub for the list and pictures.
- **GitHub acts as the database**: the sponsor list is a single JSON data file kept on a dedicated sponsors branch of this wallet's own GitHub repository (the main branch is never touched by sponsor changes), versioned and reviewable like any other change. Sponsor pictures are image files kept in the same repository. No separate repository or database is used, and entries hold no GitHub usernames. Sponsors do not need a GitHub account: they fill in a form inside the wallet, and a bot owned by the project opens the pull request against that branch for them. A maintainer then merges it after confirming the donation.
- **Content security policy gap**: the policy currently allows pictures from GitHub's avatar host and data requests to GitHub's API host, but not GitHub's raw-file host where a repository JSON file or image would be served. Decision: the list and images are loaded live, so the policy is extended to allow GitHub's raw-file host for this repository's content and nothing else. This is a security-relevant change that must be flagged in the pull request.
- **Donation can be checked automatically**: the wallet already reads transactions from public explorers and already knows the project's donation addresses (shown on the Donation page), so a server can confirm that a transaction ID really paid the project, how many blocks deep it is, and how much it paid.
- **Constitution constraints that apply**: Resilience (Principle V) — the page must work in a labeled degraded mode if GitHub is unreachable and must never block core wallet flows; the strict content security policy must not be relaxed to ship this feature. Principle II also says project servers are only static hosts, data proxies, or public-chain relays, so a server that accepts sponsor submissions needs an explicit exception or amendment.

## Clarifications

### Session 2026-10-30

- Q: Should the sponsor list update live from GitHub when the page opens, or be bundled into the app and update on the next release? → A: Live from GitHub, so a new sponsor shows up right away without a release; the security policy is extended to GitHub's raw-file host only.
- Q: Who puts a new sponsor on the list? → A: Sponsors submit their own entry, which ends up as a pull request to a dedicated sponsors branch of this repository, so the main branch is never affected; the app reads the list and images from that branch.
- Q: If someone adds an abusive, fake, or scam entry, who can remove it, and does anything check that they donated? → A: Reviewed: sponsors open the pull request, a maintainer confirms the donation and merges it into the sponsors branch, and maintainers can remove any entry at any time. (This refines the previous answer: sponsors prepare and submit, maintainers merge.)
- Q: How should a submission from the wallet form become a pull request without the sponsor having a GitHub account? → A: The form posts to one new endpoint on an API the project already runs; the server validates the submission and opens a pull request on the sponsors branch with a bot token kept server-side. Sponsors need no GitHub account. A maintainer still confirms the donation and merges. (This replaces the earlier assumption that sponsors open the pull request themselves.)
- Q: How is the donation checked? → A: Automatically against the public blockchain explorers. A submission must carry the DGB transaction ID of a confirmed payment to one of the project's donation addresses; each donation transaction can back only one entry; a maintainer no longer confirms donations by hand but still reviews the entry's content and merges. (This replaces the earlier manual donation check.)
- Q: How is a named person's consent confirmed, and how fast must a merged entry appear? → A: The form requires a confirmation that the submitter is the person named or has that person's permission to be listed, and a maintainer checks it before merging; merged entries appear within 10 minutes (the raw-file cache of about 5 minutes plus the list rebuild), not 5. The Sponsors page is reached from Settings, About and Donation History rather than the bottom tab bar.
- Q: What should "Add me to the list" show first? → A: The project's donation address as a QR code with a Copy button (step 1 of 2), then, after "I've donated, continue", the form (step 2 of 2). "Remove me" goes straight to its form.
- Q: What should be highlighted about the sponsors? → A: All three: the Sponsors links (a rose accent and a heart in the Welcome/Unlock footer, a rose border and a thank-you line in Settings and About), a "Newest supporters" spotlight of the three newest sponsors above the full list, and a small "Supported by" strip of the six newest on the Home screen that loads quietly in the background and hides on any problem. No amounts or tiers are used.
- Q: Should the mascot and a "where donations go" note be on the Sponsors page? → A: Yes. Baku appears with a thank-you message (through the existing Baku message system, so it follows the user's Baku setting), and a "Where your donations go" section lists hosting, our own DigiByte node, maintenance and security, and new features, with no amounts or percentages, and points to the public Donation page.
- Q: Whose token does the server use? → A: A dedicated bot identity controlled by the maintainer, limited to this repository with only the permissions needed to create branches and pull requests; never the maintainer's personal token, and never able to merge.
- Q: Where should the "Become a sponsor" button go? → A: Nowhere: the project has no GitHub Sponsors profile, so the page has no such button. The main call to action is "Donate DGB" (the in-app Donation page), next to "Add me to the list".
- Q: In what order should sponsors appear on the page? → A: The order of the list file, with the newest entry (last in the file) shown first; entries carry no date field.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See who supports the project (Priority: P1)

A wallet user (or visitor) opens the Sponsors page from the app's navigation and sees a grid of the people who support the project, each with a picture, display name, and (when they have one) a link to their website or social page. A short intro explains that sponsorships pay for hosting, the node, and development.

**Why this priority**: This is the whole point of the feature — public recognition of supporters, which also builds trust and encourages others to give.

**Independent Test**: Open the Sponsors page with at least one entry in the sponsor list stored in the wallet repository; confirm each sponsor appears with picture, name, and a working link where one is provided, and that the explanatory text is present.

**Acceptance Scenarios**:

1. **Given** the sponsor list in GitHub contains entries, **When** a user opens the Sponsors page, **Then** every listed sponsor is shown with picture, name, and a link when one is provided.
2. **Given** a sponsor entry names an image stored in the repository, **When** the page renders, **Then** that image is used as the sponsor's picture; **Given** it names no image, **Then** a neutral placeholder is used.
3. **Given** a sponsor has no picture or it fails to load, **When** the page renders, **Then** a neutral placeholder is shown in its place and the sponsor's name is still listed.
4. **Given** the list has sponsors, **When** the page renders, **Then** the three newest are also shown in a larger "Newest supporters" spotlight above the full list, and the Home screen shows a "Supported by" strip with the newest six that links here.
5. **Given** a new entry is merged into the sponsors branch, **When** the page is next loaded, **Then** the new sponsor appears within 10 minutes without rebuilding or redeploying the app.

---

### User Story 2 - Support the project (Priority: P1)

A user who wants to support the project sees a clear "Donate DGB" call to action on the Sponsors page that takes them to the existing Donation page. The page also offers a form to get listed (see User Story 5).

**Why this priority**: A sponsors page with no way to join does not fund anything; it is the other half of the feature's purpose.

**Independent Test**: Open the Sponsors page, activate "Donate DGB", and confirm it reaches the existing Donation page.

**Acceptance Scenarios**:

1. **Given** the Sponsors page is open, **When** the user activates "Donate DGB", **Then** they land on the existing Donation page inside the app.
2. **Given** a donor has given DGB, **When** they read the page, **Then** they see a clear "Add me to the list" action that opens the submission form.
3. **Given** the page has zero sponsors yet, **When** it renders, **Then** it shows a friendly "be the first to sponsor" message with the same call to action instead of an empty grid.

---

### User Story 3 - Page still works when GitHub is unreachable (Priority: P2)

A user opens the Sponsors page while offline or while GitHub is down or rate-limiting. The page shows the last successfully loaded list if one is available (marked as possibly out of date); otherwise it shows a short "couldn't load sponsors right now" message with a retry action. Join links keep working.

**Why this priority**: The constitution requires graceful degradation, and the wallet is used on unreliable mobile networks; but this only matters once Stories 1 and 2 exist.

**Independent Test**: Load the page once online, then go offline and reload; confirm the previous list appears with an "out of date" label. Clear saved data, stay offline, reload; confirm the error message and retry button appear and the rest of the app is unaffected.

**Acceptance Scenarios**:

1. **Given** the list loaded successfully before, **When** GitHub becomes unreachable and the page is opened again, **Then** the previously loaded list is shown with a note that it may be out of date.
2. **Given** no list was ever loaded and GitHub is unreachable, **When** the page opens, **Then** a clear error message with a retry action is shown and no broken images or blank grid appear.
3. **Given** the Sponsors page fails to load its data, **When** the user navigates to balance, receive, send, or history, **Then** those flows behave exactly as normal.

---

### User Story 4 - Sponsors choose how they appear (Priority: P3)

Sponsors keep control of their own visibility: a person appears only after agreeing to be listed, can ask to be removed at any time, and the page never displays donation amounts or tiers.

**Why this priority**: Privacy-respecting by default: nobody is listed without agreeing, and removal is a simple list edit.

**Independent Test**: Add one consenting entry, then remove it from the list; confirm it appears and then disappears, and that no amounts are shown at any point.

**Acceptance Scenarios**:

1. **Given** a donor has not agreed to be listed, **When** the list is shown, **Then** they do not appear anywhere on the page.
2. **Given** a sponsor sends a removal request through the same form and a maintainer merges the resulting pull request, **When** the page is next loaded after the change goes live, **Then** they no longer appear.

---

### User Story 5 - Submit my own entry from the wallet (Priority: P2)

A donor, with no GitHub account, opens a form on the Sponsors page and enters the name to show, an optional link, an optional picture, and the DGB transaction ID of their donation, which the system checks automatically. After submitting, they see a confirmation that their entry is waiting for review. Behind the scenes a bot opens a pull request on the sponsors branch; a maintainer reviews the entry and merges it. The same form, with the same transaction ID, can be used to ask for the entry to be removed.

**Why this priority**: Without it, listing depends on people finding and using GitHub, which most donors will not do; but the page is already useful without it, so it follows Stories 1-2.

**Independent Test**: Submit a valid entry from the form with no GitHub account; confirm a pull request appears on the sponsors branch containing exactly that entry, the page shows nothing new until a maintainer merges it, and the entry appears within 10 minutes after the merge.

**Acceptance Scenarios**:

1. **Given** a visitor chooses "Add me to the list", **When** the form area opens, **Then** they first see the project's donation address as a QR code with a Copy button and a "I've donated, continue" button, and only after continuing do they see the form (with a way back).
2. **Given** a visitor with no GitHub account, **When** they submit a valid name (and optional link and picture) and the transaction ID of a confirmed donation, **Then** they see a confirmation that the entry is awaiting review, and a pull request containing only that entry is opened on the sponsors branch.
3. **Given** the visitor has not ticked the confirmation that they are the named person or have permission, **When** they try to submit an add request, **Then** the form refuses and explains why; nothing is sent.
4. **Given** a submission breaks a rule (empty or over-long name, non-https link, picture of the wrong type or too large, a transaction ID that is not a valid ID), **When** the visitor submits, **Then** they see which field to fix and nothing is sent to GitHub.
5. **Given** a maintainer has reviewed the entry and merges the pull request, **Then** the entry appears on the page within 10 minutes and the main branch is unchanged.
6. **Given** the transaction ID cannot be verified (not found, fewer than the required confirmations, not paid to a project donation address, below the minimum amount, or already used for another entry), **When** the visitor submits, **Then** they see why the donation was not accepted and no pull request is opened.
   - **Given** the transaction was just sent and is not yet confirmed enough, **Then** the message tells them to try again shortly.
   - **Given** the public explorers cannot be reached, **Then** the visitor sees the "could not be sent, try again later" message and nothing is opened.
7. **Given** the submission service is unreachable or refuses the request, **When** the visitor submits, **Then** they see a plain message that it could not be sent and can try again later; the rest of the wallet is unaffected.
8. **Given** a visitor sends many submissions in a short time, **Then** further submissions are refused with a clear message.
9. **Given** a visitor chooses the removal option, **When** they submit the transaction ID that originally created their entry, **Then** a pull request removing that entry is opened for a maintainer to review; a transaction ID that never created an entry opens nothing.

---

### Edge Cases

- A sponsor's display name is empty or very long: long names are truncated cleanly without breaking the layout.
- Very many sponsors (hundreds): the page stays responsive and usable on a phone; the list is paged or grouped rather than one endless scroll.
- GitHub rate-limits requests: treated the same as "unreachable" (cached list or error state), never a crash or blank page.
- An entry points to an image that is missing, too large, or not an image: the placeholder is shown instead and the rest is unaffected.
- An entry has no image and no link: it still appears with a placeholder picture and its name.
- The list file is malformed or contains a duplicate or invalid entry: invalid entries are skipped, duplicates are shown once, and the rest of the page works. If the whole file is unreadable, the page shows the stale-list or error state.
- Dark mode and light mode: pictures and text remain legible in both.
- A sponsor name or profile text contains markup or script-like content: it is displayed as plain text only.
- A form submission is sent twice (double tap, retry): at most one pull request results for identical content.
- Spam or bot floods the form: per-visitor rate limiting and a cap on open pull requests stop it from flooding maintainers; a refused visitor sees a clear message.
- The entry text and the transaction ID appear in the public pull request description (the transaction ID is already public on the blockchain): the form warns visitors not to enter private information.
- One donation transaction pays two people who both submit it: the first valid submission wins; the second is told the donation was already used.
- A donation was made to the older donation address: it still counts, as both project addresses are accepted.
- Someone submits another person's name: the form's confirmation and the maintainer's check are the safeguards, and the named person can ask for removal through the same form only if their own donation created the entry.
- Someone uses another person's public transaction ID: the entry still goes to a maintainer for review before it can appear, and the real donor can submit the same ID for removal only if it created their entry.
- The bot's token is lost or revoked: submissions fail with the "could not be sent" message; nothing else in the app is affected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST provide a Sponsors page reachable from the Settings list, the About page, the Donation History page, and the footer links of the Welcome and Unlock screens (not the bottom tab bar), with a stable, shareable address.
- **FR-002**: The page MUST list every sponsor in the sponsor list stored in the wallet repository, showing picture, display name, and a link when one is provided.
- **FR-003**: GitHub MUST be the only storage for this feature: the list is a JSON file generated from one entry file per sponsor, kept on a dedicated sponsors branch of this wallet's repository, and sponsor pictures are image files on the same branch. The project MUST NOT introduce a database or any new standalone service to support it; the only server-side addition allowed is one submission endpoint on an API the project already operates (see FR-024 to FR-030).
- **FR-004**: Adding, editing, or removing an entry in the sponsor list MUST be reflected live on the page within 10 minutes, without an app rebuild or redeploy.
- **FR-005**: The page MUST list only people who agreed to be listed (see FR-034), and MUST NOT display donation amounts, tiers, or dates.
- **FR-006**: The page MUST include a prominent "Donate DGB" action that opens the existing DGB Donation page.
- **FR-007**: The page MUST include a short plain-language statement that sponsorships fund hosting, the DigiByte node, maintenance, and development.
- **FR-008**: When there are no sponsors, the page MUST show a friendly empty state with the call to action.
- **FR-023**: The page MUST show sponsors in list-file order, newest entry first, with no date or amount used for ordering.
- **FR-016**: Each list entry MUST need only a display name (everything else optional: image file, link).
- **FR-020**: The app MUST read the list and images only from the sponsors branch named in FR-003; merging sponsor entries MUST NOT change the main branch or trigger an app release.
- **FR-022**: Only maintainers MUST be able to merge into the sponsors branch (the single exception is an automated workflow on that branch that commits the generated list file); an entry MUST be merged only after its donation passed the automatic check and a maintainer has reviewed the entry, and maintainers MUST be able to remove any entry at any time.
- **FR-021**: Because entries are self-submitted and public, links MUST be https-only and open as external links, and images MUST be limited to accepted types and rendered in a small fixed box; type and size are enforced when the entry is submitted and again before merge, and anything failing at display time is shown as placeholder or without a link.
- **FR-018**: A sponsor's picture MUST be its repository image if given, otherwise the neutral placeholder.
- **FR-019**: Repository images MUST have a size limit and accepted formats stated for sponsors, so a single oversized image cannot slow the page.
- **FR-017**: Invalid, duplicate, or unresolvable entries MUST be skipped or degraded individually without breaking the page.
- **FR-009**: When GitHub cannot be reached or rate-limits, the page MUST show the last successfully loaded list marked as possibly out of date, or, if none exists, a clear error with a retry action.
- **FR-010**: A missing or failed picture MUST show a neutral placeholder without layout shift or a broken-image icon.
- **FR-011**: The Sponsors page, its form and the Home strip MUST NOT block, delay, or change any core wallet flow (balance, receive, send, history, unlock). The list is loaded when the Sponsors page opens, and, for the Home strip, in the background after Home has rendered (see FR-038); nothing is requested at app start or before Home has rendered.
- **FR-012**: The feature MAY extend the app's content security policy only to allow GitHub's raw-file host for this repository's list and images, MUST NOT relax it in any other way, and MUST NOT load third-party scripts, fonts, or trackers.
- **FR-013**: All sponsor-supplied text MUST be rendered as plain text, never as markup.
- **FR-014**: The page MUST work on phone-width screens and in both light and dark themes, consistent with the rest of the app.
- **FR-015**: The page and form MUST NOT ask for, read, or transmit any wallet data (addresses, balances, keys); they are usable without a wallet being created or unlocked.
- **FR-024**: The Sponsors page MUST include a submission form with: display name (required), link (optional), picture (optional), the DGB transaction ID of the donation (required), and a request type (add or remove).
- **FR-025**: The form MUST validate every field with the same rules as the list (name 1-60 characters, https-only link, accepted picture type and size) before sending, and the server MUST re-validate and refuse anything invalid.
- **FR-026**: A valid submission MUST result in a pull request on the sponsors branch that adds (or removes) exactly that one entry and its picture; submissions MUST NOT write to the main branch or directly to the sponsors branch.
- **FR-027**: The submission step MUST use a dedicated bot identity limited to this repository, with only the permissions needed to create branches and pull requests, and the bot MUST NOT be able to merge.
- **FR-028**: The bot's credential MUST be held only on the server, never in the wallet, the repository, logs, or error messages.
- **FR-029**: The submission endpoint MUST rate-limit per visitor, cap the number of open submission pull requests, and avoid duplicate pull requests for identical content, without relying on third-party scripts or trackers in the wallet.
- **FR-031**: Before opening any pull request, the server MUST verify the transaction ID automatically through the public blockchain explorers: the transaction exists; it has at least the required number of confirmations; one of its outputs pays a project donation address; and the amount paid to the project is at least the minimum. Starting values are 3 confirmations and 1 DGB, adjustable without a spec change.
- **FR-032**: Each donation transaction MUST back at most one entry: a second submission with the same transaction ID MUST be refused, and the claim MUST survive across pull requests and server restarts without a database. A removal request MUST be accepted only with the transaction ID that created the entry.
- **FR-033**: The check MUST use the same explorer fallback as the rest of the wallet, and when no explorer can be reached the submission MUST fail with the "try again later" message rather than skip the check.
- **FR-035**: The Sponsors page MUST show donors who gave by a channel the form cannot verify how to reach the maintainer, through a visible "Contact the maintainer" link.
- **FR-036**: Choosing "Add me" MUST first show the project's donation address as a QR code (a payment link with the minimum amount filled in) with a Copy button and a continue button; the form appears only after continuing, with a way back to the donation details. "Remove me" MUST go straight to its form.
- **FR-037**: The Sponsors page MUST show a "Newest supporters" spotlight with the three newest sponsors in larger tiles above the full list; everyone, including those three, stays in the full list. No amounts, tiers or dates are shown.
- **FR-038**: The Home screen MUST show a small "Supported by" strip with the six newest sponsors and a link to the Sponsors page. It MUST load after Home has rendered, MUST reuse a saved copy up to 30 minutes old instead of asking GitHub again, and MUST stay hidden (no error, no spinner) when there are no sponsors or the list cannot be loaded.
- **FR-039**: The links to the Sponsors page MUST stand out from their neighbours: an accent colour and a heart in the Welcome and Unlock footer links, and an accent border and a thank-you line in the Settings and About rows.
- **FR-040**: The Sponsors page MUST show the Baku mascot with a thank-you message, using the app's existing Baku message system so that it follows the user's Baku setting (hidden when Baku is turned off) and never blocks the page.
- **FR-041**: The Sponsors page MUST include a "Where your donations go" section, visible in every page state (loading, list, empty, offline, error), that names what donations pay for (hosting, our own DigiByte node, maintenance and security, new features), shows no amounts or percentages, and links to the public Donation page.
- **FR-034**: The form MUST require a confirmation, for an add request, that the submitter is the person named or has that person's permission to be listed, and MUST refuse to send without it; the pull request MUST record that the confirmation was given, and a maintainer MUST check it (and that the name is plausible for the donation) before merging.
- **FR-030**: The form MUST show a confirmation after success and a plain-language message on failure or refusal (no technical terms or status codes, 20 words or fewer, and saying what the visitor can do next), MUST warn that the entry and transaction ID appear publicly in a pull request, and MUST NOT block or slow any core wallet flow.

### Key Entities

- **Sponsor**: A person who supports the project and agreed to be listed. Attributes: display name (required), optional image file, optional link.
- **Sponsor List**: The JSON file on the sponsors branch of the GitHub repository, generated from the Sponsor entry files, holding all Sponsor entries, plus when it was last fetched by the app, so the page can label stale data.
- **Sponsor Image**: An image file stored in the repository and referenced by a Sponsor entry.
- **Submission**: A request from the form to add or remove one entry: name, optional link, optional picture, donation transaction ID, request type.
- **Donation Claim**: The record that one donation transaction ID has been used for one entry; stored in the same repository. It exists only until it becomes a pull request.
- **Submission Pull Request**: The review item on the sponsors branch that a maintainer merges or closes.
- **Support Links**: The ways to give and to get help — the in-app DGB Donation page, and the contact link for donors who gave another way.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor can open the Sponsors page and see the sponsor list in under 3 seconds on a throttled "Fast 4G" profile (about 9 Mbps down, 170 ms latency) with an empty cache.
- **SC-002**: A donor with no GitHub account can submit an entry from the wallet in under 2 minutes (timed from opening the form to seeing the confirmation, with the donation already confirmed), and once a maintainer merges it, it appears live on the page within 10 minutes with no rebuild or redeploy.
- **SC-003**: 0 donors who have not agreed to be listed are ever displayed, and 0 amounts are ever displayed.
- **SC-004**: From opening the page, a user can reach the Donation page in one tap/click.
- **SC-005**: With GitHub unreachable, the page still renders a usable state (cached list or error with retry) 100% of the time, and core wallet flows are unaffected.
- **SC-006**: The page is fully usable at 360 px width with no horizontal scrolling and no unreadable text in dark or light mode.
- **SC-007**: No database or new standalone service is added, the only new allowed domain is GitHub's raw-file host, and the only server-side change is one submission endpoint on an existing API.
- **SC-008**: 100% of valid submissions produce exactly one pull request, and 0 submissions ever change the main branch or the live list without a maintainer merge.
- **SC-011**: 100% of add submissions that reach a pull request carry the submitter's recorded confirmation of consent.
- **SC-012**: Opening Home contacts GitHub for the sponsors list at most once per 30 minutes, and never delays Home's own content.
- **SC-010**: 0 entries are ever opened for review without a verified donation, and 0 donation transactions back more than one entry.
- **SC-009**: 0 credentials or tokens appear in the wallet code, the repository, or any response sent to the browser.

## Assumptions

- "Folks who donate" means anyone who submits an entry through the wallet form after giving, Only DGB on-chain donations can be checked automatically, so the form accepts DGB donations only. Donors who gave another way (anything other than DGB on-chain) contact the maintainer through a "Contact the maintainer" link on the Sponsors page (the maintainer link already shown on the About page, and the repository's issues page); they are not handled by the form. The contact channel is an assumption and can be changed. No GitHub account is needed.
- The sponsor list is a JSON file, and sponsor images are files, on a dedicated sponsors branch of this wallet's public GitHub repository; GitHub's history gives an audit trail. Anything stored there is public, and a sponsor submitting an entry consents to publishing exactly what they add. Donations are verified automatically; maintainers review content and merge.
- The submission endpoint is added to an API the project already runs and that the wallet's security policy already allows, so no new domain is needed; creating the bot identity and storing its token on that server are operator tasks.
- The accepted donation addresses are the two shown on the Donation page (current and previous); the minimum donation (1 DGB) and the required confirmations (3) are starting values, adjustable by configuration.
- Consent is self-declared by the submitter and checked by a maintainer; the project does not contact the named person.
- Starting limits: 3 submissions per visitor per hour and at most 50 open submission pull requests; these can be tuned without a spec change.
- Adding a server role that accepts sponsor submissions needs an explicit exception to, or amendment of, the constitution's server-role rule (Principle II), recorded in the plan.
- The app reads the list directly from GitHub when the page opens, so list changes show live without redeploying; caching of up to 5 minutes plus the list rebuild (about 1 to 2 minutes) is acceptable, so a merged entry may take up to 10 minutes.
- Sponsors are shown in list-file order with the newest entry (last in the file) first; entries have no date field and there is no ranking by amount.
- Serving the list and images live from GitHub is acceptable as long as only GitHub's raw-file host for this repository is added to the security policy and the rest stays as is.
- Localization follows the app's existing language support; the page text is English at launch.
- Out of scope: sponsor-only perks, badges inside the wallet, sponsor logos/banners for companies, donation amount leaderboards, and thank-you emails.
