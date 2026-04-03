# DigiByte Wallet

A self-custodial DigiByte (DGB) wallet built as a Progressive Web App (PWA) with Blazor WebAssembly (.NET 10).

## Features

- **Self-custodial** - Keys encrypted in browser IndexedDB, never sent to any server
- **BIP39/BIP44 HD wallet** - 24-word mnemonic, `m/44'/20'/account'/change/index`
- **Send & Receive** - Real transaction building, signing, and broadcasting via NBitcoin
- **QR Scanner** - Scan addresses and `digibyte:` payment URIs
- **Contacts** - Save addresses with names, send with one tap
- **Payment Requests** - Generate BIP21 URIs with amount/label/message
- **Digi-ID** - Passwordless authentication protocol
- **Light & Dark Mode** - Official DigiByte branding with theme toggle
- **Multi-network** - Mainnet, Testnet, and Regtest support
- **10 Languages** - English, Spanish, Chinese, Japanese, Korean, Filipino, Hindi, Arabic, Portuguese, French
- **OTP-style PIN** - 6-digit PIN with individual digit boxes
- **Node API** - 87 RPC methods wrapped as REST endpoints with Scalar docs
- **Docker** - One-command regtest/testnet setup with instant mining

## Architecture

```
digibyte-wallet/
+-- src/
|   +-- DigiByte.Crypto/          # BIP39, BIP44, HD keys, tx building, Digi-ID
|   +-- DigiByte.Wallet/          # Wallet service, encryption, contacts, storage
|   +-- DigiByte.Web/             # Blazor WASM PWA (the wallet UI)
|   +-- DigiByte.Api/             # P2P marketplace backend API
|   +-- DigiByte.NodeApi/         # Node RPC wrapper (87 methods + Scalar)
|   +-- DigiByte.P2P.Shared/      # Shared models for P2P exchange
+-- tests/
+-- docker/
|   +-- digibyted/                # DigiByte Core Docker image
|   +-- node-api/                 # Node API Docker image
+-- docker-compose.yml            # Regtest (instant mining)
+-- docker-compose.testnet.yml    # Testnet (real network)
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
| Real-time | SignalR (for P2P chat) |

## Quick Start

### Prerequisites
- .NET 10 SDK
- Docker (for regtest/testnet node)

### Run the wallet (development)
```bash
dotnet run --project src/DigiByte.Web/DigiByte.Web.csproj
# Open http://localhost:5251
```

### Run with Docker (regtest + Node API)
```bash
# Start DigiByte regtest node + Node API
docker compose up -d

# Mine coins (instant on regtest)
curl -X POST http://localhost:5260/api/wallet/create \
  -H "Content-Type: application/json" -d '{"name":"faucet"}'

ADDR=$(curl -s http://localhost:5260/api/wallet/newaddress | grep -o '"address":"[^"]*"' | cut -d'"' -f4)
curl -X POST "http://localhost:5260/api/mining/generate/101" \
  -H "Content-Type: application/json" -d "{\"address\":\"$ADDR\"}"

# Node API docs
open http://localhost:5260/scalar/v1
```

### Run with testnet
```bash
docker compose -f docker-compose.testnet.yml up -d
```

## Node API

The Node API wraps all 87 DigiByte Core RPC methods into REST endpoints:

| Group | Endpoints | Examples |
|-------|-----------|---------|
| Blockchain | 9 | `/api/blockchain/info`, `/api/blockchain/height`, `/api/blockchain/block/{hash}` |
| Address | 3 | `/api/address/{addr}/balance`, `/api/address/{addr}/utxos` |
| Transaction | 7 | `/api/tx/{txid}`, `/api/tx/broadcast`, `/api/tx/decode` |
| Wallet | 17 | `/api/wallet/balance`, `/api/wallet/send`, `/api/wallet/newaddress` |
| Network | 9 | `/api/network/info`, `/api/network/fee/{blocks}`, `/api/network/mempool` |
| Faucet | 3 | `/api/faucet/send` (regtest only), `/api/faucet/balance` |
| Mining | 4 | `/api/mining/generate/{n}`, `/api/mining/difficulty` |
| Keys | 7 | `/api/keys/dump/{addr}`, `/api/keys/import/privkey` |
| PSBT | 4 | `/api/psbt/create`, `/api/psbt/finalize` |
| Descriptors | 3 | `/api/descriptor/derive`, `/api/descriptor/scan` |
| Utility | 9 | `/api/util/verify`, `/api/util/uptime` |

Interactive API docs at `http://localhost:5260/scalar/v1`

## Wallet Flow

1. **Create/Recover** - Generate 24-word mnemonic or import existing
2. **Set PIN** - 6-digit OTP-style PIN (AES-256-GCM encrypts seed)
3. **Receive** - Generate SegWit addresses (`dgb1...` / `dgbt1...` / `dgbrt1...`)
4. **Send** - Build + sign tx client-side, broadcast via Node API
5. **Track** - Transaction history with local tracking + explorer fallback

## Blockchain Service Fallback

The wallet uses a cascading fallback for blockchain data:

```
1. Node API (localhost:5260)     -- your own node
2. digiexplorer.info             -- public Esplora explorer
3. Mock demo data                -- for UI development
```

## Tests

```bash
dotnet test  # 30 tests covering crypto, HD keys, addresses, Digi-ID, payment URIs
```

## License

MIT
