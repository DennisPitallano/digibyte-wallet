# DigiByte Wallet

A self-custodial DigiByte (DGB) wallet built as a Progressive Web App (PWA) with Blazor WebAssembly (.NET 10). All cryptographic operations happen in the browser — private keys never leave your device.

## Features

- **Self-custodial** — Keys encrypted in browser IndexedDB with AES-256-GCM, never sent to any server
- **BIP39/BIP44 HD wallet** — 24-word mnemonic, derivation path `m/44'/20'/0'/change/index`
- **WIF private key import** — Single-key import with auto network detection (mainnet/testnet/regtest)
- **Send & Receive** — Real transaction building, signing, and broadcasting via NBitcoin
- **QR Scanner** — Camera-based scanning for addresses, `digibyte:` URIs, WIF keys, and Digi-ID URIs
- **Contacts** — Save addresses with names, QR scan, send with one tap
- **Payment Requests** — Generate BIP21 URIs with amount/label/message and QR codes
- **Digi-ID** — Passwordless authentication protocol (`digiid://` URI signing)
- **Light & Dark Mode** — Official DigiByte branding with theme toggle
- **Multi-network** — Mainnet, Testnet, and Regtest support
- **10 Languages** — English, Spanish, Chinese, Japanese, Korean, Filipino, Hindi, Arabic, Portuguese, French
- **OTP-style PIN** — 6-digit PIN with individual digit boxes, shake animation on error
- **Multi-explorer fallback** — Esplora → Own node → Error/Mock
- **Node API** — 87 RPC methods wrapped as REST endpoints with Scalar docs
- **Docker** — Regtest (instant mining), Testnet, and Mainnet pruned node configs
- **NFC tap-to-pay** — Web NFC API (experimental)
- **Remittance** — Fee comparison with traditional services
- **Analytics** — Market data, network stats, 7-day price chart from CoinGecko

## Architecture

```
digibyte-wallet/
├── src/
│   ├── DigiByte.Crypto/          # BIP39, BIP44, HD keys, tx building, Digi-ID
│   ├── DigiByte.Wallet/          # Wallet service, encryption, contacts, storage
│   ├── DigiByte.Web/             # Blazor WASM PWA (the wallet UI)
│   ├── DigiByte.Api/             # P2P marketplace backend API
│   ├── DigiByte.NodeApi/         # Node RPC wrapper (87 methods + Scalar)
│   └── DigiByte.P2P.Shared/      # Shared models for P2P exchange
├── tests/
│   ├── DigiByte.Crypto.Tests/    # xUnit — crypto, HD keys, addresses
│   ├── DigiByte.Wallet.Tests/    # xUnit — wallet service
│   ├── DigiByte.Api.Tests/       # xUnit — API endpoints
│   └── DigiByte.NodeApi.Tests/   # xUnit — Node API endpoints
├── docker/
│   ├── digibyted/                # DigiByte Core Docker image (multi-network)
│   └── node-api/                 # Node API Docker image
├── docs/
│   ├── ARCHITECTURE.md           # Detailed architecture & design decisions
│   ├── PROCESS_FLOWS.md          # Per-page technical flows
│   └── ROADMAP.md                # Development roadmap & status
├── docker-compose.yml            # Regtest (instant mining, local dev)
├── docker-compose.testnet.yml    # Testnet (real network)
└── docker-compose.mainnet.yml    # Mainnet pruned (production)
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor WebAssembly PWA (.NET 10) |
| Crypto | NBitcoin (HD keys, tx building, signing) |
| CSS | Tailwind CSS (CDN) |
| Storage | Browser IndexedDB (AES-256-GCM encrypted) |
| Node API | .NET 10 Minimal API + Scalar |
| Blockchain | DigiByte Core v8.26.2 (Docker) |
| Real-time | SignalR (P2P trade chat) |
| Testing | xUnit + coverlet |
| Deployment | Docker Compose / Railway |

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/) (for the DGB node)
- A modern browser (Chrome, Edge, Firefox, Safari)

### 1. Run the wallet UI only (no node)

```bash
cd src
dotnet run --project DigiByte.Web/DigiByte.Web.csproj
# Open http://localhost:5251
```

The wallet connects to a public Esplora explorer (digiexplorer.info) for blockchain data. No local node required for basic mainnet usage.

### 2. Run with Docker — Regtest (recommended for development)

```bash
# Start regtest node + Node API
docker compose up -d

# Wait for healthy
docker compose ps

# Create a node wallet and mine 101 blocks to fund it
curl -X POST http://localhost:5260/api/wallet/create \
  -H "Content-Type: application/json" -d '{"name":"faucet"}'

ADDR=$(curl -s http://localhost:5260/api/wallet/newaddress \
  | grep -o '"address":"[^"]*"' | cut -d'"' -f4)

curl -X POST "http://localhost:5260/api/mining/generate/101" \
  -H "Content-Type: application/json" -d "{\"address\":\"$ADDR\"}"

# Use the faucet to send coins to your wallet address
curl -X POST http://localhost:5260/api/faucet/send \
  -H "Content-Type: application/json" \
  -d '{"address":"YOUR_WALLET_ADDRESS","amount":50}'
```

Then run the wallet UI pointing at the local node:

```bash
dotnet run --project src/DigiByte.Web/DigiByte.Web.csproj
# Open http://localhost:5251
# Go to Settings → Network → Regtest
```

Interactive Node API docs: http://localhost:5260/scalar/v1

### 3. Run with Docker — Testnet

```bash
docker compose -f docker-compose.testnet.yml up -d
# Settings → Network → Testnet
# Get testnet coins from a faucet
```

> **Note:** Testnet initial sync takes several hours.

### 4. Run with Docker — Mainnet (production)

```bash
# ⚠️ Change the RPC password first!
# Edit docker/digibyted/digibyte-mainnet.conf: rpcpassword=YOUR_SECURE_PASSWORD
# Edit docker-compose.mainnet.yml: DigiByteNode__RpcPassword=YOUR_SECURE_PASSWORD

docker compose -f docker-compose.mainnet.yml up -d
```

> **Note:** Mainnet uses a pruned node (`prune=550`). Initial sync takes several hours. Disk usage stabilizes at ~2 GB after pruning completes.

---

## Configuration Reference

### Docker Compose Files

| File | Network | Ports | Pruning | `txindex` | Use Case |
|------|---------|-------|---------|-----------|----------|
| `docker-compose.yml` | Regtest | 18444 (P2P), 18443 (RPC), 5260 (API) | Off | Yes | Local development, instant mining |
| `docker-compose.testnet.yml` | Testnet | 12025 (P2P), 14023 (RPC), 5260 (API) | Off | Yes | Integration testing |
| `docker-compose.mainnet.yml` | Mainnet | 12024 (P2P), 14022 (RPC), 5260 (API) | `prune=550` | No | Production |

### DigiByte Node Configs (`docker/digibyted/`)

| Config File | Network | Key Settings |
|-------------|---------|-------------|
| `digibyte.conf` | Regtest | `regtest=1`, instant mining, no auth required |
| `digibyte-testnet.conf` | Testnet | `testnet=1`, real peers, full blockchain |
| `digibyte-mainnet.conf` | Mainnet | `prune=550`, `dbcache=256`, `maxmempool=50`, `maxconnections=24` |

### Node API Settings (`src/DigiByte.NodeApi/appsettings.json`)

```json
{
  "DigiByteNode": {
    "Host": "127.0.0.1",
    "MainnetPort": 14022,
    "TestnetPort": 14023,
    "RpcUser": "dgbrpc",
    "RpcPassword": "changeme",
    "IsTestnet": true,
    "FaucetEnabled": true,
    "FaucetMaxAmount": 100,
    "FaucetCooldownMinutes": 60
  }
}
```

Override any setting via environment variables in Docker Compose:
```yaml
environment:
  - DigiByteNode__Host=digibyted
  - DigiByteNode__RpcPassword=your_password
  - DigiByteNode__IsTestnet=false
  - DigiByteNode__FaucetEnabled=false
```

### Blockchain Explorer Backends (configured in `src/DigiByte.Web/Program.cs`)

| Priority | Backend | Type | Base URL | Notes |
|----------|---------|------|----------|-------|
| 1 | Esplora | Esplora REST | `digiexplorer.info` | Primary public explorer |
| Last | Own node | RPC via Node API | Configurable | Pruned node, `scantxoutset` for reads |

**Read operations** (balance, UTXOs, history, fees): Explorers in order → Own node → Mock (dev only)
**Write operations** (broadcast tx): Own node first → Explorers in order → Error

Failed explorers enter a 2-minute cooldown before retrying.

### Railway Deployment (`railway.toml`)

Three services for production:
1. `digibyted` — Pruned mainnet node
2. `digibyte-node-api` — REST wrapper for RPC
3. `digibyte-api` — P2P marketplace backend

---

## Pages

| Route | Page | Description |
|-------|------|-------------|
| `/welcome` | Welcome | Onboarding splash — Create or Recover wallet |
| `/create-wallet` | CreateWallet | 3-step flow: Generate mnemonic → Verify 3 words → Set PIN |
| `/recover-wallet` | RecoverWallet | Import via 24-word seed phrase or WIF private key with QR scan |
| `/unlock` | Unlock | PIN entry, forgot PIN (seed reset), delete wallet |
| `/` | Home | Dashboard — balance, fiat conversion, quick contacts, tx list, pull-to-refresh |
| `/send` | Send | Recipient (address/contact/QR), amount (DGB↔fiat toggle), fee selector, review modal |
| `/receive` | Receive | QR code + address display, Legacy/SegWit toggle, copy/share, generate new address |
| `/contacts` | Contacts | CRUD contacts, search, QR scan address, send to contact |
| `/payments` | Payments | Create BIP21 payment requests with QR, copy/share URI |
| `/identity` | Identity | Digi-ID passwordless auth — scan/paste `digiid://` URI, approve domain, sign challenge |
| `/p2p` | P2P Marketplace | Coming soon — buy/sell orders, escrow, trade chat |
| `/remittance` | Remittance | Send by username, fee comparison vs traditional services |
| `/settings` | Settings | Theme, language, network, currency, display mode, backup seed, delete wallet |
| `/backup-seed` | BackupSeed | PIN-protected seed phrase viewer |
| `/about` | About | Version, credits, GitHub contributors, tech stack |
| `/analytics` | Analytics | DGB price, market cap, volume, block height, 7-day chart |
| `/roadmap` | Roadmap | Visual development timeline with status indicators |
| `/deployment` | DeploymentInfo | Infrastructure status and cost breakdown |

### Navigation Structure

```
Bottom Navigation (5 tabs):
  Wallet (/)  |  Pay (/payments)  |  P2P (/p2p)  |  ID (/identity)  |  Settings (/settings)

Pages WITHOUT bottom nav:
  /welcome, /create-wallet, /recover-wallet, /unlock, /about, /roadmap, /analytics, /deployment
```

---

## Node API

The Node API wraps 87 DigiByte Core RPC methods into REST endpoints:

| Group | Endpoints | Examples |
|-------|-----------|---------|
| Blockchain | 9 | `/api/blockchain/info`, `/api/blockchain/height`, `/api/blockchain/block/{hash}` |
| Address | 3 | `/api/address/{addr}/balance`, `/api/address/{addr}/utxos` |
| Transaction | 7 | `/api/tx/{txid}`, `/api/tx/broadcast`, `/api/tx/decode` |
| Wallet | 17 | `/api/wallet/balance`, `/api/wallet/send`, `/api/wallet/newaddress` |
| Network | 9 | `/api/network/info`, `/api/network/fee/{blocks}`, `/api/network/mempool` |
| Faucet | 3 | `/api/faucet/send` (regtest/testnet only), `/api/faucet/balance` |
| Mining | 4 | `/api/mining/generate/{n}`, `/api/mining/difficulty` |
| Keys | 7 | `/api/keys/dump/{addr}`, `/api/keys/import/privkey` |
| PSBT | 4 | `/api/psbt/create`, `/api/psbt/finalize` |
| Descriptors | 3 | `/api/descriptor/derive`, `/api/descriptor/scan` |
| Utility | 9 | `/api/util/verify`, `/api/util/uptime` |

Interactive docs: http://localhost:5260/scalar/v1

---

## Wallet Flow (High Level)

```
1. Create/Recover → Generate 24-word mnemonic or import WIF key
2. Set PIN        → 6-digit PIN encrypts seed with AES-256-GCM (PBKDF2 key derivation)
3. Receive        → Generate SegWit (dgb1...) or Legacy (D...) addresses from HD path
4. Send           → Build + sign transaction client-side with NBitcoin, broadcast to network
5. Track          → Transaction history from explorers + local IndexedDB tracking
```

### Security Model

- **Seed/keys encrypted at rest** in IndexedDB with AES-256-GCM
- **PIN-derived encryption key** via PBKDF2
- **Transaction signing is local** — only signed raw transactions leave the browser
- **AuthGuard component** protects all authenticated pages — redirects to `/unlock` or `/welcome`
- **Digi-ID** uses dedicated derivation path `m/13'/siteIndex'/0'/0`

---

## Testing

```bash
# Run all tests
dotnet test

# Run specific project tests
dotnet test tests/DigiByte.Crypto.Tests/
dotnet test tests/DigiByte.Wallet.Tests/

# Build only (no tests)
dotnet build
```

Test projects use **xUnit** with **coverlet** for code coverage.

### Manual Testing (Regtest)

1. Start Docker regtest: `docker compose up -d`
2. Run the wallet: `dotnet run --project src/DigiByte.Web/DigiByte.Web.csproj`
3. Create a wallet in the UI → Settings → Network → Regtest
4. Copy your wallet address from the Receive page
5. Use the faucet to get coins:
   ```bash
   curl -X POST http://localhost:5260/api/faucet/send \
     -H "Content-Type: application/json" \
     -d '{"address":"YOUR_ADDRESS","amount":50}'
   ```
6. Send a transaction from the Send page
7. Check the Node API docs at http://localhost:5260/scalar/v1

---

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — Design decisions, project structure, blockchain service chain, Docker/deployment
- [Process Flows](docs/PROCESS_FLOWS.md) — Per-page technical flows, user flows, backend operation matrix
- [Roadmap](docs/ROADMAP.md) — Development phases, completed features, planned work
- [Contributing](CONTRIBUTING.md) — How to contribute
- [Changelog](CHANGELOG.md) — Release history
- [Code of Conduct](CODE_OF_CONDUCT.md)

## License

MIT
