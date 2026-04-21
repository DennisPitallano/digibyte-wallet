"""Webhook verification smoke tests. Stdlib unittest — no pytest
dependency. Run with ``python -m unittest`` from sdk/python/."""

from __future__ import annotations

import hashlib
import hmac
import json
import sys
import unittest
from pathlib import Path

# Make ``digipay`` importable without installing the package first.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from digipay import DigiPayError, verify_webhook

SECRET = "test-secret-123"
PAYLOAD = json.dumps(
    {
        "event": "session.paid",
        "timestamp": "2026-04-22T00:00:00Z",
        "session": {"id": "ses_abc", "amount": 5},
    },
    separators=(",", ":"),
)


def _sign(body: str, secret: str = SECRET) -> str:
    return "sha256=" + hmac.new(secret.encode(), body.encode(), hashlib.sha256).hexdigest()


class VerifyWebhookTests(unittest.TestCase):
    def test_accepts_correctly_signed_payload(self) -> None:
        event = verify_webhook(PAYLOAD, _sign(PAYLOAD), SECRET)
        self.assertEqual(event["event"], "session.paid")
        self.assertEqual(event["session"]["id"], "ses_abc")

    def test_accepts_bytes_body(self) -> None:
        event = verify_webhook(PAYLOAD.encode("utf-8"), _sign(PAYLOAD), SECRET)
        self.assertEqual(event["event"], "session.paid")

    def test_tolerates_missing_sha256_prefix(self) -> None:
        sig = _sign(PAYLOAD).removeprefix("sha256=")
        event = verify_webhook(PAYLOAD, sig, SECRET)
        self.assertEqual(event["event"], "session.paid")

    def test_rejects_wrong_signature(self) -> None:
        bad = _sign(PAYLOAD, "not-the-real-secret")
        with self.assertRaises(DigiPayError) as ctx:
            verify_webhook(PAYLOAD, bad, SECRET)
        self.assertEqual(ctx.exception.status, 401)

    def test_rejects_missing_header(self) -> None:
        with self.assertRaises(DigiPayError) as ctx:
            verify_webhook(PAYLOAD, None, SECRET)
        self.assertEqual(ctx.exception.status, 401)

    def test_rejects_tampered_body(self) -> None:
        tampered = PAYLOAD.replace('"amount":5', '"amount":5000')
        with self.assertRaises(DigiPayError) as ctx:
            verify_webhook(tampered, _sign(PAYLOAD), SECRET)
        self.assertEqual(ctx.exception.status, 401)

    def test_rejects_junk_json_after_valid_signature(self) -> None:
        bad = "{not json"
        with self.assertRaises(DigiPayError) as ctx:
            verify_webhook(bad, _sign(bad), SECRET)
        self.assertEqual(ctx.exception.status, 400)


if __name__ == "__main__":
    unittest.main()
