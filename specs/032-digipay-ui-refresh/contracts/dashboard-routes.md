# Contract C2: Merchant Dashboard Routes

These are the public URLs of the merchant dashboard. Merchants bookmark them, so once released they MUST keep working (FR-014, FR-019).

## Routes

| Route | Page | Notes |
|---|---|---|
| `/merchant` | Overview | Kept for existing bookmarks. Shows Overview; no redirect needed. |
| `/merchant/overview` | Overview | |
| `/merchant/new` | New payment | Target of the raised bottom-menu button and the Overview button |
| `/merchant/sessions` | Sessions list | Query: `status` (one of the API statuses, or `all`), `q` (search text), `page` (1-based) |
| `/merchant/sessions/{SessionId}` | Session detail | Unchanged from today. Back returns to the list URL it came from, or `/merchant/sessions` |
| `/merchant/links` | Payment links | |
| `/merchant/analytics` | Analytics | Query: `range` (`24h`, `7d`, `30d`, `90d`) |
| `/merchant/donation` | Donation button | |
| `/merchant/webhook` | Webhook and delivery log | Query: `delivery` (`failed` or `all`) |
| `/merchant/stores` | Stores | |
| `/merchant/api-keys` | API keys | |
| `/merchant/audit` | Audit log | Query: `page` |
| `/merchant/pos` | POS mode | |
| `/merchant/settings` | Settings | |
| `/merchant/signin` | Digi-ID sign-in | Query: `return` (see guard below) |
| `/merchant/{anything else}` | DigiPay not-found page | Includes a "Back to Overview" link |

**Common query parameter**: `store` (store id), accepted on every `/merchant/*` route except `signin`. If it names a store the merchant doesn't own, the page shows not-found and never shows that store's data. If it's missing, the last used store applies.

## Sign-in return guard

`/merchant/signin?return=<value>` sends the merchant to `<value>` after a successful sign-in, but only if all of these hold:

- it is a relative path that starts with `/merchant` and is not `/merchant/signin`;
- it does not start with `//` or `/\`, and has no scheme or host;
- after URL-decoding, it still meets both rules above.

Anything else goes to `/merchant`.

Unit tests (`ReturnUrlGuardTests`) cover these cases: absolute URLs, protocol-relative URLs, backslashes, encoded `//`, `javascript:` and paths outside `/merchant`.

## Navigation behaviour

- **History**: every section change is a real navigation, so it creates a browser history entry. Back and forward move between sections (FR-015).
- **Filter and page changes** update the query with `replace` semantics, so typing in search doesn't fill history. Changing the status filter or the page adds an entry.
- **Menu**:
  - **Below 1024px**: a bottom menu with Overview, Sessions, New payment (raised), Payment links and More. More opens a sheet listing the other sections and the store switcher.
  - **From 1024px**: a left rail with every section, the store switcher, a Resources group, the account and Sign out.
  - **Current item**: marked with `aria-current="page"`.
- **Page titles**: `<Section> · DigiPay`. Focus moves to the page `h1` on navigation (today's `FocusOnNavigate` behaviour).
