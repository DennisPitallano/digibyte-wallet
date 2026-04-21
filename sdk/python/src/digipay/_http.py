"""Internal HTTP transport. Stdlib-only — uses urllib so the SDK has
zero runtime dependencies.

A more modern client (``httpx``) would give us async + connection
pooling out of the box, but every dependency is a supply-chain
surface. Users who want async can wrap ``DigiPay`` in
``asyncio.to_thread``.
"""

from __future__ import annotations

import json
from typing import Any
from urllib import error as urlerror
from urllib import request as urlrequest
from urllib.parse import urlencode

from .errors import DigiPayError

SDK_VERSION = "0.1.0"


class _HttpClient:
    def __init__(self, *, api_key: str, base_url: str, timeout: float) -> None:
        self._api_key = api_key
        self._base_url = base_url.rstrip("/")
        self._timeout = timeout
        self._user_agent = f"digipay-python/{SDK_VERSION}"

    def request_json(
        self,
        method: str,
        path: str,
        body: Any = None,
    ) -> Any:
        raw = self.request_bytes(method, path, body)
        if not raw:
            return None
        try:
            return json.loads(raw.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError) as err:
            raise DigiPayError(f"Non-JSON response from {path}", body=raw) from err

    def request_bytes(
        self,
        method: str,
        path: str,
        body: Any = None,
    ) -> bytes:
        url = f"{self._base_url}{path}"
        data: bytes | None = None
        headers = {
            "Authorization": f"Bearer {self._api_key}",
            "User-Agent": self._user_agent,
        }
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"

        req = urlrequest.Request(url, data=data, method=method, headers=headers)
        try:
            with urlrequest.urlopen(req, timeout=self._timeout) as resp:
                return resp.read()
        except urlerror.HTTPError as err:
            # Non-2xx → surface the server's {"error": "..."} if we can.
            payload_raw = err.read()
            parsed: Any = None
            try:
                parsed = json.loads(payload_raw.decode("utf-8")) if payload_raw else None
            except Exception:  # noqa: BLE001 — fall back to raw text
                parsed = payload_raw.decode("utf-8", errors="replace") if payload_raw else None
            msg = (parsed or {}).get("error") if isinstance(parsed, dict) else None
            raise DigiPayError(
                msg or f"{method} {path} failed with HTTP {err.code}",
                status=err.code,
                body=parsed,
            ) from err
        except urlerror.URLError as err:
            # DNS / connection refused / TLS / timeout.
            raise DigiPayError(
                f"Network error contacting {path}: {err.reason}",
                status=0,
                body=err,
            ) from err


def build_qs(**params: Any) -> str:
    """Build a URL query string, dropping ``None`` values so defaults fall
    through to the server-side defaults."""
    filtered = {k: v for k, v in params.items() if v is not None}
    if not filtered:
        return ""
    return "?" + urlencode(filtered, doseq=False)
