# DigiByte Wallet v0.10.0 - Español, paste on desktop, payment links and CSV export

> Release date: November 2026
> Live at: https://dgbwallet.app
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.10.0

## Short post (~280 chars)

🟦 DigiByte Wallet v0.10.0: now in Español. Paste or drop a QR code on desktop, share payment links anyone can pay, export DGB and DigiDollar history to CSV, and pay up to 20 people from one multisig proposal.

→ https://dgbwallet.app

---

## Medium post

**Español, desktop-friendly scanning, payment links and CSV export**

**Spanish is here.** Settings → Language now offers English and Español, and switches at once without a reload. A browser set to Spanish opens the wallet in Spanish on the first visit. Amounts and dates follow the wallet's language, and an amount like "1,234" asks whether you meant about one or about one thousand.

**Paste / Scan on a computer.** With a mouse or no camera, the middle menu button opens a paste panel: paste a copied address, payment request, Digi-ID link or multisig code, paste a screenshot, drop a picture, or read the QR code shown in another tab. Pictures and screen frames are read in the browser and never sent anywhere. Phones still open the camera first.

**Payment links.** Receive and Request can copy a link that opens Send with the address and amount filled in for anyone with this wallet, and a payment page with a QR code for everyone else. The payment details stay after the `#`, so the website never sees them, and nothing is sent until you review and slide.

**Export CSV.** Activity and DigiDollar can save your history as a spreadsheet, with each day's price and value in your currency, for bookkeeping.

**Multisig.** A shared wallet can pay 2 to 20 recipients in one proposal, signed once by each co-signer, and every co-signer sees each recipient before signing. Send now lists your own wallets and contacts.

**Missing payments.** Recovery Tools can search up to 5,000 addresses for payments sent beyond the usual range, and a restored wallet that looks empty offers to search further.

**DigiPay** now looks like the wallet, with a clearer checkout, a redesigned POS, sales charts, every wallet currency, and **Pay with DigiByte Wallet** right in the browser.

Details: [`CHANGELOG.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/CHANGELOG.md)

---

# DigiByte Wallet v0.9.0 - Multisig you can find your way around

> Release date: November 2026
> Live at: https://dgbwallet.app
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.9.0

## Short post (~280 chars)

🟦 DigiByte Wallet v0.9.0: multisig got a redesign. Inside a multisig the menu is the multisig's, Pending shows what needs you first, every payment has its own screen, and the signing room walks you through Invite, Sign, Broadcast.

→ https://dgbwallet.app

---

## Medium post

**Multisig you can find your way around**

Until now, opening a multisig left the menu pointing at your personal wallet: one tap on Wallet and you were back at the start. In 0.9.0 the menu belongs to the multisig while it is open (Overview, Activity, Scan, Pending, More), and every page says which multisig you are in and which wallet it belongs to. Scanning an address there pays from the multisig.

**Pending** now puts the payments that need you first, as compact cards with one button each. Tapping a payment opens it on its own screen, where everything for that payment lives: sign, slide to broadcast, retry, sign together online, share with co-signers, add a signed copy, cancel. A payment whose coins were already spent is listed apart and no longer asks for your signature.

**The signing room** is built around the next step. A bar shows the room's code, the time left and the connection; a tracker shows Invite, Sign, Broadcast; and your one button stays pinned to the bottom.

**The multisig list** shows each multisig's balance, what is waiting, and whether it is backed up. More and the Multisig shortcut on Home count the payments that need you, and the shortcut reopens the multisig you used last.

How payments are signed, checked and broadcast has not changed.

Also in this release: with several wallets the Unlock screen picks the wallet from a sheet, and "Update available" shows again instead of the app reloading on its own.

Details: [`CHANGELOG.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/CHANGELOG.md)

---

# DigiByte Wallet v0.8.1 - Fix for installed apps after the 0.8.0 update

> Release date: November 2026
> Live at: https://dgbwallet.app
> Release page: https://github.com/DennisPitallano/digibyte-wallet/releases/tag/wallet-v0.8.1

## Short post (~280 chars)

🟦 DigiByte Wallet v0.8.1: if your installed wallet showed a broken screen after updating to 0.8.0, open it once with a connection and it repairs itself. Your wallet, keys and funds were never affected. Offline use works again too.

→ https://dgbwallet.app

---

## Medium post

**A fix for installed apps**

Some installed copies of the wallet showed only large menu icons after 0.8.0. The app's update check had been failing silently since 0.7.0, so those devices kept old stylesheets and scripts while new code arrived from the network, and 0.8.0 changed enough for that mix to break.

0.8.1 fixes the update check. Open the app once with a connection: it downloads the update in the background and reloads itself. Nothing needs to be cleared or reinstalled, and your wallet data, keys and funds were never touched; only the app's own downloaded files were out of step.

Details: [`CHANGELOG.md`](https://github.com/DennisPitallano/digibyte-wallet/blob/main/CHANGELOG.md)

---

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
