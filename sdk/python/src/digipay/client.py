"""Main client class. Instantiate once per merchant API key:

    from digipay import DigiPay

    dp = DigiPay(api_key=os.environ["DIGIPAY_KEY"])
    session = dp.sessions.create(amount=5, label="Order #1234")
    print(session["checkoutUrl"])

For self-serve onboarding (no existing key), use the class-level
:meth:`DigiPay.register` — it returns the initial key, which is shown
once and must be stored server-side.
"""

from __future__ import annotations

import json
from typing import Any
from urllib import error as urlerror
from urllib import request as urlrequest

from ._http import SDK_VERSION, _HttpClient, build_qs
from .errors import DigiPayError
from .types import (
    Network, RegisterMerchantResponse, Session, SessionList, SessionStatus,
    Store, WebhookDelivery,
)

DEFAULT_BASE_URL = "https://api.pay.dgbwallet.app"


class DigiPay:
    """Top-level SDK entry point. One instance per API key."""

    def __init__(
        self,
        api_key: str,
        *,
        base_url: str = DEFAULT_BASE_URL,
        timeout: float = 15.0,
    ) -> None:
        if not api_key:
            raise ValueError("DigiPay: api_key is required")
        self._http = _HttpClient(api_key=api_key, base_url=base_url, timeout=timeout)
        self.sessions = _Sessions(self._http)
        self.stores = _Stores(self._http)

    # ---- Self-serve onboarding ---------------------------------------------

    @staticmethod
    def register(
        *,
        display_name: str,
        address_or_xpub: str,
        network: Network | None = None,
        webhook_url: str | None = None,
        base_url: str = DEFAULT_BASE_URL,
        timeout: float = 15.0,
    ) -> RegisterMerchantResponse:
        """Create a brand-new merchant + first store + initial API key in
        one unauthenticated call. Returns the initial API key — shown
        once, so store it in your secrets manager immediately.
        """
        url = f"{base_url.rstrip('/')}/v1/pay/merchants"
        body: dict[str, Any] = {
            "displayName": display_name,
            "addressOrXpub": address_or_xpub,
        }
        if network is not None:
            body["network"] = network
        if webhook_url is not None:
            body["webhookUrl"] = webhook_url

        req = urlrequest.Request(
            url,
            data=json.dumps(body).encode("utf-8"),
            method="POST",
            headers={
                "Content-Type": "application/json",
                "User-Agent": f"digipay-python/{SDK_VERSION}",
            },
        )
        try:
            with urlrequest.urlopen(req, timeout=timeout) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urlerror.HTTPError as err:
            payload_raw = err.read()
            parsed: Any = None
            try:
                parsed = json.loads(payload_raw.decode("utf-8")) if payload_raw else None
            except Exception:  # noqa: BLE001
                parsed = payload_raw.decode("utf-8", errors="replace") if payload_raw else None
            msg = (parsed or {}).get("error") if isinstance(parsed, dict) else None
            raise DigiPayError(msg or f"Registration failed: HTTP {err.code}", err.code, parsed) from err
        except urlerror.URLError as err:
            raise DigiPayError(f"Network error: {err.reason}", 0, err) from err


# ---- Resources -------------------------------------------------------------


class _Sessions:
    def __init__(self, http: _HttpClient) -> None:
        self._http = http

    def create(
        self,
        *,
        amount: float,
        store_id: str | None = None,
        label: str | None = None,
        memo: str | None = None,
        fiat_currency: str | None = None,
        fiat_amount: float | None = None,
        expiry_minutes: int | None = None,
        idempotency_key: str | None = None,
    ) -> Session:
        """Create a new payment session. ``amount`` is DGB (decimal).

        Pass ``idempotency_key`` to make the call safely retryable — the
        server stores key→sessionId for 24h and returns the original
        session on replay (Stripe-shaped). Useful when a network blip
        could cause your code to retry past an already-created session.
        """
        body: dict[str, Any] = {"amount": amount}
        if store_id is not None:
            body["storeId"] = store_id
        if label is not None:
            body["label"] = label
        if memo is not None:
            body["memo"] = memo
        if fiat_currency is not None:
            body["fiatCurrency"] = fiat_currency
        if fiat_amount is not None:
            body["fiatAmount"] = fiat_amount
        if expiry_minutes is not None:
            body["expiryMinutes"] = expiry_minutes
        return self._http.request_json(
            "POST", "/v1/pay/sessions", body,
            idempotency_key=idempotency_key,
        )

    def get(self, session_id: str) -> Session:
        """Look up a single session. Public read — no auth strictly needed
        but we pass it anyway since we already have it."""
        result = self._http.request_json("GET", f"/v1/pay/sessions/{session_id}")
        # The public endpoint wraps the session in { session, merchantName }.
        return result.get("session", result) if isinstance(result, dict) else result

    def list(
        self,
        *,
        store_id: str | None = None,
        status: SessionStatus | None = None,
        take: int = 25,
        skip: int = 0,
    ) -> SessionList:
        qs = build_qs(storeId=store_id, status=status, take=take, skip=skip)
        return self._http.request_json("GET", f"/v1/pay/sessions{qs}")

    def export_csv(
        self,
        *,
        store_id: str | None = None,
        status: SessionStatus | None = None,
        take: int = 10_000,
    ) -> str:
        """CSV bookkeeping export — returns the raw text. Row cap defaults
        to 10 000 (server-side max). Add a date/status filter if you're
        near the limit."""
        qs = build_qs(format="csv", storeId=store_id, status=status, take=take)
        return self._http.request_bytes("GET", f"/v1/pay/sessions{qs}").decode("utf-8")


class _Stores:
    def __init__(self, http: _HttpClient) -> None:
        self._http = http

    def list(self) -> list[Store]:
        return self._http.request_json("GET", "/v1/pay/stores")

    def get(self, store_id: str) -> Store:
        return self._http.request_json("GET", f"/v1/pay/stores/{store_id}")

    def create(self, *, name: str, network: Network = "mainnet") -> Store:
        return self._http.request_json(
            "POST",
            "/v1/pay/stores",
            {"name": name, "network": network},
        )

    def update(
        self,
        store_id: str,
        *,
        name: str | None = None,
        network: Network | None = None,
        address_or_xpub: str | None = None,
        webhook_url: str | None = None,
        default_session_expiry_minutes: int | None = None,
    ) -> Store:
        patch: dict[str, Any] = {}
        if name is not None:
            patch["name"] = name
        if network is not None:
            patch["network"] = network
        if address_or_xpub is not None:
            patch["addressOrXpub"] = address_or_xpub
        if webhook_url is not None:
            patch["webhookUrl"] = webhook_url
        if default_session_expiry_minutes is not None:
            patch["defaultSessionExpiryMinutes"] = default_session_expiry_minutes
        return self._http.request_json("PATCH", f"/v1/pay/stores/{store_id}", patch)

    def delete(self, store_id: str) -> dict[str, bool]:
        return self._http.request_json("DELETE", f"/v1/pay/stores/{store_id}")

    def send_test_webhook(self, store_id: str) -> dict[str, Any]:
        return self._http.request_json("POST", f"/v1/pay/stores/{store_id}/webhook/test")

    def list_deliveries(
        self,
        store_id: str,
        *,
        take: int = 25,
        session_id: str | None = None,
    ) -> list[WebhookDelivery]:
        qs = build_qs(take=take, sessionId=session_id)
        return self._http.request_json(
            "GET",
            f"/v1/pay/stores/{store_id}/webhook-deliveries{qs}",
        )

    def replay_delivery(self, store_id: str, delivery_id: str) -> WebhookDelivery:
        return self._http.request_json(
            "POST",
            f"/v1/pay/stores/{store_id}/webhook-deliveries/{delivery_id}/replay",
        )

    def export_deliveries_csv(
        self,
        store_id: str,
        *,
        take: int = 10_000,
        session_id: str | None = None,
    ) -> str:
        qs = build_qs(format="csv", take=take, sessionId=session_id)
        return self._http.request_bytes(
            "GET",
            f"/v1/pay/stores/{store_id}/webhook-deliveries{qs}",
        ).decode("utf-8")
