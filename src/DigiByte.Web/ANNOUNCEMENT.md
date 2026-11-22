# DigiByte Wallet v0.8.0 - A new menu, one Scan for everything, and a wallet you can snap shut

> Release date: November 2026
> Live at: https://dgbwallet.app
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.8.0

## Short post (~280 chars)

🟦 DigiByte Wallet v0.8.0: a new menu with one Scan button for addresses, payment requests, Digi-ID and multisig codes. Sends end with a slide, new wallets show their address on Home, and the balance card is a wallet you can snap shut to lock.

→ https://dgbwallet.app

---

## Medium post

**Easier to find your way, harder to make a mistake**

This release changes how the wallet is laid out and how it looks.

- **A new menu.** Wallet, Activity, Scan, Request and More. Digi-ID and Settings moved into More, Pay is now called Request, and a one-time tip tells returning users where things went.
- **One Scan for everything.** The round button in the middle reads a DigiByte address or payment request, a Digi-ID login, a multisig proposal, a redeem script or a room invite, and opens the right page. A code it does not recognise opens nothing.
- **Slide to send.** The last step of a send, a DigiDollar send, a consolidation, a Digi-ID fund sweep and a multisig broadcast is a slide, so a stray tap cannot move coins.
- **A better first day.** A new wallet shows its address and QR code on Home straight away, the Receive page notices an incoming payment by itself, and the first payment is marked once.
- **A wallet that looks like one.** The balance card is drawn as a wallet in your wallet's colour, with a snap that locks the app when you press it.

Also new: optional sounds for payments received and sent, an easier word-by-word recovery screen, text fields that no longer make iPhones zoom, more readable text in the light theme, and one consistent style for buttons, cards, tabs and headings.

Details: [`CHANGELOG.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/CHANGELOG.md)

---

# DigiByte Wallet v0.7.0 - Faster balances, pending payments, and multisig signing online

> Release date: November 2026
> Live at: https://dgbwallet.app
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.7.0

## Short post (~280 chars)

🟦 DigiByte Wallet v0.7.0: balance and history load faster with about half the network requests, pending payments show under your balance, and wallet pages open with your figures at once. Multisig co-signers can now sign together online.

→ https://dgbwallet.app

---

## Medium post

**Faster, clearer, and multisig together**

This release does three things.

- **Faster balance and history.** When nothing new has happened, a refresh now asks the explorer one question per address instead of three. In a live test a refresh took 0.3 seconds instead of 1.2, so "Unable to load" from rate limits is rarer, especially on mobile. Activity, Receive and Send now open with your last known figures and update quietly, and an address with more than 25 transactions finally shows its whole history.
- **Pending payments.** A payment on its way to you, or a send that hasn't confirmed, shows on its own line under your balance. The main figure is your confirmed money, and Send won't offer coins a pending send already spent.
- **Multisig, together.** Co-signers can sign a payment together online in a short-lived room, or pass a proposal by QR code, paste or file. Every wallet checks the payment itself and signs on its own device; the room never sees a key.

Also new: an Install app entry in Settings, 14 more Latin American currencies, and fixes for a stuck splash screen on Android and for balances that could read too low when some lookups failed.

Details: [`CHANGELOG.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/CHANGELOG.md)

---

# DigiByte Wallet v0.6.0 - Multisig that follows your wallet, and stronger PIN protection

> Release date: November 2026
> Live at: https://dgbwallet.app
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.6.0

## Short post (~280 chars)

🟦 DigiByte Wallet v0.6.0: multisigs now stay with the wallet that made them, can be renamed, show every co-signer's full key and send like normal DGB, with a QR code for the redeem script. Your PIN is also protected with Argon2id. Updates happen on next unlock.

→ https://dgbwallet.app

---

## Medium post

**Multisig, rebuilt around your wallet**

This release does three things.

- **Multisig that makes sense.** A multisig now shows only under the wallet that created it, can be renamed, and each co-signer's full public key can be checked, copied or shown as a QR code. Sending looks like Send DGB (paste, scan, Max, a fee preview and a review step), there is an Activity list for what came in and went out, and pending payments show who has signed. The redeem script can be saved or scanned as a QR code.
- **Stronger PIN protection (first published in this release).** Your PIN or passphrase now becomes an encryption key with Argon2id, which needs a lot of memory for every guess. Existing wallets upgrade the next time you unlock, with the same PIN, and the older copy is kept for 7 days. You can also choose a passphrase instead of a 6-digit PIN.
- **A clearer wallet.** Your addresses on Receive (used, unused, named), your own address on each activity row with an address filter, the oracle price on the DigiDollar card, and a public Network status page that says which data sources the wallet is using.

Note: version 0.5.0 was never published as a tag; everything in it ships here.

Details: [`CHANGELOG.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/CHANGELOG.md)

---

# DigiByte Wallet v0.1.4 — Verifiable Self-Custody Release

> Release date: April 2026
> Live at: https://dgbwallet.app
> Custody walkthrough: https://github.com/DennisPitallano/digibyte-wallet/blob/main/SELF_CUSTODY.md
> Architectural audit: https://github.com/DennisPitallano/digibyte-wallet/blob/main/docs/walletscrutiny-self-eval.md
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.1.4

---

## Short post (Telegram / Twitter / Nostr, ~280 chars)

🟦 DigiByte Wallet v0.1.4 — first stable release.

Self-custodial by architecture, now **verifiable** too: strict CSP, reproducible builds, per-release hash manifests, public custody walkthrough.

Submitted for independent WalletScrutiny review.

→ https://dgbwallet.app

---

## Medium post (Reddit / forums / blog intro, ~170 words)

**DigiByte Wallet v0.1.4 — verifiable, by design**

The wallet has always been self-custodial: your seed is generated, encrypted (AES-256-GCM + PBKDF2-SHA256, 100,000 iterations), stored in IndexedDB, and used to sign — all in your browser, never on our server. v0.1.4 is the first stable release with that architecture made *verifiable*.

What's new on the verifiability axis:

- **Strict Content Security Policy.** No `'unsafe-inline'` on `script-src` or `style-src`; every inline script is covered by a per-build SHA-256 hash injected at publish time.
- **Reproducible builds.** `<Deterministic>` + `<ContinuousIntegrationBuild>` repo-wide; .NET SDK pinned via `global.json`; npm lockfile committed.
- **Per-tag release manifests.** Every `wallet-v*` tag attaches a JSON manifest of every published file's SHA-256 — plus a zipped copy of the bundle — to the GitHub Release.
- **Plain-English custody walkthrough.** `SELF_CUSTODY.md` is a 200-line tour of where keys live, what the server sees, and how to verify yourself with devtools.
- **Independent review submitted.** Filed at WalletScrutiny work_items/907; we'll post a follow-up when the listing lands.

→ https://dgbwallet.app

---

## Long post (newsletter / blog, ~450 words)

### DigiByte Wallet v0.1.4 — verifiable, by design

The DigiByte Wallet has always been **self-custodial**. Your seed phrase is generated by code that runs inside your browser, encrypted with a password you choose (AES-256-GCM, PBKDF2-SHA256, 100,000 iterations), and stored in your browser's IndexedDB. Signing happens in WebAssembly, on your device. The server side of `dgbwallet.app` is a static asset host — it serves the bundle, then steps out of the way. There is no API endpoint on the wallet origin that accepts seed phrases or private keys.

That was true at v0.9.0-beta. What's different at v0.1.4 is that you can now *verify* it.

**What ships in this release**

- **Strict Content Security Policy.** `script-src` and `style-src` no longer carry `'unsafe-inline'`. Every executable script either lives at a verifiable URL on the wallet origin or is covered by a per-build SHA-256 hash. The hashes are computed at publish time by a small MSBuild task and injected into the CSP `<meta>` tag automatically — every `dotnet publish` produces a strict-CSP `index.html`, no manual step.

- **Reproducible builds.** A repo-wide `Directory.Build.props` sets `<Deterministic>` + `<ContinuousIntegrationBuild>` for every .NET project in the tree. The .NET SDK is pinned via `global.json`. The npm `package-lock.json` is committed; Tailwind and Inter are pinned to exact versions.

- **Per-tag release manifests.** Every `wallet-v*` tag triggers a GitHub Action that publishes the wallet, walks the published `wwwroot/`, and writes a sorted JSON manifest of every file's size + SHA-256. The manifest and a zipped copy of the bundle land on the Release page. A reviewer — or you — can hash what `dgbwallet.app` actually serves and diff against the published manifest to confirm what your browser is loading matches what we publish.

- **Plain-English custody walkthrough.** [`SELF_CUSTODY.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/SELF_CUSTODY.md) is a tour of where keys live, what the server sees, and how to verify yourself with devtools — written for users, not for auditors. The deeper architectural review (with file/line references) is at [`docs/walletscrutiny-self-eval.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/docs/walletscrutiny-self-eval.md).

- **Security policy.** [`SECURITY.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/SECURITY.md) is the front door for vulnerability reporting — GitHub Security Advisories as the primary private-disclosure channel, with response SLAs and explicit scope.

**Independent review submitted**

We've also filed the wallet for review at [WalletScrutiny](https://walletscrutiny.com) — the third-party watchdog that audits self-custodial wallets against verifiability and custody criteria. Tracking at [work_items/907](https://gitlab.com/walletscrutiny/walletScrutinyCom/-/work_items/907). We'll post a follow-up when the listing lands.

DigiByte Wallet is open source, non-custodial, and — as of v0.1.4 — verifiable. The seed you back up is yours; the JavaScript your browser runs comes from a hash you can compute yourself.

→ Open the wallet: <https://dgbwallet.app>
→ Read the custody story: <https://github.com/DennisPitallano/digibyte-wallet/blob/main/SELF_CUSTODY.md>
→ Audit the architecture: <https://github.com/DennisPitallano/digibyte-wallet/blob/main/docs/walletscrutiny-self-eval.md>

— Built by [Dennis Pitallano](https://dennispitallano.github.io) and contributors.
