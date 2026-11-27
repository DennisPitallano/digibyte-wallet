# DigiPay × Python/Flask — mini-store sample

A complete, single-file mini-store wired to DigiPay. Buy a tee, pay in DGB,
order updates from a verified webhook.

What it shows:

- Creating a session via the official `digipay` SDK
- Redirecting the customer to DigiPay's hosted checkout
- Receiving a webhook with **raw-body HMAC verification** (the part most
  integrations get wrong)
- Polling the local order from the customer's browser until the webhook fires

## Setup

```bash
cd samples/flask-store
python -m venv .venv && source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -r requirements.txt
```

You'll need two values from your DigiPay dashboard:

1. **API key** — _API keys_ tab → create a key starting with `dgp_…`
2. **Webhook secret** — _Webhook_ tab → set the URL to your public
   `…/digipay-webhook` (use ngrok for local dev) and copy the secret it generates.

## Run

```bash
DIGIPAY_KEY=dgp_… \
DIGIPAY_SECRET=… \
PUBLIC_URL=http://localhost:3000 \
python app.py
```

Open <http://localhost:3000>, click _Buy_, complete the checkout, watch the
status flip to `paid` then `confirmed` once the chain catches up.

## Files

- `app.py` — the entire app, ~140 lines, comments explain each step
- `requirements.txt` — only two dependencies: `flask` and `digipay`

## Production-ready hardening (left out for clarity)

- Replace the in-memory `orders` dict with a real DB and per-customer auth
- Add idempotency on `POST /buy` so a double-click can't create two sessions
- Persist webhook deliveries (id + signature) so retries are de-duped
- Set `expires_in_seconds` on the session to match your fulfilment SLA
