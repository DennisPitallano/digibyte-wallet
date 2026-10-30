# DigiByte Wallet sponsors data

This branch holds the list of people who support the [DigiByte Wallet](https://github.com/DennisPitallano/digibyte-wallet)
and is read live by the wallet's **Sponsors** page. It contains data, scripts and workflows only, and it shares no history with `main`.

## How to get listed

Use the form on the wallet's Sponsors page. You do not need a GitHub account.

1. Donate at least 1 DGB to the project and wait for about 3 confirmations.
2. Open the Sponsors page, choose **Add me to the list**, enter the name to show, an optional https link and picture, and the
   transaction ID of your donation.
3. The donation is checked automatically and a pull request is opened for you. A maintainer reviews the entry and merges it.
4. Your entry appears on the page within about 10 minutes of the merge.

To be removed, use **Remove me** with the same transaction ID. Donors who gave another way (for example GitHub Sponsors) can
contact the maintainer through the link on the Sponsors page.

Everything on this branch is public, including the name, link, picture and transaction ID you submit.

## Layout

| Path | What it is |
|------|------------|
| `data/entries/<id>.json` | One file per sponsor: `id`, `name`, optional `image` and `link` |
| `data/images/<id>.<webp\|png\|jpg>` | The sponsor's picture (at most 200 KB and 512x512) |
| `data/donations/<txid>.txt` | Claim: this donation backs entry `<id>`. One donation, one entry |
| `data/sponsors.json` | **Generated** list the wallet reads. Never edit by hand |
| `schema/sponsors-list.schema.json` | JSON Schema for the list and entry files |
| `scripts/` | `validate.py`, `build_index.py`, `check_scope.py` (standard library only) |

## Rules (checked by `validate-sponsors` on every pull request)

- Name: 1 to 60 characters, no control characters. Link: `https` only, at most 200 characters, no credentials.
- Pictures: PNG, JPEG or WebP, at most 200 KB and 512x512 pixels. No SVG.
- No duplicate entries (same name, link and picture). Every entry has exactly one donation claim.
- A submission pull request may only change its own entry, picture and claim.

## Maintainers

Only maintainers can merge here; the GitHub Actions identity may bypass the pull-request rule solely to commit the generated
`data/sponsors.json`. The bot that opens submission pull requests cannot merge. See
`specs/006-sponsors-page/` on `main` for the full design.
