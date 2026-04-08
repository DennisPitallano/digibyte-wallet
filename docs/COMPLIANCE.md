# DigiByte Protocol Compliance

> **Version:** 0.3.0-beta.2 | **Last Updated:** April 2026

This document details the wallet's compliance with DigiByte protocol standards (BIPs), network parameters, address formats, and regulatory considerations. It is intended for technical reviewers, auditors, and developers evaluating interoperability with the DigiByte network.

---

## Table of Contents

- [BIP Compliance Matrix](#bip-compliance-matrix)
- [Network Parameters](#network-parameters)
- [Address Format Compliance](#address-format-compliance)
- [HD Wallet Derivation](#hd-wallet-derivation)
- [Transaction Standards](#transaction-standards)
- [Multisig Standards](#multisig-standards)
- [PSBT Compliance](#psbt-compliance)
- [URI Scheme Compliance](#uri-scheme-compliance)
- [Digi-ID Protocol](#digi-id-protocol)
- [SegWit Support](#segwit-support)
- [Not Implemented (Out of Scope)](#not-implemented-out-of-scope)
- [Regulatory Considerations](#regulatory-considerations)
- [Verification & Testing](#verification--testing)
- [References](#references)

---

## BIP Compliance Matrix

The following table lists all BIPs relevant to a DigiByte light wallet and our implementation status:

| BIP | Name | Status | Implementation |
|-----|------|--------|----------------|
| **BIP 11** | M-of-N Multisig | ✅ Implemented | Standard multisig outputs via NBitcoin; supports 2–7 signers |
| **BIP 13** | P2SH Address Format | ✅ Implemented | Script address prefix `0x3F` (mainnet, "S" prefix), `0x8C` (testnet) |
| **BIP 16** | Pay-to-Script-Hash | ✅ Implemented | P2SH wrapping for multisig (P2SH-P2WSH) |
| **BIP 21** | URI Scheme | ✅ Implemented | `digibyte:` URI format for payment requests with amount, label, message params |
| **BIP 32** | HD Wallets | ✅ Implemented | Hierarchical Deterministic key derivation via NBitcoin `ExtKey`/`ExtPubKey` |
| **BIP 39** | Mnemonic Seed | ✅ Implemented | 24-word English mnemonic; 256-bit entropy; PBKDF2-HMAC-SHA512 seed derivation |
| **BIP 43** | Purpose Field | ✅ Implemented | Purpose `44'` for standard derivation |
| **BIP 44** | Multi-Account HD | ✅ Implemented | Path `m/44'/20'/0'/change/index`; coin type 20 per SLIP-44 registration |
| **BIP 67** | Deterministic Key Sorting | ✅ Implemented | Lexicographic sorting of public keys in multisig redeem scripts |
| **BIP 141** | SegWit (Consensus) | ✅ Implemented | Witness program version 0; `SupportSegwit = true` in network consensus |
| **BIP 143** | SegWit Signing | ✅ Implemented | BIP143 transaction digest algorithm for SegWit inputs via NBitcoin |
| **BIP 144** | SegWit Serialization | ✅ Implemented | Witness serialization format handled by NBitcoin |
| **BIP 173** | Bech32 Addresses | ✅ Implemented | `dgb1...` (mainnet), `dgbt1...` (testnet), `dgbrt1...` (regtest) |
| **BIP 174** | PSBT | ✅ Implemented | Create, sign, combine, finalize, extract, and broadcast PSBTs |

---

## Network Parameters

### Mainnet

| Parameter | Value | Source |
|-----------|-------|--------|
| P2PKH Prefix | `0x1E` → addresses start with `D` | `DigiByteNetwork.cs` |
| P2SH Prefix | `0x3F` → addresses start with `S` | `DigiByteNetwork.cs` |
| WIF Prefix | `0x80` | `DigiByteNetwork.cs` |
| Extended Public Key | `0x0488B21E` (xpub) | `DigiByteNetwork.cs` |
| Extended Secret Key | `0x0488ADE4` (xprv) | `DigiByteNetwork.cs` |
| Bech32 HRP | `dgb` | `DigiByteNetwork.cs` |
| P2P Port | 12024 | `DigiByteNetwork.cs` |
| RPC Port | 14022 | `DigiByteNetwork.cs` |
| Network Magic | `0xDAB6C3FA` | `DigiByteNetwork.cs` |
| Block Time | 15 seconds | Consensus config |
| Halving Interval | 1,050,000 blocks | Consensus config |
| Coin Type (SLIP-44) | `20` | HD derivation path |

### Testnet

| Parameter | Value |
|-----------|-------|
| P2PKH Prefix | `0x7E` |
| P2SH Prefix | `0x8C` |
| WIF Prefix | `0xFE` |
| Extended Public Key | `0x04358CF7` |
| Extended Secret Key | `0x04358394` |
| Bech32 HRP | `dgbt` |

### Regtest

| Parameter | Value |
|-----------|-------|
| P2PKH Prefix | `0x7E` |
| P2SH Prefix | `0x8C` |
| WIF Prefix | `0xFE` |
| Extended Public Key | `0x04358CF7` |
| Extended Secret Key | `0x04358394` |
| Bech32 HRP | `dgbrt` |

> **Verification**: All network parameters are registered via NBitcoin's `NetworkBuilder` in `DigiByte.Crypto/Networks/DigiByteNetwork.cs` and validated against the DigiByte Core source (`chainparams.cpp`).

---

## Address Format Compliance

The wallet supports all standard DigiByte address formats:

| Format | BIP | Example Prefix | Usage |
|--------|-----|----------------|-------|
| **P2PKH** (Legacy) | — | `D...` (mainnet) | Single-key legacy addresses |
| **P2SH** | BIP 13/16 | `S...` (mainnet) | Script hash (multisig wrapping) |
| **P2WPKH** (Native SegWit) | BIP 141/173 | `dgb1q...` | Single-key SegWit (default for new wallets) |
| **P2SH-P2WPKH** (Nested SegWit) | BIP 141 | `S...` | SegWit wrapped in P2SH (backward-compatible) |
| **P2WSH** (SegWit Script) | BIP 141/173 | `dgb1q...` | Native SegWit multisig |
| **P2SH-P2WSH** (Nested Script) | BIP 16/141 | `S...` | Multisig default (maximum compatibility) |

### Address Validation

- `AddressValidator.cs` validates all formats using NBitcoin's `BitcoinAddress.Create()` with the correct network
- Rejects addresses from wrong networks (e.g., testnet address on mainnet)
- Supports both base58check (P2PKH, P2SH) and bech32 (P2WPKH, P2WSH) decoding

---

## HD Wallet Derivation

### Standard Derivation (BIP 44)

```
m / 44' / 20' / 0' / change / index
 │    │     │     │      │       └── Address index (0, 1, 2, ...)
 │    │     │     │      └────────── 0 = receiving, 1 = change
 │    │     │     └───────────────── Account (always 0)
 │    │     └─────────────────────── Coin type 20 (DGB per SLIP-44)
 │    └───────────────────────────── Purpose 44 (BIP 44)
 └────────────────────────────────── Master key
```

| Component | Value | Standard |
|-----------|-------|----------|
| Purpose | `44'` | BIP 43 |
| Coin Type | `20'` | SLIP-44 (DigiByte registered) |
| Account | `0'` | BIP 44 |
| Change | `0` (external) / `1` (internal) | BIP 44 |
| Index | Sequential from `0` | BIP 44 |

### Mnemonic Generation (BIP 39)

- **Entropy**: 256 bits (cryptographically random via `RandomNumberGenerator`)
- **Word Count**: 24 words from the English BIP39 wordlist (2048 words)
- **Checksum**: 8 bits (SHA-256 of entropy)
- **Seed Derivation**: PBKDF2-HMAC-SHA512, 2048 iterations, mnemonic as password, `"mnemonic"` as salt
- **Compatibility**: Seeds generated by this wallet can be imported into any BIP39/BIP44 compatible wallet using coin type 20

### Digi-ID Derivation

Digi-ID uses a separate derivation path per site (not BIP 44):

```
m / 13' / site_index' / 0' / 0
```

This ensures wallet keys and Digi-ID keys never overlap.

---

## Transaction Standards

### Transaction Building

| Feature | Standard | Implementation |
|---------|----------|----------------|
| Input signing | ECDSA secp256k1 | NBitcoin `TransactionBuilder.SignTransaction()` |
| SegWit digest | BIP 143 | Automatic for SegWit inputs |
| Output encoding | P2PKH, P2SH, P2WPKH, P2WSH | NBitcoin address parsing |
| Fee estimation | sat/vB | Configurable: Low (100), Normal (200), Fast (400) |
| Change outputs | Automatic | `TransactionBuilder.SetChange()` to sender's address |
| Dust threshold | 546 satoshis | NBitcoin default for standard outputs |

### UTXO Selection

- **Strategy**: Largest-first selection from confirmed UTXOs
- **Confirmed only**: Unconfirmed UTXOs are excluded to avoid `too-long-mempool-chain` errors
- **Excess check**: Warns users if amount exceeds available balance

### Transaction Broadcasting

Transactions are broadcast through the fallback chain:
1. Own node (NodeApi) → 2. Blockbook explorers → 3. Esplora explorers

---

## Multisig Standards

### Redeem Script Construction (BIP 67)

1. Collect compressed public keys from all co-signers (33 bytes each)
2. Sort keys **lexicographically** (byte-order ascending) per BIP 67
3. Construct `OP_M <key1> <key2> ... <keyN> OP_N OP_CHECKMULTISIG`
4. Wrap in P2SH-P2WSH (default) or P2WSH for the address

### Supported Configurations

| Parameter | Range | Notes |
|-----------|-------|-------|
| Total signers (N) | 2–7 | Upper bound chosen for mobile UX |
| Required signatures (M) | 1–N | Must be ≤ N |
| Key format | Compressed public key (33 bytes hex) | Uncompressed keys rejected |
| Address type | P2SH-P2WSH (default), P2WSH | Configurable at creation |

### Deterministic Address Generation

Given the same set of public keys and threshold:
- BIP 67 sorting guarantees the same redeem script regardless of input order
- Same redeem script → same address on all co-signers' devices
- Verified by 26 unit tests in `MultisigServiceTests.cs`

---

## PSBT Compliance

### BIP 174: Partially Signed Bitcoin Transactions

The wallet implements the full PSBT lifecycle:

| Operation | Method | Description |
|-----------|--------|-------------|
| **Create** | `CreatePSBT()` | Creates unsigned PSBT with inputs, outputs, and UTXO data |
| **Sign** | `SignPSBT()` | Adds ECDSA signature for the local co-signer's key |
| **Combine** | `CombinePSBT()` | Merges signatures from multiple co-signers |
| **Finalize** | `FinalizePSBT()` | Assembles scriptSig/witness when threshold is met |
| **Extract** | `ExtractTransaction()` | Extracts the final raw transaction |
| **Broadcast** | `BroadcastTransactionAsync()` | Sends to the network |

### PSBT Serialization

- Format: Base64-encoded (standard BIP 174 format)
- Interoperable with DigiByte Core's `createpsbt`, `walletprocesspsbt`, `combinepsbt`, `finalizepsbt` RPCs
- Tested against DigiByte Core v8.26.2

---

## URI Scheme Compliance

### BIP 21: DigiByte URI Format

```
digibyte:<address>?amount=<value>&label=<label>&message=<message>
```

| Parameter | Required | Description |
|-----------|----------|-------------|
| `address` | Yes | Any valid DigiByte address (P2PKH, P2SH, bech32) |
| `amount` | No | Amount in DGB (decimal) |
| `label` | No | Label for the address (URL-encoded) |
| `message` | No | Description of the payment (URL-encoded) |

- **QR Scanner**: Parses `digibyte:` URIs from QR codes and auto-fills send form
- **Payment Requests**: Generates BIP 21 URIs with QR codes for the receive page

---

## Digi-ID Protocol

Digi-ID is a DigiByte-specific authentication protocol (not a BIP):

| Aspect | Detail |
|--------|--------|
| **URI Scheme** | `digiid://` |
| **Key Derivation** | `m/13'/siteIndex'/0'/0` (site-isolated) |
| **Signing** | ECDSA secp256k1 signature of the callback URI |
| **Replay Protection** | Nonce included in the URI by the server |
| **Privacy** | Each site gets a unique derived key; main wallet keys never exposed |
| **Callback** | Signed response POSTed to the callback URL over HTTPS |

---

## SegWit Support

### Implemented SegWit Features

| Feature | BIP | Status |
|---------|-----|--------|
| Witness program v0 | BIP 141 | ✅ Supported |
| P2WPKH (native) | BIP 141 | ✅ Default for single-key |
| P2WSH (native script) | BIP 141 | ✅ Available for multisig |
| P2SH-P2WPKH (nested) | BIP 141 | ✅ Supported |
| P2SH-P2WSH (nested script) | BIP 141 | ✅ Default for multisig |
| Bech32 encoding | BIP 173 | ✅ `dgb1...` / `dgbt1...` |
| SegWit signing | BIP 143 | ✅ Automatic |
| Witness serialization | BIP 144 | ✅ Via NBitcoin |

### Not Yet Implemented

| Feature | BIP | Reason |
|---------|-----|--------|
| Taproot (v1 witness) | BIP 340/341/342 | Not yet widely used on DigiByte; planned for future |
| Bech32m | BIP 350 | Required for Taproot addresses; will be added with Taproot support |

---

## Not Implemented (Out of Scope)

The following standards are implemented by DigiByte Core but are **not applicable** to a light wallet:

| BIP | Name | Reason |
|-----|------|--------|
| BIP 9 | Version bits | Consensus mechanism; node-level only |
| BIP 22/23 | getblocktemplate | Mining protocol; not a wallet feature |
| BIP 30/34 | Coinbase rules | Block validation; handled by the node |
| BIP 37 | Bloom filters | P2P SPV protocol; we use REST APIs, not raw TCP |
| BIP 111 | NODE_BLOOM | P2P service bit; not applicable |
| BIP 125 | Replace-by-fee | DGB does not enable RBF by default |
| BIP 152 | Compact blocks | Block relay optimization; node-level |
| BIP 155 | addrv2 | P2P address relay; node-level |
| BIP 157/158 | Compact block filters | Light client P2P; we use API backends |
| BIP 324 | v2 P2P transport | Encrypted P2P; node-level |
| BIP 340/341/342 | Taproot | Planned for future release |
| DigiAssets | Token layer | Token issuance/transfer not in scope |

---

## Regulatory Considerations

### Self-Custodial Classification

This wallet is **self-custodial** (non-custodial):

| Aspect | Detail |
|--------|--------|
| **Key custody** | User holds all private keys; encrypted on-device only |
| **Server access to keys** | None — server never sees keys, seeds, or PINs |
| **Fund control** | Only the user can sign and broadcast transactions |
| **Exchange/swap** | Not offered — no fiat on/off-ramp |
| **Token issuance** | Not offered |

### Regulatory Implications

| Jurisdiction | Typical Classification | Notes |
|-------------|----------------------|-------|
| **United States** | Not a money transmitter (FinCEN guidance) | Self-custodial wallets that don't hold or transmit user funds are generally exempt |
| **European Union** | Not a VASP under MiCA | Non-custodial software providers are excluded from VASP registration |
| **General** | Software tool, not a financial service | The wallet is open-source software; it does not custody, transmit, or exchange funds on behalf of users |

> **Disclaimer**: This is not legal advice. Regulatory landscapes vary by jurisdiction and change over time. Projects should consult legal counsel for specific compliance questions.

### No KYC/AML Requirements

As a self-custodial wallet with no exchange functionality:
- No user registration or identity verification
- No account creation on our servers
- No transaction surveillance or reporting obligations
- No data collection beyond what the user stores locally on their device

### Open-Source & DigiByte Foundation

- DigiByte is a decentralized, open-source blockchain with no central authority
- The DigiByte Foundation encourages third-party wallet development
- No trademark licensing required for non-commercial, open-source wallets
- This project is MIT licensed

---

## Verification & Testing

### Unit Test Coverage

| Test Suite | Tests | Coverage |
|-----------|-------|----------|
| `MultisigServiceTests` | 26 | Redeem scripts, key sorting, address generation, PSBT lifecycle |
| `MultisigWalletServiceTests` | 14 | Wallet CRUD, config persistence, pending transactions |
| `MultisigModelsTests` | 12 | Model validation, serialization, edge cases |
| Crypto & Wallet tests | 90+ | HD derivation, address validation, transaction building |

### Interoperability Verified Against

| Software | Version | Verification |
|----------|---------|-------------|
| DigiByte Core | v8.26.2 | Address format, PSBT exchange, transaction broadcast |
| NBitcoin | Latest | All cryptographic operations delegated to NBitcoin |
| Blockbook | Various | UTXO queries, transaction history, balance checks |
| Esplora | Various | Fallback blockchain data source |

### How to Verify Network Parameters

Compare values in `DigiByteNetwork.cs` against DigiByte Core source:
- [`src/chainparams.cpp`](https://github.com/DigiByte-Core/digibyte/blob/develop/src/chainparams.cpp) — Network magic, ports, address prefixes
- [`src/chainparamsbase.cpp`](https://github.com/DigiByte-Core/digibyte/blob/develop/src/chainparamsbase.cpp) — RPC ports
- [SLIP-44 Registry](https://github.com/satoshilabs/slips/blob/master/slip-0044.md) — Coin type 20 = DigiByte

---

## References

| Resource | URL |
|----------|-----|
| DigiByte Core BIPs | [github.com/DigiByte-Core/digibyte/blob/develop/doc/bips.md](https://github.com/DigiByte-Core/digibyte/blob/develop/doc/bips.md) |
| SLIP-44 Coin Types | [github.com/satoshilabs/slips/blob/master/slip-0044.md](https://github.com/satoshilabs/slips/blob/master/slip-0044.md) |
| BIP 39 Wordlists | [github.com/bitcoin/bips/blob/master/bip-0039/english.txt](https://github.com/bitcoin/bips/blob/master/bip-0039/english.txt) |
| BIP 174 PSBT | [github.com/bitcoin/bips/blob/master/bip-0174.mediawiki](https://github.com/bitcoin/bips/blob/master/bip-0174.mediawiki) |
| Digi-ID Protocol | [digibyte.org](https://www.digibyte.org/) |
| NBitcoin Library | [github.com/MetacoSA/NBitcoin](https://github.com/MetacoSA/NBitcoin) |

---

## Changelog

| Date | Change |
|------|--------|
| April 2026 | Initial compliance document for v0.3.0-beta.2 |
