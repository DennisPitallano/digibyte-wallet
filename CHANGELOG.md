# Changelog

All notable changes to the DigiByte Wallet project.

## [Unreleased]

### Added
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

### Fixed- **bad-witness-nonstandard on send** \u2014 HD wallet UTXO path now queries each address individually; correct scriptPubKey (legacy vs segwit) per UTXO
- **Send page spinners** \u2014 Review Transaction and Send Now buttons show loader immediately (`Task.Yield` forces render before async work)- **MemoryCacheService TryGet** — Value types (decimal 0) no longer treated as cache hits
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
