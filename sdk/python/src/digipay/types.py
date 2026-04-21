"""Shape of the DTOs returned by the DigiPay REST API.

We use ``TypedDict`` rather than ``@dataclass`` so the SDK doesn't copy /
validate fields on the happy path — users get the parsed JSON straight
through with full type-checker support, and opting in to stricter
validation is a separate concern they can bolt on (e.g. pydantic).
"""

from __future__ import annotations

from typing import Literal, NotRequired, TypedDict

SessionStatus = Literal[
    "pending",     # waiting for a payment to land in mempool
    "seen",        # payment detected, not yet confirmed
    "paid",        # 1 confirmation — good enough for most flows
    "confirmed",   # 6+ confirmations, treat as irreversible
    "expired",     # window closed with nothing (or not enough) received
    "underpaid",   # received less than the expected amount
]

Network = Literal["mainnet", "testnet", "regtest"]


class Session(TypedDict):
    id: str
    storeId: str
    merchantId: str
    status: SessionStatus
    address: str
    amountSatoshis: int
    amount: float
    fiatCurrency: NotRequired[str | None]
    fiatAmount: NotRequired[float | None]
    label: NotRequired[str | None]
    memo: NotRequired[str | None]
    receivedSatoshis: int
    confirmations: int
    paidTxid: NotRequired[str | None]
    createdAt: str
    expiresAt: str
    seenAt: NotRequired[str | None]
    paidAt: NotRequired[str | None]
    uri: str
    checkoutUrl: str


class Store(TypedDict):
    id: str
    merchantId: str
    name: str
    network: Network
    hasReceive: bool
    mode: Literal["address", "xpub", "none"]
    receiveAddress: NotRequired[str | None]
    webhookUrl: NotRequired[str | None]
    defaultSessionExpiryMinutes: int
    createdAt: str


class WebhookDelivery(TypedDict):
    id: str
    storeId: str
    sessionId: NotRequired[str | None]
    eventName: str
    url: str
    attempt: int
    statusCode: NotRequired[int | None]
    errorMessage: NotRequired[str | None]
    durationMs: NotRequired[int | None]
    responseSnippet: NotRequired[str | None]
    createdAt: str
    deliveredAt: NotRequired[str | None]
    nextRetryAt: NotRequired[str | None]
    success: bool


class RegisterMerchantResponse(TypedDict):
    id: str
    displayName: str
    storeId: str
    network: str
    mode: Literal["address", "xpub"]
    apiKey: str
    webhookSecret: NotRequired[str | None]


class SessionList(TypedDict):
    total: int
    take: int
    skip: int
    sessions: list[Session]


class WebhookEvent(TypedDict):
    event: str
    timestamp: str
    session: Session
