"""Official Python SDK for DigiPay.

    pip install digipay

Minimal usage:

    from digipay import DigiPay

    dp = DigiPay(api_key=os.environ["DIGIPAY_KEY"])
    session = dp.sessions.create(amount=5, label="Order #1234")
    print(session["checkoutUrl"])

See :class:`DigiPay` for the full resource surface, and :func:`verify_webhook`
for the HMAC verification helper.
"""

from .client import DEFAULT_BASE_URL, DigiPay
from .errors import DigiPayError
from .types import (
    Network,
    RegisterMerchantResponse,
    Session,
    SessionList,
    SessionStatus,
    Store,
    WebhookDelivery,
    WebhookEvent,
)
from .webhook import verify_webhook

__version__ = "0.1.0"

__all__ = [
    "DigiPay",
    "DigiPayError",
    "verify_webhook",
    "DEFAULT_BASE_URL",
    "Network",
    "RegisterMerchantResponse",
    "Session",
    "SessionList",
    "SessionStatus",
    "Store",
    "WebhookDelivery",
    "WebhookEvent",
]
