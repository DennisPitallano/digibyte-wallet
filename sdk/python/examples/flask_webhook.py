"""Minimal Flask webhook receiver with signature verification.

Run with:
    DIGIPAY_SECRET=… python examples/flask_webhook.py
"""

from __future__ import annotations

import os

from flask import Flask, abort, request

from digipay import DigiPayError, verify_webhook

app = Flask(__name__)


@app.post("/digipay-webhook")
def webhook():
    try:
        event = verify_webhook(
            raw_body=request.get_data(),
            signature=request.headers.get("X-DigiPay-Signature"),
            secret=os.environ["DIGIPAY_SECRET"],
        )
    except DigiPayError as err:
        # Signature mismatch → 401, malformed body → 400. Don't leak why.
        abort(err.status)

    match event["event"]:
        case "session.paid":
            # Mark order as paid, fulfil, send receipt...
            print(f"💰 {event['session']['amount']} DGB → {event['session']['label']}")
        case "session.confirmed":
            print(f"✅ confirmed: {event['session']['id']}")
        case "session.expired" | "session.underpaid":
            print(f"❌ {event['event']}: {event['session']['id']}")
        case _:
            # Unknown event types: ack 200 and ignore so forward-compatible
            # events don't trigger delivery failures.
            pass

    return "", 200


if __name__ == "__main__":
    app.run(port=3000)
