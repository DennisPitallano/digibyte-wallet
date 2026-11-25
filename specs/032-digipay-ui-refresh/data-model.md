# Data Model: DigiPay UI Refresh

This feature adds no server-side data. Everything here is browser-side preference state or a fixed mapping in the UI. The Pay API, its database and its session model are unchanged (FR-036).

## 1. Theme preference (website, dashboard, shopper pages)

| Field | Values | Where it lives | Notes |
|---|---|---|---|
| `mode` | `light` \| `dark` \| `system` | `localStorage["dp-theme"]` | Same key as today, so a saved `light` or `dark` is kept. Missing or invalid means `system` (clarification 1). |

**Resolution**, highest wins (implemented in `wwwroot/theme.js`, mirrored by a C# helper for tests):

1. **Shop override.** On `/pay/{id}`, `/pay/{id}/receipt` and `/pay/button`, a query parameter `theme` equal to `light` or `dark` (case-insensitive). It applies to this page view only and is never saved. Any other value is ignored. See [contracts/checkout-theme.md](contracts/checkout-theme.md).
2. **POS preference.** On `/pos` only, see section 2.
3. **Saved `mode`.**
4. **Device setting.** Used when the result so far is `system`: `prefers-color-scheme`.

**Output**: the `dark` class on `<html>` (present or absent), and `<meta name="theme-color">` set to `#002352` (dark) or `#f8fafc` (light), matching the wallet.

**Transitions**: The ThemePicker writes `mode`. While `mode = system`, a change to the device setting re-applies the theme live. A shop override never writes to storage.

## 2. POS theme preference

| Field | Values | Where it lives | Notes |
|---|---|---|---|
| `posTheme` | `light` \| `dark` \| `system` | `localStorage["digipay.pos.theme:v1"]` | Missing means `dark` (clarification 3, second pass). Already-paired tablets have no key, so they stay dark. |

- **Who can change it**: written only from POS settings after the existing manager PIN check passes (FR-013).
- **Pairing**: unpairing clears the device key. It keeps `posTheme`, because the tablet is still the merchant's.

## 3. Dashboard section

A fixed list. It drives the rail, the bottom menu, the More sheet, routes, page titles and the empty states.

| Key | Route | Label | In bottom menu | Empty state (Baku pose) |
|---|---|---|---|---|
| `overview` | `/merchant`, `/merchant/overview` | Overview | Yes (1) | Setup checklist when the store can't accept payments (`support`) |
| `sessions` | `/merchant/sessions` | Sessions | Yes (2) | "No payments yet": Create your first payment (`greeting`) |
| `new` | `/merchant/new` | New payment | Yes, raised (3) | Not applicable. Explains why when the store can't accept payments (FR-023) |
| `links` | `/merchant/links` | Payment links | Yes (4) | "No payment links yet": Create link (`idea`) |
| `analytics` | `/merchant/analytics` | Analytics | More | "No activity in this window yet" (`thinking`) |
| `donation` | `/merchant/donation` | Donation button | More | Not applicable (always shows the snippet builder) |
| `webhook` | `/merchant/webhook` | Webhook | More | "No webhook set": Add endpoint (`support`) |
| `stores` | `/merchant/stores` | Stores | More | Not applicable (always has at least one store) |
| `api-keys` | `/merchant/api-keys` | API keys | More | "No API keys": Create key (`idea`) |
| `audit` | `/merchant/audit` | Audit log | More | "Nothing recorded yet" (`thinking`) |
| `pos` | `/merchant/pos` | POS mode | More | "No paired devices": Pair a tablet (`idea`) |
| `settings` | `/merchant/settings` | Settings | More | Not applicable |
| *(More)* | Not a route (opens a sheet) | More | Yes (5) | Not applicable |

- **Detail route**: `/merchant/sessions/{SessionId}` stays as it is, with a back button to `/merchant/sessions` (keeping the query from the list the merchant came from).
- **Pose files**: `support`, `greeting`, `idea` and `thinking` are existing wallet poses in `src/DigiByte.Web/wwwroot/images/mascot/poses/` (`.webp`). Only these four are copied to `src/DigiByte.UI/wwwroot/images/mascot/poses/`. Baku never appears on `/pay/*` or `/pos` (clarification 2).

## 4. Dashboard view state (scoped `MerchantState`, per circuit)

| Field | Source | Notes |
|---|---|---|
| `Token` | `localStorage["digipay-token"]` | Unchanged auth. Missing or rejected sends the merchant to `/merchant/signin?return=<current path+query>`. |
| `Merchant` | Pay API | Fetched once per circuit. |
| `ActiveStoreId` | `?store=` query, else the last used store, else the first store | The switcher updates the query. |
| Cached lists (sessions page, keys, links, devices) | Pay API | Kept while moving between sections. Refreshed on explicit refresh, after a change, or when older than 60 s. |
| Sessions filters | Query string (`status`, `q`, `page`) | Restored by back and forward. |

## 5. Checkout state (display mapping)

The session `Status` values come from the Pay API unchanged. Each one maps to one display state. The client adds two states of its own.

| Status (API) | Badge text | Tone | Icon (shared set) | Announcement (`aria-live`) | Next step shown |
|---|---|---|---|---|---|
| `pending` | Waiting for payment | neutral | clock | none (initial) | Pay with QR, Open in wallet, or copy the address and amount |
| `seen` | Payment detected | info (amber) | radar | "Payment detected. Waiting for the network." | Keep this page open |
| `paid` | Payment received | success | check | "Payment received." | View receipt |
| `confirmed` | Confirmed | success | check-circle | "Payment confirmed." | View receipt; Back to shop when a return URL exists |
| `underpaid` | Underpaid | danger | alert | "Too little was received." | Contact the merchant, with the received and expected amounts |
| `expired` | Price expired | neutral | timer-off | "This price has expired." | Back to shop when available. No QR code or address shown |
| `refunded` | Refunded | info (indigo) | undo | "This payment was refunded." | View receipt |
| `voided` | Cancelled by the merchant | neutral | ban | "The merchant cancelled this payment." | Back to shop when available |
| *(client)* reconnecting | Reconnecting… | neutral | refresh | "Reconnecting. Your payment is still being watched." | Keep showing the last known state (FR-011) |
| *(client)* load failed | Not available | danger | alert | "This payment page could not be loaded." | Retry, and Back to shop when known. Never shows exception text (FR-010) |

- **Live states**: `pending`, `seen` and `paid` keep the live connection open. The other API states are final, matching today's logic in `Checkout.razor:256`.
- **Price drift**: the existing drift banner is not a status. It stays a banner in the shared style, with warning tone and text.
- **Validation**: Every API status must map to a row. An unknown status renders as `pending`'s badge with no announcement, and is logged to the console only. A unit test enumerates the table.

## 6. Notice (toast)

| Field | Values |
|---|---|
| `Tone` | `success` \| `info` \| `warning` \| `error` |
| `Text` | Plain language. Never exception text. |
| `Action` | Optional label and callback (for example "Retry") |
| `Duration` | 3 s by default. `error` stays until dismissed or acted on. |

- **Announcements**: `success` and `info` use `role=status`. `warning` and `error` use `role=alert`.
- **No duplicates**: identical text inside 2 s is merged.
- **Checkout priority**: a status announcement (section 5) takes priority. A copy notice must not cover the badge.
