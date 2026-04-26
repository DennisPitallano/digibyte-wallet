"""End-to-end DigiPay sample: a one-product mini-store.

Flow:
    GET  /                  → browse the product, click "Buy"
    POST /buy               → create a DigiPay session, redirect to its checkout_url
    POST /digipay-webhook   → verified webhook receiver; flips the in-memory order
    GET  /orders/<id>       → "your order" page that polls until the webhook fires

Deliberately uses an in-memory order store so the sample stays single-file.
In a real app you'd swap that for Postgres / SQLite / Redis with a unique-key
constraint per (session_id, attempt) so retries are de-duped.

Run with:
    DIGIPAY_KEY=dgp_…       (a DigiPay API key — Dashboard → API keys)
    DIGIPAY_SECRET=…        (the store webhook secret — Dashboard → Webhook)
    PUBLIC_URL=http://localhost:3000   (your URL the customer will see)
    pip install -r requirements.txt && python app.py
"""

from __future__ import annotations

import os

from flask import Flask, abort, jsonify, redirect, request

from digipay import DigiPayClient, DigiPayError, verify_webhook


def _required(name: str) -> str:
    raise SystemExit(f"Set {name} (see README.md)")


KEY = os.environ.get("DIGIPAY_KEY") or _required("DIGIPAY_KEY")
SECRET = os.environ.get("DIGIPAY_SECRET") or _required("DIGIPAY_SECRET")
PUBLIC_URL = os.environ.get("PUBLIC_URL", "http://localhost:3000").rstrip("/")
PORT = int(os.environ.get("PORT", "3000"))

digipay = DigiPayClient(api_key=KEY)

# One product. In a real store this would come from a DB.
PRODUCT = {"id": "sku-tee", "name": "DigiByte tee", "price": 12.5}  # 12.5 DGB

# Order state by id. status: 'pending' | 'paid' | 'confirmed' | 'expired' | 'underpaid'.
orders: dict[str, dict] = {}

app = Flask(__name__)


@app.get("/")
def home() -> str:
    return _layout(
        f"""
        <h1>{PRODUCT["name"]}</h1>
        <p class="price">{PRODUCT["price"]} DGB</p>
        <form method="POST" action="/buy">
            <button type="submit">Buy with DigiByte</button>
        </form>
        """
    )


@app.post("/buy")
def buy():
    try:
        session = digipay.sessions.create(
            amount=PRODUCT["price"],
            label=PRODUCT["name"],
            memo=f"sku={PRODUCT['id']}",
        )
    except DigiPayError as err:
        return f"DigiPay rejected the session: {err}", err.status

    # Track our local order keyed by session id — the webhook will look it up.
    orders[session["id"]] = {"sessionId": session["id"], "status": "pending"}
    # Redirect the customer to DigiPay's hosted checkout — they pay there.
    return redirect(session["checkoutUrl"], code=303)


@app.get("/orders/<sid>")
def order_page(sid: str) -> str:
    order = orders.get(sid)
    if not order:
        abort(404)
    txid_html = f"<p>Tx: <code>{order.get('txid','')}</code></p>" if order.get("txid") else ""
    return _layout(
        f"""
        <h1>Order {sid[-8:]}</h1>
        <p>Status: <b id="status">{order["status"]}</b></p>
        {txid_html}
        <script>
            // Poll the local order endpoint until terminal — saves wiring SignalR.
            const id = {sid!r};
            setInterval(async () => {{
                const r = await fetch('/orders/' + id + '.json');
                const j = await r.json();
                document.getElementById('status').textContent = j.status;
                if (['paid','confirmed','expired','underpaid'].includes(j.status)) location.reload();
            }}, 2000);
        </script>
        """
    )


@app.get("/orders/<sid>.json")
def order_json(sid: str):
    order = orders.get(sid)
    if not order:
        abort(404)
    return jsonify(order)


@app.post("/digipay-webhook")
def webhook():
    # CRITICAL: the HMAC covers the raw bytes as the server sent them. Use
    # request.get_data() — *not* request.json — to avoid whitespace
    # normalisation breaking verification.
    try:
        event = verify_webhook(
            raw_body=request.get_data(),
            signature=request.headers.get("X-DigiPay-Signature"),
            secret=SECRET,
        )
    except DigiPayError as err:
        # 401 on bad signature, 400 on malformed body. No detail to attackers.
        abort(err.status)

    sid = event["session"]["id"]
    order = orders.get(sid)
    if not order:
        # Webhook for an order we don't know — ack to stop retries, log for debugging.
        app.logger.warning("webhook for unknown session %s", sid)
        return "", 200

    # Map DigiPay event names to your local status. Unknown events ack 200 so
    # forward-compatible events don't trigger delivery failures.
    name = event["event"]
    if name in ("session.paid", "session.confirmed"):
        order["status"] = name.split(".")[1]
        order["txid"] = event["session"].get("paidTxid")
        app.logger.info("✅ %s: %s DGB for %s", order["status"], event["session"]["amount"], sid)
    elif name in ("session.expired", "session.underpaid"):
        order["status"] = name.split(".")[1]
        app.logger.info("❌ %s: %s", order["status"], sid)

    return "", 200


def _layout(body: str) -> str:
    return f"""<!doctype html>
<meta charset="utf-8">
<title>DigiByte tee — sample store</title>
<style>
    body {{ font-family: system-ui, sans-serif; max-width: 32rem; margin: 4rem auto; padding: 0 1rem; }}
    h1 {{ font-size: 1.5rem; }}
    .price {{ font-size: 2rem; font-weight: bold; color: #0062cc; }}
    button {{ padding: .75rem 1.25rem; background: #0062cc; color: white; border: 0;
              border-radius: .5rem; font-size: 1rem; font-weight: bold; cursor: pointer; }}
    code {{ background: #f3f4f6; padding: .125rem .375rem; border-radius: .25rem; font-size: .875rem; }}
</style>
{body}"""


if __name__ == "__main__":
    print(f"mini-store on {PUBLIC_URL}")
    print("Webhook URL to register on the DigiPay store:")
    print(f"  {PUBLIC_URL}/digipay-webhook")
    app.run(port=PORT)
