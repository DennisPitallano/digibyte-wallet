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
| `DigiByte.Crypto` | Class Library | BIP39/BIP44, HD keys, tx building, Digi-ID, WIF import |
| `DigiByte.Wallet` | Class Library | Wallet service, encryption, contacts, storage abstractions |
| `DigiByte.Web` | Blazor WASM PWA | The wallet UI — all pages, components, JS interop |
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
  │     ├── BlockbookApiService    (digibyteblockexplorer.com — primary, returns scriptPubKey)
  │     ├── BlockchainApiService   (digiexplorer.info — Esplora)
  │     └── BlockbookApiService    (dgb-explorer.nownodes.io — NOWNodes free)
  ├── NodeApiBlockchainService     (own pruned node — last resort for reads)
  └── MockBlockchainService        (demo data — development only)
```

**Reads** (balance, UTXOs, tx history, fees): Explorers first → Own Node → Mock (dev) / Error (prod)
**Writes** (broadcast tx): Own Node first → Explorers in order → Error

### Explorer Backends

| Priority | Name | Type | URL | Notes |
|----------|------|------|-----|-------|
| 1 | blockbook-primary | Blockbook | digibyteblockexplorer.com | Free, returns scriptPubKey in UTXOs |
| 2 | esplora | Esplora | digiexplorer.info | Existing, no scriptPubKey in UTXOs |
| 3 | blockbook-nownodes | Blockbook | dgb-explorer.nownodes.io | Free NOWNodes tier |
| Last | node-api | Own Node RPC | Railway (configurable) | Pruned node, scantxoutset for reads |

Explorers are registered in `DigiByte.Web/Program.cs`. To add or remove backends, edit the `explorers` list.

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

**Railway services:**
1. `digibyted` — Pruned mainnet node (entrypoint override: `entrypoint-mainnet.sh`)
2. `digibyte-node-api` — REST wrapper for RPC
3. `digibyte-api` — P2P marketplace backend

### Multi-Network Support
- Mainnet: `dgb1...` (SegWit), `D...` (Legacy)
- Testnet: `dgbt1...`, `t...`
- Regtest: `dgbrt1...`
- Network auto-detected from WIF key prefix on import

### Wallet Types
- **HD Wallet**: BIP39 mnemonic, BIP44 derivation `m/44'/20'/0'/change/index`
- **Private Key Wallet**: Single WIF key import, supports both Legacy and SegWit addresses

## UI Architecture

### Layout
- **MainLayout.razor** — Root wrapper, 480px max-width mobile-first container
- **NavMenu.razor** — Fixed bottom nav (5 tabs: Wallet, Pay, P2P, ID, Settings) with frosted glass
- **AuthGuard.razor** — Protects pages: no wallet → `/welcome`, locked → `/unlock`
- **OfflineBanner** — Amber banner when offline
- **ToastContainer** — Top-center notifications (success/error/warning/info)

### Shared Components

| Component | Purpose |
|-----------|---------|
| `PinInput` | 6-digit OTP input, auto-focus, paste support, shake on error |
| `QrScannerModal` | Camera QR scanner with jsQR decoding + file upload fallback |
| `ToastContainer` | Auto-dismiss notifications via NotificationService |
| `OfflineBanner` | Offline indicator via NetworkStatusService |
| `DigiLogo` | Official DigiByte SVG logo, configurable size |
| `OnboardingWalkthrough` | 5-step first-time carousel, persisted in localStorage |
| `TransactionDetailModal` | Tx detail popup — status, fee, explorer link, add to contacts |

### Services (DI in Program.cs)

| Service | Scope | Purpose |
|---------|-------|---------|
| `WalletService` | Scoped | Wallet CRUD, unlock, sign, balance, send |
| `ContactService` | Scoped | Contact CRUD, search |
| `PaymentRequestService` | Scoped | BIP21 payment request management |
| `FallbackBlockchainService` | Scoped | Cascading blockchain backend orchestration |
| `NodeApiBlockchainService` | Scoped | RPC wrapper for own node |
| `BlockbookApiService` | Scoped | Blockbook REST API (×2 instances) |
| `BlockchainApiService` | Scoped | Esplora REST API |
| `MockBlockchainService` | Scoped | Demo data (development only) |
| `AppState` | Singleton | Theme, network, display mode, currency |
| `ThemeService` | Singleton | Dark/light mode toggle |
| `LocalizationService` | Singleton | 10-language i18n |
| `NetworkStatusService` | Singleton | Online/offline detection |
| `NotificationService` | Singleton | Toast notification dispatch |
| `NfcService` | Scoped | NFC tap-to-pay (Web NFC API) |

### Animation & UX Patterns
- **Page transitions** — `slide-up`, `fade-in`, `scale-in` keyframe animations
- **Interactive feedback** — `press-effect` (scale 90% on active), group hover
- **Loading** — Skeleton shimmer screens, spinner animations, Virtualize for long lists
- **Pull-to-refresh** — Custom touch handler on Home dashboard
