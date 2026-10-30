# Contract: Sponsor Submission API

One endpoint added to the existing `DigiByte.Api` service. Shared rules: `src/DigiByte.P2P.Shared/Sponsors/SponsorRules.cs`. Data shapes: [data-model.md](../data-model.md).

## `POST /api/sponsors/submissions`

- **Caller**: the wallet only (CORS policy `WasmClient`); no authentication, by design.
- **Content type**: `multipart/form-data`, total body ≤ 300 KB.

### Request fields

| Field | Required | Notes |
|-------|----------|-------|
| `type` | Yes | `add` or `remove` |
| `name` | Yes for `add` | 1–60 characters, no control characters; not used for `remove` |
| `link` | No | `https` only, ≤ 200 characters, no credentials |
| `txid` | Yes | DGB transaction ID (64 hex characters); verified automatically; for `remove`, the transaction ID that created the entry |
| `image` | No | WebP, PNG or JPEG ≤ 200 KB with a valid signature for its type; ignored for `remove` |
| `consent` | Yes for `add` | Must be `true`; otherwise `400 invalid` with field `consent` |
| `website` | No | Honeypot; if non-empty the request is accepted silently and nothing is sent to GitHub |

### Responses

| Status | Body | When |
|--------|------|------|
| `201` | `{ "state": "created" }` | Pull request opened |
| `200` | `{ "state": "duplicate" }` | Identical submission already open (branch exists) |
| `400` | `{ "state": "invalid", "fields": ["name", ...] }` | Any rule failed; field names only, never echoing submitted content |
| `413` | `{ "state": "invalid", "fields": ["image"] }` | Body or image too large |
| `409` | `{ "state": "already_used" }` | The transaction already backs an entry or an open pull request |
| `409` | `{ "state": "name_taken", "fields": ["name"] }` | A listed sponsor already has this name (ignoring case and extra spaces); checked before the donation lookup. Add requests only |
| `422` | `{ "state": "not_verified", "reason": "not_found" \| "too_few_confirmations" \| "wrong_recipient" \| "below_minimum" }` | The donation check failed |
| `429` | `{ "state": "rate_limited" }` | More than 3 submissions per hour from one client IP; `Retry-After` header set |
| `503` | `{ "state": "queue_full" }` | 50 or more open submission pull requests |
| `502` | `{ "state": "unavailable" }` | GitHub error, bot token missing or rejected; details only in server logs |

Responses never contain the token, GitHub error bodies, or pull request URLs.

### Server behaviour (in order)

1. Enforce size limit and rate limit.
2. Honeypot check.
3. Validate fields with `SponsorRules`; check the image size and signature (PNG, JPEG or WebP).
4. Check the claim: `data/donations/<txid>.txt` must not exist on `sponsors` (for `add`) or must exist (for `remove`); check the open-PR cap (cached 60 seconds).
5. Verify the donation through the explorer clients (fallback order as in the wallet): the transaction exists, has at least `MinConfirmations`, and pays at least `MinDonationSatoshis` to one of `DonationAddresses`. No reachable explorer → `502 unavailable`; the check is never skipped.
6. Create `submission/<first 16 hex of txid>` from the head of `sponsors`; a "branch exists" error returns `already_used`.
7. Commit `data/entries/<id>.json`, `data/donations/<txid>.txt` (content: the entry `id`) and optionally `data/images/<id>.<detected extension>`; for `remove`, delete the entry and image files named by the claim file.
8. Open the pull request into `sponsors` titled `Sponsor submission: <name>` (name sanitised), labels `sponsor-submission` and `donation-verified`, body with the transaction ID, the recorded consent statement, and a public-text notice.
9. On any failure after step 6, delete the new branch best-effort and return `502`.

### Configuration (server only)

| Setting | Purpose | Secret |
|---------|---------|--------|
| `Sponsors__GitHubToken` | Bot token (Contents RW, Pull requests RW, this repo only) | **Yes — environment variable only** |
| `Sponsors:Repository` | `DennisPitallano/digibyte-wallet` | No |
| `Sponsors:Branch` | `sponsors` | No |
| `Sponsors:MaxOpenSubmissions` | 50 | No |
| `Sponsors:PerVisitorPerHour` | 3 | No |
| `Sponsors:DonationAddresses` | `dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa`, `DPSNEw5UmtUMQwTZPNoiUFDmCLu3Tp5SrK` | No |
| `Sponsors:MinConfirmations` | 3 | No |
| `Sponsors:MinDonationSatoshis` | 100000000 (1 DGB) | No |

If the token is absent the endpoint returns `502 unavailable`; the rest of the API is unaffected.

### Logging

Log the pull-request branch prefix, type, and outcome only. Never log the token, request body, name, link, or full transaction ID.
