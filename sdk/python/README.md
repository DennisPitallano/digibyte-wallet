# digipay

Official Python SDK for **[DigiPay](https://pay.dgbwallet.app)** — accept DigiByte payments on your site without holding any funds.

- **Non-custodial.** Payments land directly in your wallet (single address or BIP84 xpub).
- **Zero runtime dependencies.** Standard library only (`urllib`, `hmac`, `hashlib`, `json`).
- **Fully typed.** `py.typed`, `TypedDict` DTOs, `Literal` status unions.
- **Python 3.10+.**

```bash
pip install digipay
```

## Quickstart

```python
import os
from digipay import DigiPay

dp = DigiPay(api_key=os.environ["DIGIPAY_KEY"])

session = dp.sessions.create(amount=5, label="Order #1234")
print(session["checkoutUrl"])  # → https://pay.dgbwallet.app/pay/ses_…
```

## Self-serve registration

If you don't have an API key yet, register a brand-new merchant + first store + initial key in a single call:

```python
from digipay import DigiPay

merchant = DigiPay.register(
    display_name="My Shop",
    address_or_xpub="dgb1q…",  # or a BIP84 xpub
    webhook_url="https://my-shop.example/digipay-webhook",
)

print(merchant["apiKey"])        # dgp_… (shown once)
print(merchant["webhookSecret"]) # store this for verify_webhook
```

## Webhook verification

DigiPay POSTs signed JSON to your `webhookUrl` on every state change. The signature is HMAC-SHA256 of the raw body, hex-encoded, in the `X-DigiPay-Signature` header (prefixed `sha256=`).

```python
import os
from flask import Flask, request, abort
from digipay import verify_webhook, DigiPayError

app = Flask(__name__)

@app.post("/digipay-webhook")
def webhook():
    try:
        event = verify_webhook(
            raw_body=request.get_data(),                       # raw bytes
            signature=request.headers.get("X-DigiPay-Signature"),
            secret=os.environ["DIGIPAY_SECRET"],
        )
    except DigiPayError as err:
        abort(err.status)

    if event["event"] == "session.paid":
        # event["session"]["id"], .amount, .paidTxid, etc.
        ...
    return "", 200
```

**Critical:** verify the signature against the **raw bytes** before parsing JSON. Re-serialising (or letting a framework parse for you) breaks the HMAC.

## Resources

### Sessions

```python
dp.sessions.create(amount=5, label="Order #1", memo="…", fiat_currency="USD", fiat_amount=2.50)
dp.sessions.get("ses_abc")
dp.sessions.list(status="paid", take=50)
dp.sessions.export_csv(status="paid")   # returns CSV text
```

### Stores

```python
dp.stores.list()
dp.stores.get("sto_abc")
dp.stores.create(name="Side hustle", network="mainnet")
dp.stores.update("sto_abc", webhook_url="…", default_session_expiry_minutes=30)
dp.stores.delete("sto_abc")

# Webhook tooling
dp.stores.send_test_webhook("sto_abc")
dp.stores.list_deliveries("sto_abc", take=100)
dp.stores.replay_delivery("sto_abc", "wdel_…")
dp.stores.export_deliveries_csv("sto_abc")
```

## Errors

Every failure raises `DigiPayError` with the HTTP status preserved:

```python
from digipay import DigiPay, DigiPayError

try:
    dp.sessions.create(amount=0)
except DigiPayError as err:
    print(err.status)  # 400
    print(err.body)    # {"error": "amount (DGB) must be > 0"}
```

| `err.status` | Meaning |
|---|---|
| `0` | Network / DNS / TLS / timeout |
| `400` | Validation failure — see `err.body["error"]` |
| `401` | Missing or invalid API key |
| `404` | Resource not found, or not owned by this merchant |
| `429` | Rate-limited (sandbox endpoints only) |
| `>= 500` | Server-side; safe to retry with backoff |

## Configuration

```python
DigiPay(
    api_key="dgp_…",
    base_url="https://api.pay.dgbwallet.app",  # default
    timeout=15.0,                               # seconds, default
)
```

For staging or self-hosted, pass an alternate `base_url`.

## License

MIT — see the repo root [`LICENSE`](../../LICENSE).
