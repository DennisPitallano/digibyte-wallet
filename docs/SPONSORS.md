# Sponsors

The wallet's **Sponsors** page (`/sponsors`) lists the people who support the project. This page explains how to get listed, what is public, and how the pieces fit together. The design lives in `specs/006-sponsors-page/`.

## How to get listed

You do not need a GitHub account.

1. Open **Sponsors** and choose **Add me to the list**. Step 1 shows the project's donation address as a QR code (and a Copy button). **Donate** at least 1 DGB and wait for about 3 confirmations, then tap **I've donated, continue**.
2. Step 2 is the form. Fill in:
   - the name to show (1 to 60 characters),
   - an optional link (`https` only),
   - an optional picture (PNG, JPEG or WebP; the wallet shrinks it to a small square before sending),
   - the **transaction ID** of your donation (64 characters, 0-9 and a-f),
   - and tick the confirmation that you are the person named or have their permission.
3. The server checks the donation automatically against the public explorers and opens a pull request on the `sponsors` branch. A maintainer reviews the entry and merges it.
4. Your entry shows up on the page within about 10 minutes of the merge.

To be **removed**, use **Remove me** and enter the transaction ID of the donation that created your entry. A maintainer reviews the request.

Donated another way (anything other than DGB)? Use the **Contact the maintainer** link on the Sponsors page.

## Where sponsors show

- The **Sponsors** page: a "Newest supporters" spotlight (the three newest) above the full list.
- The **Home** screen: a small "Supported by" strip with the five newest and a link to the page.
- Links to the page: Settings, About, Donation History, and the footer of the Welcome and Unlock screens.

## What is public

Everything you submit is published: your name, link, picture and the donation transaction ID appear in a pull request on GitHub, and the name, link and picture appear on the Sponsors page. The transaction ID is already public on the blockchain. Do not enter anything private. No amounts, tiers or dates are shown.

## Limits

| Item | Limit |
|------|-------|
| Name | 1 to 60 characters, no control characters |
| Link | `https` only, at most 200 characters, no embedded credentials |
| Picture | PNG, JPEG or WebP, at most 200 KB and 512×512 pixels (no SVG) |
| Donation | At least 1 DGB to a project address, at least 3 confirmations, and each transaction backs one entry |
| Submissions | 3 per visitor per hour; at most 50 open submission pull requests at a time |

The amount (1 DGB), the confirmations (3) and the limits are settings (`Sponsors:*` in `src/DigiByte.Api/appsettings.json`) and can be tuned without a code change.

## How it works

```text
Sponsors page ──GET──▶ raw.githubusercontent.com/<repo>/sponsors/data/sponsors.json   (read live, last good copy kept offline)
Sponsors form ──POST─▶ DigiByte.Api  /api/sponsors/submissions
                         ├─ validates the entry with the same rules as the wallet (DigiByte.P2P.Shared)
                         ├─ checks the donation on digiexplorer.info and digibyteblockexplorer.com
                         └─ opens a pull request on a submission/* branch using the bot token (server-side only)
maintainer merges ──▶ build-sponsors-index workflow regenerates data/sponsors.json
```

- Data lives on the orphan **`sponsors`** branch: `data/entries/<id>.json` (one per sponsor), `data/images/`, `data/donations/<txid>.txt` (one file per donation; a transaction can be used once, and a sponsor with several claims shows "Donated N times") and the generated `data/sponsors.json`. Merging never touches `main`.
- The submission endpoint is a *public-contribution intake relay* (constitution 1.1.0, Principle II). It is anonymous by design, so it is limited instead: rate limit, open-pull-request cap, size limits, automatic donation check, and a bot that can only open pull requests.
- The wallet reads the list from the `sponsors` branch only; the content security policy allows exactly that path of `raw.githubusercontent.com` and nothing else on that host.

## Privacy

Opening the Sponsors page contacts GitHub's raw-file host, and so does the small "Supported by" strip on the Home screen (in the background after Home has loaded, at most once every 30 minutes, and hidden if it fails), and submitting contacts the project's API. Both can see the visitor's IP address, as the wallet's other price and explorer requests already do. No wallet data is read or sent, and the server does not store submissions: its logs keep only the type, the first 8 characters of the transaction ID and the outcome.

## For operators

One-time setup (not application code):

1. Push the `sponsors` branch (orphan; data, scripts and workflows only).
2. Create a **bot account** with write access to this repository only, and a fine-grained token (Contents and Pull requests, read and write, this repository only). Set it on the API host as the environment variable **`Sponsors__GitHubToken`**. Never put it in a file, log or response.
3. Rulesets on **`main`** and **`sponsors`**: pull request required, merges restricted to maintainers, head branches deleted after merge. On `sponsors` only, allow the **GitHub Actions** identity to bypass, so the index workflow can commit the generated list. Confirm the bot can neither merge nor push directly.
4. Create the `sponsor-submission` label (the API also adds `donation-verified` and `removal-request`).

If the token is missing, the form answers "could not be sent" and nothing else in the app is affected.
