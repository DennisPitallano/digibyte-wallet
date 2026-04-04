# Changelog

All notable changes to the DigiByte Wallet project.

## [Unreleased]

### Added
- Address format labels (Legacy/SegWit/P2SH) on contacts and transaction details
- Address format selector on Receive page (Legacy/SegWit toggle)
- Transaction detail modal with explorer link, copy txid, add to contacts
- Scan both Legacy and SegWit addresses for WIF imports
- Forgot PIN recovery (verify seed phrase to reset PIN, or delete wallet)
- Delete Wallet option in Settings (PIN-protected)
- Enhanced PIN input with auto-focus, shake animation on error, unique instance IDs
- WIF private key import (auto-detect network from key prefix)
- QR code scanner (Send, Contacts, Home scan button)
- Loading skeletons for balance and transaction list
- Send success animation (green checkmark overlay)
- Nav hidden on pre-auth pages (Welcome, Unlock, Create, Recover)
- Nav hidden behind modals (CSS :has selector)
- Full-viewport modal backdrop
- Fiat/DGB amount toggle on Send page
- OTP-style 6-digit PIN input on all PIN pages
- Contact book with CRUD, QR scan, search
- Payment requests with BIP21 URI generation
- Digi-ID passwordless authentication
- Remittances with fee comparison widget
- NFC tap-to-pay support (Web NFC API)
- Toast notification system
- Localization (10 languages)
- Transaction tracking (local IndexedDB + explorer fallback)
- Node API (87 RPC methods + Scalar docs)
- Docker setup (regtest with instant mining + testnet)
- Light/dark theme with official DigiByte branding
- Settings persistence to localStorage
- Backup seed phrase (PIN-protected viewing)
- Real transaction signing and broadcasting via NBitcoin
- Mainnet tested with real Guardia wallet import

### Fixed
- Auto-detect network from WIF key prefix on import
- Use detected network for WIF wallet address generation
- Load persisted preferences (network, display mode) at app startup
- Confirmations calculated from currentHeight - blockHeight + 1
- Full-viewport modal backdrop (removed transform from page-enter animation)

## [0.1.0] - 2026-04-03

### Added
- Initial release
- Blazor WASM PWA with DigiByte wallet core
- BIP39/BIP44 HD wallet (24-word mnemonic)
- AES-256-GCM encrypted seed storage in IndexedDB
- Send/Receive with QR codes
- DigiByte network definitions (mainnet, testnet, regtest)
