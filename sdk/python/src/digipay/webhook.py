"""HMAC signature verification for incoming DigiPay webhooks.

Framework-agnostic: hand it the raw bytes + the header + the secret,
get back a parsed event dict, or a :class:`DigiPayError` on mismatch.
"""

from __future__ import annotations

import hashlib
import hmac
import json

from .errors import DigiPayError
from .types import WebhookEvent


def verify_webhook(
    raw_body: bytes | str,
    signature: str | None,
    secret: str,
) -> WebhookEvent:
    """Verify the HMAC signature on an incoming DigiPay webhook and
    return the parsed event.

    Parameters
    ----------
    raw_body:
        The un-parsed bytes of the HTTP body, exactly as received. Do
        **not** re-serialize the JSON before passing it in — the HMAC
        covers the bytes, so any reserialization breaks the check. In
        Flask: ``request.get_data()``. In FastAPI: ``await request.body()``.
    signature:
        Value of the ``X-DigiPay-Signature`` header. Format is
        ``sha256=<hex>``; the prefix is tolerated if stripped.
    secret:
        The store's webhook secret. Treat like a password.

    Raises
    ------
    DigiPayError
        With ``status=401`` on missing / mismatched signature,
        ``status=400`` on malformed JSON after a valid signature.

    Example (Flask)
    ---------------
        from flask import Flask, request, abort
        from digipay import verify_webhook, DigiPayError

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
                abort(err.status)
            # event["event"] == "session.paid" | "session.confirmed" | ...
            return "", 200
    """
    if not signature:
        raise DigiPayError("Missing X-DigiPay-Signature header", 401)
    if not secret:
        raise DigiPayError("Webhook secret is required", 400)

    # Header format is "sha256=<hex>"; tolerate a stripped prefix just
    # in case a proxy rewrote the header (uncommon but harmless here).
    provided = signature[len("sha256="):] if signature.startswith("sha256=") else signature

    body_bytes = raw_body.encode("utf-8") if isinstance(raw_body, str) else raw_body
    expected = hmac.new(secret.encode("utf-8"), body_bytes, hashlib.sha256).hexdigest()

    # hmac.compare_digest is constant-time and handles unequal lengths
    # safely — preferred over == for any secret comparison.
    if not hmac.compare_digest(expected, provided):
        raise DigiPayError("Webhook signature mismatch", 401)

    try:
        return json.loads(body_bytes.decode("utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError) as err:
        raise DigiPayError("Webhook body is not valid JSON", 400, err) from err
