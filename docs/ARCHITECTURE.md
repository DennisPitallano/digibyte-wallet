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
          ┌──────────────┴──────────────┐
          │                             │
    ┌─────┴──────┐              ┌───────┴────────┐
    │  Node API  │              │  digiexplorer   │
    │ (Docker)   │              │    .info        │
    │ Port 5260  │              │  (Public API)   │
    └─────┬──────┘              └────────────────┘
          │
    ┌─────┴──────┐
    │ digibyted  │
    │ (Docker)   │
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
  ├── NodeApiBlockchainService  (your own node)
  ├── BlockchainApiService      (digiexplorer.info Esplora API)
  └── MockBlockchainService     (demo data)
```
For reads: Explorer first (has address indexing), then Node API, then mock.
For writes: Node API first (direct broadcast), then Explorer.

### Multi-Network Support
- Mainnet: `dgb1...` (SegWit), `D...` (Legacy)
- Testnet: `dgbt1...`, `t...`
- Regtest: `dgbrt1...`
- Network auto-detected from WIF key prefix on import

### Wallet Types
- **HD Wallet**: BIP39 mnemonic, BIP44 derivation `m/44'/20'/0'/change/index`
- **Private Key Wallet**: Single WIF key import, supports both Legacy and SegWit addresses
