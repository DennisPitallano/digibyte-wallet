# Changelog

All notable changes to the DigiByte Wallet project.

## [Unreleased]

## [0.7.0-beta.1] - 2026-04-12

### Added
- **Per-wallet spending limits** — Configurable daily (24h), weekly (7d), and monthly (30d) rolling DGB limits; settings stored per-wallet in IndexedDB (`spending_limit_` prefix)
- **Spending limit enforcement on send** — `PreviewSend()` checks all enabled limits; hard block prevents confirm modal, soft warn shows amber banner in review
- **Spending limit settings page** (`/settings/spending-limits`) — PIN-gated controls: enable/disable toggle, DGB limit inputs with period badges, alert threshold selector (60/70/80/90%), hard block toggle, live spending progress bars
- **Settings nav card** — "Spending Limits" card (red shield icon) in Settings page; hidden for watch-only (xpub) wallets
- **Alert threshold warnings** — Amber warning banner in send confirm modal when approaching configurable percentage of any limit
- **Spending limit cleanup** — Limit settings deleted on wallet delete alongside biometric data
- **17 new unit tests** — SpendingLimitServiceTests covering settings persistence, roundtrip, corrupt JSON, period calculations, threshold warnings, hard block, multi-period worst-result, zero-limit skip, batch amounts, per-wallet isolation

### Changed
- **Help Center updated** — New "Spending Limits" Q&A section with 4 entries covering setup, enforcement, and per-wallet behavior
- **Roadmap updated** — "Spending limits / transaction alerts" marked as completed
- **Process flows updated** — Send page flow updated with spending limit check step; new SpendingLimits page flow added

## [0.6.0-beta.1] - 2026-04-14

### Added
- **Recovery phrase masked input** — Seed phrase is hidden by default (CSS `-webkit-text-security: disc` for textarea, `type="password"` for word inputs); eye toggle to show/hide
- **Word-by-word input mode** — 3-column numbered grid for entering recovery phrase one word at a time; mode toggle transfers data between Paste All and Word by Word
- **BIP39 auto-suggest** — Dropdown shows up to 6 prefix-matched BIP39 words as you type; keyboard navigation (Arrow Up/Down, Enter/Tab to accept, Escape to dismiss); privacy mode shows first 2 chars + dots when hidden
- **Multi-separator paste** — Word inputs auto-split on spaces, commas, newlines, and tabs; pasting a full phrase into a single field distributes words across all fields
- **12/24 word count toggle** — Switch between 12 and 24 word grids; auto-expands to 24 if more than 12 words are pasted
- **Word validation** — Non-BIP39 words get red borders via binary search against the 2048-word list
- **`MnemonicGenerator.GetWordList()`** — New public API exposing the BIP39 English wordlist from NBitcoin
- **Donation page USD conversion** — Live DGB/USD price from CoinGecko via `IBlockchainService.GetDgbPriceAsync()` on Deployment and Donation History pages
- **Copy donation address** — One-tap clipboard copy with "📋 Copy Address" → "✅ Copied!" feedback animation
- **Donation data caching** — 5-minute TTL via `MemoryCacheService` for donation balance, transactions, and price (`donation:balance`, `donation:txs`, `donation:price` keys)
- **Donation refresh buttons** — Spinning arrow buttons that clear donation cache prefix and force re-fetch
- **Donation history USD values** — Each transaction on `/donations` shows its USD equivalent at the current price
- **3 new unit tests** — MnemonicGenerator wordlist tests (count, valid words, sorted order)

### Changed
- **Donation pages use BlockchainService** — Refactored from direct `IHttpClientFactory` HTTP calls to `FallbackBlockchainService` with proper fallback chain
- **Mainnet-forced donation queries** — Donation pages temporarily switch to mainnet via `SetNetworkMode()` regardless of current network setting, restoring original mode in `finally` block
- **Roadmap updated** — New "Donation Pages & Recovery UX" and "Biometric & Batch Send" milestones marked Done; security enhancements and advanced features updated
- **Help Center updated** — New Q&A for wallet recovery input modes and donation page features (14 sections total)
- **Process flows updated** — RecoverWallet section rewritten to document dual input modes, auto-suggest, and masking; Deployment section updated with donation/history page descriptions

### Fixed
- **ScriptPubKey matching on donation history** — Transactions matched by `output.ScriptHex` against the donation address's scriptPubKey for accurate filtering

## [0.5.0-beta.1] - 2026-04-11

### Added
- **Biometric unlock (WebAuthn PRF)** — Unlock your wallet with fingerprint or face recognition using the WebAuthn PRF extension; per-wallet enrollment with AES-256-GCM wrapping key derived from platform authenticator
- **Biometric enrollment prompt** — After first successful PIN unlock, a modal offers to enable biometric; "Not Now" dismisses per-wallet; can be enabled later from Settings
- **Confirm sends with biometric** — Optional setting to require biometric verification (WebAuthn assertion) before broadcasting any transaction; toggle in Settings under Biometric Unlock
- **Biometric auto-trigger on unlock** — When biometric is enrolled, the unlock screen automatically triggers biometric authentication on page load
- **Settings biometric toggle** — Enable/disable biometric unlock from Settings with PIN verification for enrollment and confirmation for disable
- **Biometric cleanup on wallet delete** — Disabling or deleting a wallet removes all biometric keys (credential, wrapping key, encrypted seed, preferences)
- **19 new unit tests** — BiometricServiceTests covering enabled/dismissed/confirm-send preferences, disable cleanup, per-wallet isolation, JS interop error handling, and identity verification

### Changed
- **Version bumped to 0.5.0-beta.1** — Biometric unlock and send confirmation feature complete
- **Production log suppression** — HTTP/Polly info logs suppressed only in production; Development keeps info-level logging
- **Font preload removed** — Removed unused `<link rel="preload">` for Inter font; `@font-face` with `font-display: swap` handles on-demand loading
- **Roadmap updated** — Biometric Unlock and Batch Send UI marked as completed
- **Help Center updated** — New Biometric & Security section with real-life scenarios, security model explanation, and confirm-send documentation

### Fixed
- **Biometric enrollment prompt race condition** — Prompt no longer shows when biometric is already enabled; `UnlockAsync` now re-checks `IsEnabledAsync` directly instead of relying on cached field from `OnAfterRenderAsync`
- **JS interop timing in Blazor WASM** — Biometric detection moved to `OnAfterRenderAsync(firstRender)` since JS interop is unavailable during `OnInitializedAsync`
- **Global scope collision** — `webauthn.js` wrapped in IIFE to prevent `const IV_SIZE` conflict with `crypto.js`
- **Font preload browser warning** — Removed preload tag that triggered "preloaded but not used" console warning

### Security
- **No secrets at rest** — Biometric wrapping key is derived from the platform authenticator via WebAuthn PRF; only encrypted blobs stored in IndexedDB
- **Per-wallet credentials** — Each wallet gets its own WebAuthn credential; disabling biometric for one wallet does not affect others
- **Identity-only send confirmation** — Send confirmation uses a simple WebAuthn assertion (no PRF/decryption), verifying only that the authenticated user is present

## [0.4.0-beta.1] - 2026-04-11

### Added
- **Multi-wallet support** — Create and manage up to 10 wallets (HD, WIF, Watch-Only) per device with independent PIN unlock per session
- **Wallet switcher** — Swipe left/right on the Home balance card to switch wallets instantly; wallet picker bottom sheet with swipe-to-dismiss
- **Stacked card carousel** — Layered card visual on Home showing wallet deck with colored ghost cards behind the active wallet
- **Wallet color theming** — 10-color palette with per-wallet color; color picker during creation and in wallet management
- **Wallet management page** — Rename, change color, delete, and backup wallets from Settings → Manage Wallets
- **Wallet picker sheet** — Bottom sheet overlay listing all wallets with color dots, type badges, "Create New Wallet" button
- **Per-wallet scoped cache** — Balance, transactions, and fetch timestamps are now scoped per wallet ID; price remains global
- **Enhanced unlock screen** — Wallet name shown in frosted-glass pill badge with color dot and glow effect
- **Recovery page wallet info** — Active wallet info card showing name, type, and color on the Recovery Tools page
- **`ChangeWalletColorAsync`** — New service method to update wallet color with persistence and in-memory session sync
- **19 new unit tests** — MultiWalletTests covering creation, switching, rename, color change, deletion, palette, and mixed wallet types

### Changed
- **Version bumped to 0.4.0-beta.1** — Multi-wallet feature complete
- **Cache freshness increased** — Stale threshold raised from 2 to 5 minutes; in-memory TTL from 5 to 10 minutes
- **Touch handlers enhanced** — Horizontal swipe detection (wallet switch) coexists with vertical pull-to-refresh
- **Roadmap updated** — Multi-Wallet Support milestone marked Done; removed from Q2 planned
- **Help Center updated** — 4 new Q&As for multi-wallet, wallet switching, color, and rename
- **Welcome page** — Footer links/version hidden in add-wallet mode for cleaner UI

### Fixed
- **Zero-balance wallet always re-fetched** — Cache required balance > 0 to count as cached; now any persisted balance is valid
- **Global in-memory timestamp** — `lastFetchTime` was shared across all wallets causing false fresh/stale decisions; now wallet-scoped
- **Removed BIP44→BIP84 migration banner** — Legacy fund sweep banner removed from Home; recovery tools handle this instead

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
