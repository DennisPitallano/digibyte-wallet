# Contract: `GET /api/health` (digibyte-api, digipay-api, node-api)

Existing behavior is unchanged. One field is added.

| Field | Type | Notes |
|---|---|---|
| `commit` | string | Commit the running build came from, read from `RAILWAY_GIT_COMMIT_SHA`. `"unknown"` when the variable is absent. |

Existing fields stay as they are (for example `status` and `timestamp`). The field is public and non-secret, since the repository is open source. A test per API asserts the field is present (T055).
