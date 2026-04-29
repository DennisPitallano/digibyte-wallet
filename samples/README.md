# DigiPay sample apps

End-to-end mini-store integrations for DigiPay — pick your stack and copy.
Each app is **single-file**, runs in seconds, and walks through the full
checkout lifecycle: create a session, redirect to hosted checkout, verify the
webhook, fulfil the order.

| Stack | Folder | Lines | SDK |
|---|---|---|---|
| Node + Express | [`express-store/`](express-store/) | ~120 | [`@dgbwallet/digipay`](../sdk/node) |
| Python + Flask | [`flask-store/`](flask-store/) | ~140 | [`digipay`](../sdk/python) |
| ASP.NET Core (Minimal API) | [`dotnet-store/`](dotnet-store/) | ~150 | [`DigiPay`](../sdk/dotnet) |
| WooCommerce plugin (PHP) | [`woocommerce-plugin/`](woocommerce-plugin/) | ~600 | none — direct REST + HMAC verify |

Each folder has its own README with run instructions. They all expose the same
three routes:

- `GET /` — product page with a _Buy with DigiByte_ button
- `POST /buy` — creates a DigiPay session, redirects to the hosted checkout
- `POST /digipay-webhook` — verified webhook receiver that flips local order state

## What you'll need from the dashboard

1. An **API key** (`dgp_…`) — _Dashboard → API keys → Create_
2. A **webhook secret** — _Dashboard → Webhook_, point the URL at your public
   `…/digipay-webhook` (use [ngrok](https://ngrok.com) for local dev)

## Why these particular samples

These three together cover ~80% of the new integrations we see: a JS-fronted
e-commerce site (Express / Next.js API routes), a backend in Python (Flask /
Django webhook listeners), or a .NET shop (Minimal API / MVC). The HMAC
raw-body verification step is the same one every integration trips over — all
three samples deliberately demonstrate it correctly so you can lift the
pattern.

## Beyond the basics

Once you've got the basic flow running, useful next steps:

- Pass `expiresInSeconds` to match your fulfilment SLA
- Use `fiatCurrency` + `fiatAmount` to price in USD/EUR/GBP and let DigiPay
  pin the DGB amount at checkout time
- Replace the in-memory order store with a real DB and add idempotency on the
  buy route
- Subscribe to `session.confirmed` (not just `paid`) before shipping high-value
  orders — confirmed = 6+ on-chain confirmations
