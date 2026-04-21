"""Single exception class for every SDK failure. HTTP status is preserved
on ``status`` so callers can branch on the number rather than string-
matching the message.

    * ``status == 0``      — network / DNS / TLS / timeout
    * ``status == 400``    — validation (``body['error']`` has details)
    * ``status == 401``    — missing or bad bearer token / webhook signature
    * ``status == 404``    — not found / not owned by this merchant
    * ``status == 429``    — rate-limited (sandbox endpoints only)
    * ``status >= 500``    — server-side; safe to retry with backoff
"""

from __future__ import annotations

from typing import Any


class DigiPayError(Exception):
    """Raised by every SDK call on failure; also by ``verify_webhook``."""

    def __init__(self, message: str, status: int = 0, body: Any = None) -> None:
        super().__init__(message)
        self.status = status
        self.body = body

    def __repr__(self) -> str:  # pragma: no cover — debug aid
        return f"DigiPayError(status={self.status}, message={super().__str__()!r})"
