# Architecture

## Overview

The DigiByte Wallet is a self-custodial Progressive Web App (PWA) built with Blazor WebAssembly. All cryptographic operations happen in the browser — private keys never leave the user's device.

```
┌─────────────────────────────────────────────────────┐
│                   User's Browser                     │
│  ┌───────────────────────────────────────────────┐  │
│  │           Blazor WASM PWA (DigiByte.Web)       │  │
│  │  ┌─────────┐ ┌──────────┐ ┌────────────────┐ │  │
│  │  │   UI    │ │  Wallet  │ │    Crypto      │ │  │
│  │  │ (Razor) │ │ Service  │ │ (NBitcoin)     │ │  │
│  │  └────┬────┘ └────┬─────┘ └───────┬────────┘ │  │
│  │       │           │               │           │  │
│  │  ┌────┴───────────┴───────────────┴────────┐  │  │
│  │  │        IndexedDB (AES-256-GCM)          │  │  │
│  │  │     Encrypted seeds, contacts, txs       │  │  │
│  │  └─────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────┘  │
│                        │                             │
│                        │ HTTP (signed txs only)      │
└────────────────────────┼─────────────────────────────┘
                         │
          ┌──────────────┴──────────────────────────────────────┐
          │                             │                       │
    ┌─────┴──────┐     ┌────────────────┴──────────┐    ┌───────┴───────┐
    │  Blockbook  │     │     Esplora Explorer      │    │   NOWNodes    │
    │ (Primary)   │     │  digiexplorer.info         │    │  Blockbook    │
    │ Free, has   │     │  (Fallback #2)             │    │ (Fallback #3) │
    │ scriptPubKey│     └──────────────────────────┘    └───────────────┘
    └─────────────┘
          │ (all explorers failed)
    ┌─────┴──────┐
    │  Node API  │
    │ (Railway)  │
    │ Port 5260  │
    └─────┬──────┘
          │
    ┌─────┴──────┐
    │ digibyted  │
    │ (pruned)   │
    │ RPC 14022  │
    └────────────┘
```

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `DigiByte.Server.Shared` | Class Library | Outbound-request guard for the two public APIs only (`DigiByte.Api`, `DigiByte.Pay.Api`): URL and resolved-address checks, no redirects, capped reads, client address for rate limits. References no other project. |
| `DigiByte.Crypto` | Class Library | BIP39/BIP84, HD keys, tx building, Digi-ID, WIF import, multisig (redeem scripts, PSBT), PSBT review and signing (`Psbt/`: `PsbtDocument`, `PsbtAnalyzer`, `PsbtSigner`) |
| `DigiByte.Wallet` | Class Library | Wallet service, encryption, contacts, storage abstractions, multisig wallet management |
| `DigiByte.Web` | Blazor WASM PWA | The wallet UI — all pages, components, JS interop |
| `DigiByte.UI` | Razor Class Library (static assets only) | The shared style layer: `css/ui.css` (tokens and component classes), the two self-hosted fonts and the Baku poses DigiPay uses. Referenced by `DigiByte.Web` and `DigiByte.Pay.Web`, which copy it in at build time and serve it from their own origin at `_content/DigiByte.UI/...`. References no other project. See `docs/UI_STYLE.md`. |
| `DigiByte.Api` | Web API | P2P marketplace backend (future) |
| `DigiByte.NodeApi` | Web API | Wraps 87 digibyted RPC methods into REST + Scalar docs |
| `DigiByte.P2P.Shared` | Class Library | Shared models for P2P exchange |

## Key Design Decisions

### Self-Custodial
- Private keys are generated and stored entirely in the browser
- Seeds encrypted with AES-256-GCM, key derived from user's PIN via PBKDF2
- The Node API only receives signed transactions — never sees private keys

### Cascading Blockchain Service
```
FallbackBlockchainService
  ├── Explorer List (tried in order, 2-min cooldown on failure)
  │     └── BlockchainApiService   (digiexplorer.info — Esplora)
  ├── NodeApiBlockchainService     (own pruned node — last resort for reads)
  └── MockBlockchainService        (demo data — development only)
```

**Reads** (balance, UTXOs, tx history, fees): Esplora first → Own Node → Mock (dev) / Error (prod)
**Writes** (broadcast tx): Own Node first → Esplora → Error

That is the order with node use switched off (the default). With node use on (`WALLET_NODE_ENABLED`, specs/005-wallet-own-node-source) a `NodeSourceMonitor` decides, from the node API's `/api/health` plus a height cross-check against an explorer, whether the node is `Active`:

- **Node not active** (starting, syncing, unavailable, stabilizing): explorers only, for reads and sends; the node is not even a last resort, because a syncing node answers address lookups with incomplete data.
- **Node active**, by route class: **fast** routes (transaction lookup, fee, height) and **broadcast** try the node first and fall back to an explorer per request; **address** routes (balance, spendable outputs) stay explorer-first with the node as their fallback (the node answers them by scanning, about 14 s); **history** is explorers only (the node cannot list full history).
- Promotion needs the node to stay ready for a stable period (default 2 minutes); demotion is immediate on a not-ready check, or after three failed node reads in a row.
- In this mode the node client runs in strict mode (a failed request is an error, never a zero), a source switch clears cached answers, and the UI shows a data-source dot with a plain-language detail, plus a banner only on real degradation.

### Balance and history read path (specs/023-faster-balance-history)

A seed wallet watches about 80 addresses. The read path keeps the number of explorer requests per refresh low:

- **Shared reads.** `FallbackBlockchainService` caches each finished read (30 s for balance and history, 15 s for the tip) and lets concurrent identical reads share one request. `InvalidateCache()` (after every send) also drops reads still in flight, through a generation number, so nothing from before a send is stored after it. History is cached per address (`txs:{address}`) whatever page size a caller asks for. `BlockchainApiService` shares one tip height for 10 s across every caller, and pages an address's history past Esplora's 25-transaction page (up to 2,000).
- **Change-only sync** (`WalletHistorySync`, on by default). Each refresh reads one summary per watched address (`GET /address/{a}`: confirmed and pending counts and amounts), computes the balance and the net pending amount from them, and fetches history pages only for addresses whose summary changed, stopping at a transaction already saved. Summaries read in the last 30 s are reused (address discovery seeds them), and a failed summary fails the balance read. Results are merged by txid into a saved, explorer-shaped history, and confirmations are recomputed from each transaction's block and one tip read. Saved data lives in `ISecureStorage` under `addr_summary_{walletId}_{network}` and `chain_history_{walletId}_{network}`, the same storage and protection as the Home cache, and is removed with the wallet.
- **Full re-read once per unlock.** After the first change-only history result, a background task re-reads every used address in full, two requests at a time, giving way to the user's own refreshes. It corrects what summaries can't show (a transaction moved by a reorganisation) and completes a busy address's history, then replaces the saved history in one write. It is skipped when every address was already read in full in this session, cancelled on lock or wallet switch, and retried a minute after a failure.
- **Source order for history.** (1) The SPV gateway's one-pass wallet history (`IWalletHistorySource`, each raw transaction parsed once) while the gateway answers history; (2) change-only sync while an explorer can give summaries (never on regtest, and never while the gateway is first); (3) every address in full, as before. Balance follows (2) then (3); a gateway-first wallet's balance comes from the gateway as before.
- **Tiered refresh** (specs/024-tiered-refresh, on by default with change-only sync). `HotAddressSelector` picks the addresses that can change from data already in memory: those with funds or pending activity, the receive address on show and the 3 handed out before it (SegWit and, when watched, legacy), the next change address, the inputs of a pending send, and addresses with activity in the last 7 days. An automatic refresh reuses a hot summary for 30 s and a cold one for 5 minutes, and reads a share of the oldest cold ones in proportion to the time since the last refresh, so none waits longer than 5 minutes and they never fall due together. Unlock, a send, Home's refresh button and pull, and Activity's pull to refresh (`WalletService.RequestFullCheck()`) read every address. Wallets with fewer than 10 watched addresses aren't tiered. Home's wallet info panel shows "Fully checked N min ago".
- **Send coin reads.** `SendCoinPlanner` lets send, batch send and consolidate read coins only from addresses whose summary (read within 5 minutes) shows funds or pending activity, still fresh; if those can't cover the amount plus 0.01 DGB, the rest are read before "insufficient funds". Without such summaries every address is read, as before.
- **Switches and benchmark.** `/dev/node` has "Change-only history sync" (`dev:history-change-only`) and "Tiered refresh" (`dev:tiered-refresh`) switches and a wallet refresh benchmark that shows time and explorer requests by kind, quiet refreshes with tiering on and off, and how many addresses a send would read, counted by `RequestCountingHandler` on the explorer `HttpClient` in memory only. The switches are for developers, testing and benchmarks: a saved value counts only while the developer view is on (`HistorySyncOptions.FromStorage`); otherwise both are on.
- **Instant pages.** `WalletSnapshotCache` (Web) holds the figures Home has always saved (`cache:balance:{id}`, `cache:transactions:{id}`, `cache:lastFetch:{id}`, the shared `cache:dgbPrice`). Transactions, Receive and Send show them at once and refresh in the background; a refresh that can't reach the chain keeps them with "Last updated". Home's main figure is the confirmed balance, with a separate "Pending ±X DGB" line; Send offers `AvailableDgb` = confirmed + min(0, pending). The coins for a send are always read fresh.

### Paste, drop and payment links (specs/030-desktop-paste-scan)

Desktop users rarely hold a code up to a camera: it is on the same screen or already copied.

- **Rules in `DigiByte.Wallet`** (tested): `PastedCode` (cleaning pasted text, spotting a recovery phrase or private key), `ScanInputMode` (paste or camera first, from device facts and the remembered choice), `PaymentLink` (making and checking `{base}pay#digibyte:…` links).
- **Browser glue** (`wwwroot/js/qrscanner.js`): device facts without prompts, clipboard reading, paste and drop listeners, QR reading from pictures and from screen-share frames. Picture bytes and frames never reach .NET or the network.
- **Routing**: `ScanRouter` also recognises this wallet's own payment link (same scheme, host and port, path `/pay`); a look-alike site's link goes nowhere.
- **`/pay` page and `PaymentHandoff`**: the payment stays after `#`, so the host only ever sees `GET /pay`. With a wallet, the page hands the payment to Send in memory (`PaymentHandoff`, one-shot, like `ProposalHandoff`); without one it shows a read-only page that cannot move funds.
- **Installed app**: `manifest.webmanifest` registers `web+digibyte` → `/pay#%s` next to `web+digiid`. If a browser refuses a placeholder after `#`, the handler is removed rather than moved into the query.

### First-receive flow (specs/025-first-receive-flow)

A new user's first session is built around getting a first payment.

- **State per wallet** (`FirstReceiveRule`, `FirstReceiveStore` in `DigiByte.Wallet`): one stored value, `empty`, `history` or `celebrated`. A wallet created on the device is `empty` from the start. Any other wallet is decided by history: saved history with a transaction means `history`; a complete load with nothing in it, after address discovery has finished, means `empty`. Only an `empty` wallet can celebrate, once.
- **What a load proves** (`WalletService.LoadTransactionHistoryAsync`): each call returns its own `Complete` and `AddressesSettled`. A failed summary read is incomplete even though the per-address fallback answers, because that reader returns nothing for an address it cannot reach. The shared `LastHistoryIncomplete` is set from the same result for older callers.
- **Coordinator** (`FirstReceiveCoordinator` in `DigiByte.Web`): Home, Receive and Activity report every history load to it. It keeps the pending celebration in memory and marks the wallet `celebrated` when the overlay is actually on screen, so a celebration that could not be shown is offered again.
- **Shared refresh** (`RefreshPacer`, `AutoRefreshLoop`): Home's poller pacing, used by Home and by Receive, which now checks while open. `NewArrivals` and `IncomingPaymentAnnouncer` are Home's "payment received" notice, one instance per page.
- **First-session marker**: a per-device flag set the first time any wallet shows a transaction. Until then the install banner (outside the setup pages) and the sponsors strip are hidden.

### P2P light-client gateway (optional, off by default)

`DigiByte.SpvGateway` is a separate ASP.NET service that depends only on `DigiByte.Crypto` (specs/011-p2p-spv-gateway). It connects to public peers, verifies
headers (linkage, per-algorithm difficulty, proof of work for all five algorithms), indexes the blocks of a 90-day scan window in SQLite, and serves
`/v1/health`, `/v1/headers`, `/v1/scan`, `/v1/utxos`, `/v1/tx`, `/v1/proofs`, `/v1/broadcast` and `/v1/fee`. It keeps nothing about a request.

In the wallet it is one more source in the chain: `P2pGatewayBlockchainService` answers from the wallet's own sync record (`WalletSyncState`), which is
refreshed through `GatewayIncrementalSync` and rebuilt only from transactions whose merkle proofs `GatewayProofChecker` matched to headers the wallet
verified itself (`GatewayHeaderStore`, `GatewayHeaderSync`). `GatewayTipCrossCheck` compares those headers with public explorers. A readiness monitor with
the same rules as the own-node monitor decides when it may be used, and it sits behind the explorers until the wallet checks proof of work itself.
A question it cannot answer correctly throws `GatewayDeclinedException`, so the next source answers and the gateway is not demoted.

### Explorer Backends

| Priority | Name | Type | URL | Notes |
|----------|------|------|-----|-------|
| 1 | esplora | Esplora | digiexplorer.info | Primary public explorer |
| Last | node-api | Own Node RPC | Railway (configurable) | Pruned node, scantxoutset for reads |

Explorers are registered in `DigiByte.Web/Program.cs`. To add or remove backends, edit the `explorers` list.

### Oracle price and status (explorer backend)

The explorer has no oracle data, so with `DigiDollar:Backend=esplora` two small pieces fill the gap (specs/014-digibyte-io-oracle):

- **Relay** (`DigiByte.Api`: `OracleRelay`, `GET /api/oracle`): fetches `getoracleprice` and `getdigidollarstats` from digibyte.io, validates them, keeps one shared answer (one attempt per kind per 30 s, served until 5 minutes after it was fetched) and serves the merged result. 503 when there is nothing usable, 404 when `Oracle__Enabled` is false.
- **Wrapper** (`OracleEnrichedDigiDollarApi` over `EsploraDigiDollarApi`, fed by `OracleDataServiceClient`): `GetHealthAsync` awaits only the explorer, merges the reading the feed already holds into `NetworkHealth` and asks the feed to refresh in the background. The screens re-merge when the feed reports a change. Mainnet only; an unavailable health stays unavailable.

Display only: the oracle never feeds a transaction. The home balance keeps the market price. The wallet only talks to the relay (`DigiDollar:OracleApiUrl`, https or loopback), never to digibyte.io.

### Daily price ranges for CSV export (spec 031)

The CSV export values each transaction at its local day's price. The wallet makes **one** request per export to the price
service, carrying only a currency, an asset (`dgb` or `dd`) and two dates; nothing about the wallet.

- **Endpoint** (`DigiByte.Api`: `PriceRangeEndpoint`, `GET /api/price/range?currency=&asset=&from=&to=`): returns one price per
  UTC day with a `basis` of `native`, `convertedFromUsd` (the USD price at that day's USD rate) or `peg` (DigiDollar, 1 DD = 1 USD).
  Rate-limited (30/min per visitor), cached for 1 hour, logs status codes only.
- **Archive** (`PriceArchive`, `src/DigiByte.Api/PriceArchive/*.csv`): read-only data shipped with the service:
  `dgb-daily-{currency}.csv` (DGB price per day) and `usd-fx-{currency}.csv` (units per USD per published day). Archived days
  are served first and win over live values. A day without a published rate uses the latest earlier one up to 7 days old.
- **Live sources** (days after the archive only, called by the price service, never by the wallet): CoinGecko
  `market_chart/range` (its free tier covers the last 365 days), Frankfurter/ECB daily rates, and the free `exchange-api` daily
  snapshots for currencies the ECB doesn't publish.
- **Refresh**: `tools/PriceArchiveImport` seeds the archive (a maintainer, once). `.github/workflows/price-archive-refresh.yml`
  runs monthly, appends new days and opens a pull request for review. It never merges, and it fails when a file falls more
  than 300 days behind.

In the wallet, `TransactionExportService` (`DigiByte.Wallet/Services/Export`) reads the complete history through strict reads
(`WalletService.LoadCompleteHistoryAsync`, `DigiDollarService.LoadCompleteHistoryAsync`). These have no per-address cap and
throw on any failure, and they never use the saved copy Activity shows offline. The service then writes the file on the
device; `ExportCsvSheet` saves or shares it.

### Sponsors (list read from GitHub, submissions as pull requests)

The Sponsors page reads `data/sponsors.json` live from the `sponsors` branch of the project's repository (`raw.githubusercontent.com`, one request per page open, last good copy kept in local storage). The form posts to one endpoint on `DigiByte.Api`, a public-contribution intake relay: it validates with the shared rules in `DigiByte.P2P.Shared`, checks the donation on the public explorers, and opens a pull request through a server-side bot token. A maintainer merges; a workflow on the branch regenerates the list. No database is used and `main` is never touched. See [SPONSORS.md](SPONSORS.md) and `specs/006-sponsors-page/`.

### Docker / Deployment

| Compose File | Network | Pruning | `txindex` | Use |
|---|---|---|---|---|
| `docker-compose.yml` | Regtest | Off | Yes | Local dev (instant mining) |
| `docker-compose.testnet.yml` | Testnet | Off | Yes | Integration testing |
| `docker-compose.mainnet.yml` | **Mainnet** | **`prune=550`** | **No** | **Production (Railway)** |

**Mainnet pruned node:**
- Keeps only ~550 MB of recent blocks (saves 35+ GB disk vs full node)
- Full UTXO set is always maintained (`scantxoutset` works)
- `getrawtransaction` only works for mempool and recent blocks
- Tuned for Railway: `dbcache=256`, `maxmempool=50`, `maxconnections=24`
- RPC password **must** be changed before deploying (`digibyte-mainnet.conf`)

**Railway full node (separate project):** a second, preferred setup runs an unpruned `txindex` node (DigiByte Core v9.26.7, wallet disabled) with its own copy of the node API in a dedicated Railway project. The node's RPC port is only reachable over that project's private network; the node API is the single public surface. Operator-only routes (keys, wallet, mining, node control, block invalidate, costly reads) need `X-NodeApi-Key` outside Development. See [RAILWAY_NODE.md](RAILWAY_NODE.md).

**Railway services (existing project):**
1. `digibyted` — Pruned mainnet node (entrypoint override: `entrypoint-mainnet.sh`)
2. `digibyte-node-api` — REST wrapper for RPC
3. `digibyte-api` — P2P marketplace backend

### Multi-Network Support
- Mainnet: `dgb1...` (SegWit), `D...` (Legacy)
- Testnet: `dgbt1...`, `t...`
- Regtest: `dgbrt1...`
- Network auto-detected from WIF key prefix on import

### Wallet Types
- **HD Wallet**: BIP39 mnemonic, BIP84 derivation `m/84'/20'/0'/change/index` (native SegWit)
- **Private Key Wallet**: Single WIF key import, supports both Legacy and SegWit addresses

### BIP44→BIP84 Migration (Temporary)
Wallets created before the BIP84 fix used `m/44'/20'/...` derivation with SegWit addresses — a non-standard combination. On Home page load, the app scans old BIP44 addresses for stranded UTXOs and shows a migration banner if funds are found. Users can sweep all funds to their current BIP84 address in one transaction. This check auto-expires after **May 10, 2026** to avoid unnecessary API overhead.

## UI Architecture

### Layout
- **MainLayout.razor** — Root wrapper, 480px max-width mobile-first container
- **NavMenu.razor** — Fixed bottom nav (5 tabs: Wallet, Pay, P2P, ID, Settings) with frosted glass
- **AuthGuard.razor** — Protects pages: no wallet → `/welcome`, locked → `/unlock`
- **HiddenNavPaths** — Routes without bottom nav: `/welcome`, `/create-wallet`, `/recover-wallet`, `/unlock`, `/about`, `/roadmap`, `/analytics`, `/deployment`, `/help`, `/help/multisig`, `/pay`
- **OfflineBanner** — Amber banner when offline
- **ToastContainer** — Top-center notifications (success/error/warning/info)

### Shared Components

| Component | Purpose |
|-----------|---------|
| `PinInput` | 6-digit OTP input, auto-focus, paste support, shake on error |
| `QrScannerModal` | The scan panel (spec 030): paste first on desktop (field, clipboard, picture paste, drag-and-drop, upload, scan from screen), camera first on phones; same parameters for every caller, `FromMenu` only for the menu |
| `ToastContainer` | Auto-dismiss notifications via NotificationService |
| `OfflineBanner` | Offline indicator via NetworkStatusService |
| `DigiLogo` | Official DigiByte SVG logo, configurable size |
| `ReceiveEmptyState` | Home's empty state: the receiving address, its QR code, Copy address and a link to Receive (spec 025) |
| `FirstReceiveCelebration` | The one-time first-payment celebration, placed on Home, Receive and Activity (spec 025) |
| `TransactionDetailModal` | Tx detail popup — status, fee, explorer link, add to contacts |
| `BakuMessage` | Baku the mascot: one guide, tip or warning card (pose + text); text-only when Baku is Off or the image fails |
| `BakuWarningDialog` | Guardian confirmation ("Go back" / "I understand, continue"), hosted once in `MainLayout` |

### Services (DI in Program.cs)

| Service | Scope | Purpose |
|---------|-------|---------|
| `WalletService` | Scoped | Wallet CRUD, unlock, sign, balance, send |
| `ContactService` | Scoped | Contact CRUD, search |
| `PaymentRequestService` | Scoped | BIP21 payment request management |
| `MultisigWalletService` | Scoped | Multisig wallet CRUD, PSBT signing workflow, co-signer management |
| `SigningRoomClient` | Scoped | Connection to the online signing-room hub (`/hubs/multisig-signing`); connects only when a room is opened or joined |
| `PsbtSigningService` | Scoped | Loads, reviews, signs and broadcasts PSBTs made elsewhere (`/settings/psbt-sign`). Gathers chain facts and runs the pure `PsbtAnalyzer`; holds no PSBT between calls |
| `IPsbtKeyProvider` | Scoped | Implemented by `WalletService`: the PSBT feature's only route to keys. Gives owned scripts (no key material) and signs chosen inputs on a copy |
| `PsbtConfirmationFlow` | Per sign | Fixed order before signing or broadcasting: acknowledgment, spending limit, warnings, then biometrics or PIN. Stops at the first failure |
| `PinAttemptThrottle` | Per page | Wrong-PIN delay shared by the unlock and PSBT signing pages |
| `FallbackBlockchainService` | Scoped | Cascading blockchain backend orchestration |
| `NodeApiBlockchainService` | Scoped | RPC wrapper for own node |
| `BlockchainApiService` | Scoped | Esplora REST API (digiexplorer.info) |
| `MockBlockchainService` | Scoped | Demo data (development only) |
| `AppState` | Singleton | Theme, network, display mode, currency |
| `ThemeService` | Singleton | Dark/light mode toggle |
| `LocalizationService` | Scoped | English and Spanish; text, number style and the saved choice (see Localization) |
| `NetworkStatusService` | Singleton | Online/offline detection |
| `NotificationService` | Singleton | Toast notification dispatch |
| `NfcService` | Scoped | NFC tap-to-pay (Web NFC API) |
| `BakuService` | Scoped | Baku presence mode and dismissed tips (localStorage `dgb-baku-mode`, `dgb-baku-dismissed`); picks the single message to show |

### Animation & UX Patterns
- **Page transitions** — `slide-up`, `fade-in`, `scale-in` keyframe animations
- **Interactive feedback** — `press-effect` (scale 90% on active), group hover
- **Loading** — Skeleton shimmer screens, spinner animations, Virtualize for long lists
- **Pull-to-refresh** — Custom touch handler on Home dashboard

### Baku, the mascot (guide and guardian)

Baku is a static, client-side character; there is no backend and no AI. Spec: `specs/002-baku-mascot-guide/`.

- **Messages**: predefined English messages in `BakuCatalog` (id, severity Info/Tip/Warning/Critical, pose, title, body). Only Info and Tip can be dismissed.
- **One at a time**: `BakuService.Current` shows the highest severity, ties to the most recent request. Warning and Critical are never hidden by presence mode; they fall back to text-only when Baku is Off or the image fails to load.
- **Guardian**: `BakuService.ConfirmAsync` / `ConfirmSendRiskAsync` show `BakuWarningDialog` before the seed phrase is revealed, before a DGB or DigiDollar send to a new address or of 50% or more of the balance, before approving a Digi-ID login to a first-time or insecure (HTTP) site (`DigiIdRiskEvaluator`), and on the P2P page. It never permanently blocks a confirmed action and fails open if it cannot check.
- **Send risk rule**: `DigiByte.Wallet.Services.SendRiskEvaluator` (pure, unit-tested) with `KnownAddressProvider` (saved contacts plus previously-sent addresses, derived from existing history).
- **Poses**: transparent WebP files in `wwwroot/images/mascot/poses/`, cut from the source sheets by `tools/mascot-slice/slice.py`. The full-size source sheets stay in the repo but are excluded from publish output.
- **Privacy and CSP**: only a presence mode and tip ids are stored on the device; assets are self-hosted, so the CSP is unchanged.

### Localization (specs/028-spanish-language-option)

- **Languages**: English and Spanish. Translations are flat key → text files in `wwwroot/locales/{code}.json`; `LocalizationService.SupportedLocales` lists exactly the languages that have a file. Any text missing from a language shows in English, never as its key.
- **Choosing**: Settings → Language opens `LanguagePickerSheet`. At startup the wallet uses the saved choice (`localStorage["dgb-locale"]`), else the browser's first language if it is offered (not saved), else English. A switch only happens once that language's file has loaded; if it can't load, nothing changes and the sheet says so.
- **Live switch**: `MainLayout` wraps the whole shell in `<CascadingValue Name="Locale">`. Every component that shows translated text declares `[CascadingParameter(Name = "Locale")] public string? Locale` and so re-renders on a switch, keeping what the person typed. No page reload. The page's `lang` attribute follows the language, so screen readers pronounce it correctly.
- **Number and date style follow the wallet language, not the browser**: the service sets the default thread culture (`en-US` or `es`), so `ToString("N8")` and similar follow it everywhere. The web project loads only the `icudt_EFIGS.dat` ICU subset (`BlazorIcuDataFileName`), which has both languages; with Blazor's default per-browser subsets a culture other than the browser's is refused at startup.
- **Machine-read values never use the display style**: payment links, QR codes, navigation query strings, storage and API calls use `InvariantCulture` (`PaymentRequest.ToUri`/`FromUri`, `ScanRouter`, `DonationInfo`). Link amounts accept only digits and one point, so `amount=1,5` is left empty rather than read as 15.
- **Typed amounts**: `DigiByte.Wallet.Services.AmountEntry` (pure, unit-tested) reads Send, Receive and spending-limit amounts the same way in every language: a comma or a point is the decimal mark, more than one mark is refused, and one mark followed by exactly three digits that could be a thousands mark ("1.234") is returned as ambiguous so the screen asks which was meant (`AmountAmbiguityChoice`). Amounts the wallet fills in itself (a link, a scan, Max) are recorded as already settled.
- **Adding a language**: add `wwwroot/locales/{code}.json` with every English key, one line in `SupportedLocales`, and check `BlazorIcuDataFileName` covers it. `LocaleFilesTests` enforce key parity, matching placeholders, that every `L.T("…")` key exists, and that the in-scope screens have no fixed English.
- **Review**: Spanish security and money warnings need a native speaker's approval before release (`specs/028-spanish-language-option/translation-review.md`).

## Online multisig signing rooms (specs/017-multisig-online-signing)

`DigiByte.Api` hosts `/hubs/multisig-signing` (`MultisigSigningHub`, rules in `Multisig/SigningRoomRegistry`), a
co-signer coordination relay under constitution 1.2.0. Connections are anonymous; a connection sees or changes a room
only after a single-use `CoSignerProof` (signing-room domain) shows it holds one of that room's co-signer keys. Rooms
carry public data only (keys, PSBT, signatures, display names, status), live in memory, end after 15 minutes to 24 hours
(`MultisigSigning:MaxLifetimeMinutes`, default 1440), and are swept every 15 s. Per-connection limits: 5 failed joins per
minute, 10 rooms per hour; negotiate requests are limited to 30 per minute per client address. Logs and metrics
(`DigiByte.Api.SigningRooms`) hold anonymous counts only.

**Deployment constraint:** rooms live in one API instance's memory. Run a single instance, or route
`/hubs/multisig-signing` with sticky sessions so every participant of a room reaches the same instance.

## DigiPay DigiDollar payouts (specs/034)

DigiDollar payment addresses come from the DigiDollar node's `digipay-hot` wallet, which every store shares. `DigiByte.Pay.Api` keeps its own ledger of who owns that DD:

- `DdAddresses` records the owner (store or merchant) of every DD address DigiPay creates.
- `DdPayoutWorker` (a hosted service, every 30 seconds) reads the wallet's receives through NodeApi and credits `DdLedgerEntries` (payment, overpayment, late, extra refund or donation), applies payout address changes whose 48-hour wait is over, plans payouts per store schedule, checks that the wallet still covers everything DigiPay owes, and sends each payout with `/api/digidollar/send`, reconciling unclear sends through `/api/digidollar/sends` before anything is retried.
- The dashboard's **DigiDollar payouts** section and the read-only API show balances, hold reasons and payouts; payout events are signed webhooks stored with their body so retries resend it exactly.

Nothing in this path is used for DGB payments, which keep going straight to the merchant's own address or xpub.

## DigiPay buyer's choice of DGB or DigiDollar (specs/033)

A payment priced in a regular currency can be paid in DGB or DD. It stays one `PaySession` with one `PaySessionOption` row per currency; the session's own `Currency`, `Address` and amounts always describe the currency in effect.

- The DGB option is made with the session. The DD option is made the first time the buyer picks DD (`POST /v1/pay/sessions/{id}/options/dd`, anonymous and rate-limited), registered in `DdAddresses`, and priced once: USD at 1:1, other currencies at the USD rate, rounded up to the cent.
- `InvoiceMonitor` watches every option of an unlocked session (the explorer for DGB, NodeApi for DD; a DD error never stops DGB). The first funds lock the session to that option by copying it onto the session, so receipts, webhooks, stats and the POS need nothing new.
- After the lock, and through the 3-day late window, funds on the other option are recorded as an extra payment (`session.extra_payment`); extra DD is credited as `extra_refund` in the payout ledger.
- All of it is off until `DIGIPAY_DD_SURFACES` is on.
