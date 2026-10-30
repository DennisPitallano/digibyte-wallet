# Contract: Hosting and UI

## 1. Hosting contract (GitHub)

| Item | Value |
|------|-------|
| Repository | `DennisPitallano/digibyte-wallet` (public) |
| Branch | `sponsors` (orphan; data, docs, workflows only) |
| Generated list URL | `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/data/sponsors.json` |
| Image URL | `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/data/images/<file>` |
| Entry files | `data/entries/<id>.json`, one per sponsor (schema: [sponsors-list.schema.json](sponsors-list.schema.json)) |
| Donation claims | `data/donations/<txid>.txt`, content = entry `id`; created by the submission pull request, never deleted |
| Submission branches | `submission/<first 16 hex of txid>`, created only by the bot, deleted after merge |
| Image rules | From the form: WebP, or PNG/JPEG where the browser cannot encode WebP (≤ 200 KB); hand-added PNG/JPEG/WebP allowed; all ≤ 512×512 |
| Rulesets | `main` and `sponsors`: pull request required, merges restricted to maintainers; the GitHub Actions identity may bypass on `sponsors` only (to commit the generated list); head branches deleted after merge |
| Workflows (on `sponsors`) | `validate-sponsors` (PR checks), `build-sponsors-index` (regenerates `sponsors.json` on push) |
| Freshness | Raw-host cache lifetime (~5 minutes) plus workflow run time (about 1 to 2 minutes) gives a bound of 10 minutes for a merge to appear |

Changing the repository or branch name requires updating `Sponsors:*` settings, `Sponsors:BaseUrl`, and the content security policy.

## 2. Wallet configuration (`wwwroot/appsettings.json`)

| Setting | Default | Notes |
|---------|---------|-------|
| `Sponsors:BaseUrl` | `https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/data/` | Must stay inside the CSP-allowed prefix |
| `Sponsors:SubmitUrl` | `<PriceApiUrl host>/api/sponsors/submissions` (i.e. `https://digibyte-api-production.up.railway.app/api/sponsors/submissions`) | Host is already in the CSP `connect-src` |

## 3. Content security policy change

Add to **both** `img-src` and `connect-src` in `src/DigiByte.Web/wwwroot/index.html`:

`https://raw.githubusercontent.com/DennisPitallano/digibyte-wallet/sponsors/`

Nothing else changes. Flag as security-relevant in the PR (merge gate).

## 4. UI contract — `/sponsors`

| Element | Behaviour |
|---------|-----------|
| Route | `/sponsors`, hidden bottom tab bar like About/Roadmap |
| Entry points | Settings list (next to Roadmap), About page, Donation History page |
| Intro | One short statement that sponsorships fund hosting, the node, maintenance and development |
| Grid | Fixed square picture box, name, optional link; plain text only; `rel="noopener noreferrer"`, new tab for links |
| Picture | Repository image, otherwise (or on load error) a neutral letter placeholder; no layout shift |
| Paging | First 48 entries, "Show more" adds 48 |
| Join section | Primary "Donate DGB" link to `/donations`, "Add me to the list" which opens the form, and a "Contact the maintainer" link for donors who gave another way |
| States | `Fresh`, `FreshEmpty`, `Stale` (with fetch time), `Unavailable` (with Retry) |

### Submission form (`SponsorSubmitForm`)

| Element | Behaviour |
|---------|-----------|
| Flow | "Add me": step 1 of 2 shows the donation address as a QR code (BIP21 link, minimum amount filled in) with a Copy button and "I've donated, continue"; step 2 of 2 is the form, with a way back. "Remove me" goes straight to its form |
| Fields | Request type (add / remove), name, optional link, optional picture, donation transaction ID (64 hex characters), a required consent confirmation for add requests ("I am this person, or I have their permission to be listed"); hidden honeypot. For remove, only the transaction ID is needed |
| Picture | File picker; the wallet resizes to at most 256×256 and re-encodes (WebP, or JPEG where the browser cannot encode WebP) before sending, and shows a preview |
| Validation | Same rules as the server (shared rules); inline field messages; nothing is sent until all pass |
| Notice | Visible warning before sending: the name, link, picture and transaction ID will appear publicly in a pull request |
| Results | `created` / `duplicate` → "Submitted, awaiting review"; `invalid` → highlights fields; `not_verified` → explains why (not found, not enough confirmations yet, not paid to the project, below minimum); `already_used` → "this donation was already used"; `rate_limited` / `queue_full` → "Try again later"; `unavailable` or network error → "Could not be sent, try again later" |
| Isolation | No wallet service injected; never reads or sends wallet data; failures never affect other pages |
