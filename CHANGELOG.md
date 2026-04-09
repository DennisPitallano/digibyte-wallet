# Changelog

All notable changes to the DigiByte Wallet project.

## [Unreleased]

### Fixed
- **BIP84 derivation path** — Changed HD key derivation from BIP44 (`m/44'/20'/...`) to BIP84 (`m/84'/20'/...`) for native SegWit addresses. Addresses now match standard BIP84-compatible tools (Ian Coleman, Ledger, Trezor). Fixes [#1](https://github.com/DennisPitallano/digibyte-wallet/issues/1)
- **Cache clearing broke transaction loading** — `fetchFailed` flag was based on empty data instead of actual HTTP errors; replaced with separate `balanceFetchFailed`/`txFetchFailed` flags

### Added
- **HD derivation unit tests** — BIP84 path verification, address format, determinism, watch-only, and regression tests in `DigiByte.Crypto.Tests`
- **BIP44→BIP84 fund sweep migration** — Automatically scans old BIP44 derivation path addresses for stranded funds on Home page load. Displays migration banner with balance and "Move to wallet" button that sweeps all UTXOs to the current BIP84 SegWit address in a single transaction. Includes loading indicator during scan and sweep processing. Auto-expires after May 10, 2026 to avoid unnecessary API calls once all users have migrated.
- **Legacy address toggle removed for HD wallets** — Receive page now only shows SegWit addresses for HD wallets (WIF wallets retain the Legacy/SegWit toggle)

### Changed
- Updated all documentation (README, ARCHITECTURE, COMPLIANCE, SECURITY, PROCESS_FLOWS, ROADMAP) to reflect BIP84 derivation
- Updated Home page wallet info to show BIP84 path and description
- Updated Roadmap page milestone text

## [0.3.0-beta.1] - 2026-04-07

### Added
- **Real-time multisig rooms** — SignalR-based collaborative multisig wallet creation with invite codes, participant tracking, ready system, auto-expiry (15 min), and deep link support
- **Multisig backup/restore** — Download redeem script as `.txt` backup file with wallet metadata, co-signers, and recovery instructions
- **Import from backup file** — Upload `.txt` backup on MultisigImport page; auto-parses wallet name and redeem script
- **Backup warning** — Amber warning banner on MultisigDetail page explaining seed phrases cannot recover multisig wallets
- **VersionBadge component** — Shared tappable version pill badge used across 5 pages (Welcome, About, CreateWallet, Settings, Unlock)
- **Version Info modal** — Tappable modal with app details, channel, platform, framework, and What's New section; rendered at layout level via static event pattern
- **Real-life scenario** — Maya/Leo/Priya vacation fund walkthrough in Multisig Guide real-time collaboration section
- **Security documentation** — Comprehensive `docs/SECURITY.md` with threat model, OWASP Top 10 mapping, risk matrix, cryptographic primitives, and hardening recommendations
- **Video tutorial** — Real-time multisig creation recording (`create-multi-sigg.mp4` + GIF) added to README

### Changed
- **Version bumped to 0.3.0-beta.1** — Promoted from alpha to beta; all core features functional, documented, and tested
- **Roadmap updated** — Added "Real-Time Multisig Rooms" and "UI Enhancements" as completed milestones
- **Help Center updated** — New Q&As for multisig rooms, real-time creation, and recovery warnings
- **Multisig Guide updated** — New "Real-Time Collaboration" expandable section with step-by-step walkthrough
- **Delete wallet help** — Updated to mention backing up multisig redeem scripts before deletion

### Fixed
- **Version Info modal positioning** — Fixed CSS `transform` containment issue (animations creating containing blocks that break `position: fixed`); modal now rendered at layout level outside page transforms

## [0.2.0-alpha.1] - 2026-04-06

### Added
- **Video tutorials** — Recorded walkthroughs in `docs/media/`: How to Install PWA, How to Recover/Import Wallet, How to Send DigiByte
- **iOS PWA install banner** — Detects iOS Safari and shows manual "Add to Home Screen" instructions (since `beforeinstallprompt` is Chromium-only)
- **Route-aware install banner** — Banner always shows on Welcome, Recover, and Watch-Only pages regardless of dismiss state
- **CoinGecko price proxy** \u2014 Server-side proxy endpoints (`/api/price/simple`, `/api/price/coin`) with 60s IMemoryCache TTL; avoids browser CORS and 429 rate limits
- **Railway deployment** \u2014 Dockerfiles for API (multi-stage .NET 10) and Web (Blazor WASM + Nginx), ForwardedHeaders, config-driven CORS
- **PWA install banner** \u2014 Native `beforeinstallprompt` capture with styled top banner (session-scoped dismiss)
- **Donation balance** \u2014 Live DGB balance shown on Deployment & Support page
- **Config-driven network defaults** \u2014 `DefaultNetwork` in appsettings (mainnet prod, testnet dev); localStorage overrides
- **Configurable fee rates** \u2014 Send page fee selection (Low 100/Normal 200/Fast 400 sat/vB) now controls actual transaction fees
- **Global error boundary** — Styled crash recovery screen with "Go Home" button; auto-recovers on navigation
- **PIN lockout protection** — Exponential backoff after 3 failed attempts (15s → 30s → 60s → 120s)
- **Multi-currency fiat** — USD ($), EUR (€), GBP (£), PHP (₱), JPY (¥) with proper currency symbols
- **Currency change cache invalidation** — Changing currency clears price caches and forces re-fetch
- **Self-hosted assets** — Tailwind CSS (purged ~37KB), Inter font (woff2), jsQR — all CSP compliant
- **DigiByte favicon/icons** — SVG favicon, 192px/512px PNG icons using official DGB logo
- **Version and tagline** — "Speed · Security · Scalability" footer on Unlock and CreateWallet pages
- **Polly resilience** — Per-client retry, circuit breaker, timeout (Blockchain 15s/45s, NodeApi 8s/20s)
- **Two-tier caching** — MemoryCacheService (TTL + dedup) + IndexedDB persistent cache
- **Production log levels** — HTTP/Polly at Warning; Development overrides to Information
- **Send page loading states** — Skeleton loaders for balance/price, form disabled during fetch
- **Amount warnings** — Real-time inline warnings for exceeding balance and below minimum
- **Developer tools guard** — Cache clear button only in Development environment
- **Recovery Options close button** — X button to dismiss Forgot PIN panel
- **Language coming soon modal** — Placeholder until multi-language is complete
- Address format labels (Legacy/SegWit/P2SH) on contacts and transaction details
- Address format selector on Receive page (Legacy/SegWit toggle)
- Transaction detail modal with explorer link, copy txid, add to contacts
- Forgot PIN recovery (verify seed phrase to reset PIN, or delete wallet)
- Delete Wallet option in Settings (PIN-protected, clears all caches)
- QR code scanner (Send, Contacts, Home scan button)
- OTP-style 6-digit PIN input on all PIN pages
- Contact book with CRUD, QR scan, search
- Payment requests with BIP21 URI generation
- Digi-ID passwordless authentication
- Toast notification system
- Transaction tracking (local IndexedDB + explorer fallback)
- Node API (87 RPC methods + Scalar docs)
- Docker setup (regtest, testnet, mainnet pruned)
- Light/dark theme with official DigiByte branding
- Settings persistence to localStorage
- Backup seed phrase (PIN-protected viewing)
- Real transaction signing and broadcasting via NBitcoin

### Fixed
- **too-long-mempool-chain broadcast error** — Blockbook fallback + confirmed-only UTXO filtering
- **bad-witness-nonstandard on send** \u2014 HD wallet UTXO path now queries each address individually; correct scriptPubKey (legacy vs segwit) per UTXO
- **Send page spinners**  \u2014 Review Transaction and Send Now buttons show loader immediately (`Task.Yield` forces render before async work)
- **MemoryCacheService TryGet** — Value types (decimal 0) no longer treated as cache hits
- **CoinGecko $0.00 price** — Services throw instead of returning 0, enabling fallback chain
- **IndexedDB price cache** — No longer loads/saves 0 values
- **Home page empty data** — Distinguishes network error from empty wallet
- **Currency not persisted** — Now saved to localStorage on change
- **Stale cache after currency change** — Clears fetch timestamp so Home re-fetches
- **Blazor input re-render** — `@bind:after` prevents cursor jumps on Send amount warnings
- Auto-detect network from WIF key prefix on import
- Load persisted preferences at app startup
- Confirmations calculated correctly (currentHeight - blockHeight + 1)

## [0.1.0] - 2026-04-03

### Added
- Initial release
- Blazor WASM PWA with DigiByte wallet core
- BIP39/BIP44 HD wallet (24-word mnemonic)
- AES-256-GCM encrypted seed storage in IndexedDB
- Send/Receive with QR codes
- DigiByte network definitions (mainnet, testnet, regtest)
