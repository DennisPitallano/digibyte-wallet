"""Create a checkout session.

Run with:
    DIGIPAY_KEY=dgp_… python examples/create_session.py
"""

from __future__ import annotations

import os

from digipay import DigiPay

dp = DigiPay(api_key=os.environ["DIGIPAY_KEY"])

session = dp.sessions.create(
    amount=5,
    label="Order #1234",
    memo="Customer: alice@example.com",
)

print("Session ID:  ", session["id"])
print("Amount:      ", session["amount"], "DGB")
print("Address:     ", session["address"])
print("Expires:     ", session["expiresAt"])
print("BIP21 URI:   ", session["uri"])
print("Hosted page: ", session["checkoutUrl"])
