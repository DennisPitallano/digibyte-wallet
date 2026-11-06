# Contract: `POST /api/digiid/callback`

**Request**: `{ "uri": "digiid://host/path?x=nonce", "address": "D...", "signature": "...", "callbackUrl": "optional" }`

**Rules**
1. `uri` must parse as `digiid://`. The target is `https://host[:port]/path?x=nonce`. `u=1` (http) is rejected.
2. If `callbackUrl` is present it must equal the derived target, else `400`.
3. The signature must verify for `uri` and `address`, else `400`.
4. The target must pass the outbound guard ([webhook-url-policy.md](webhook-url-policy.md)), else `400` with a generic message.
5. No redirects followed. Timeout and response cap apply. Rate limited per IP, else `429`.

**Response from the site**: relayed as today: the site's status code, with `{ "status": <upstream status>, "body": <truncated upstream text> }`. Existing client behavior is preserved.

**Proxy refusals**: `400` (invalid request, bad signature, blocked destination), `429` (rate limit) or `502` (the site could not be reached: timeout, DNS failure, connection error, body over the cap), with `{ "source": "proxy", "reason": "<short category>" }`. Categories: `invalid request`, `bad signature`, `must use https`, `address not allowed`, `redirects not allowed`, `rate limited`, `site unreachable`. The wallet checks `source` and shows a proxy-specific message ("couldn't reach the site securely") instead of the "site rejected" messages. Errors never include internal addresses or resolver details.
