# Contracts: PSBT Signing

**Feature**: [../spec.md](../spec.md) | **Types**: [../data-model.md](../data-model.md)

This feature has no network API. These are its internal boundaries and the page's behavior, which tests rely on. Exact signatures may be refined during implementation, but the guarantees may not.

## `IPsbtKeyProvider` (DigiByte.Wallet)

Implemented by `WalletService`. It is the only route from this feature to private keys.

| Member | Guarantee |
|---|---|
| `bool CanSign` | False when locked or watch-only |
| `Network Network` | The active wallet's network, used to load PSBTs and turn scripts into addresses |
| `Task<IReadOnlyDictionary<Script, string>> GetOwnedScriptsAsync()` | Scripts and their addresses for the range in FR-004, after waiting a limited time for address discovery. Never fails because discovery did. Contains no key material. Available for watch-only wallets |
| `PSBT SignInputs(PSBT psbt, IReadOnlyList<int> inputIndexes)` | Signs a copy, one input at a time, only for listed indexes whose script is owned. Never modifies the original. Throws if `CanSign` is false |

## `WalletService.VerifyPinAsync(walletId, pin)`

| Case | Result |
|---|---|
| Correct PIN | True |
| Wrong PIN, missing wallet | False |
| Any case | Does not change the session or active wallet. Clears decrypted bytes before returning |

## `IBlockchainService.GetVerifiedUtxosAsync(address)`

| Case | Result |
|---|---|
| The source answers | The address's unspent outputs, which may be none |
| The source can't answer, or its reply can't be read | Throws. It never returns an empty list |

## `PsbtSigningService` (DigiByte.Wallet)

Holds no PSBT between calls.

| Method | Behavior |
|---|---|
| `LoadAsync(text or bytes)` → `(PsbtDocument?, ReviewSummary)` | Decodes, performs lookups, runs the analyzer. Refusals are returned, not thrown. A chain failure on this wallet's inputs gives `ChainStatus = Unavailable` and `CanSign = false`. A failed lookup of another participant's input with a stated amount does not |
| `SignAsync(document, acknowledged)` → `SigningResult` or refusal | Repeats all chain checks first (FR-008). Refuses if acknowledgment is required but missing. Adds missing SegWit data (FR-023). Signs only `InputsToSign` |
| `PrepareSignedAsync(document, acknowledged)` → `SigningResult` or refusal | For a PSBT that arrived already fully signed. Repeats all chain checks, refuses if acknowledgment is required but missing, requires spending keys and at least one wallet input, and signs nothing. Its result is what `BroadcastAsync` takes |
| `BroadcastAsync(signingResult, review)` → `BroadcastResult` | Requires spending keys, `IsComplete`, and at least one wallet input (FR-024). Records one history entry on success (research R9) |

## `PsbtAnalyzer.Analyze` (DigiByte.Crypto)

Pure: the same inputs always give the same result, with no I/O.

| Input | Output |
|---|---|
| PSBT, owned scripts, chain facts per input, network | `ReviewSummary` |

The refusal order is defined in the data model.

## `PsbtSigner` (DigiByte.Crypto)

| Method | Guarantee |
|---|---|
| `SignInputs(psbt, keysByIndex)` | Uses per-input signing with the standard signature type only. Never calls `SignWithKeys` |
| `TryComplete(signedPsbt)` | Finalizes a clone. Returns the transaction if scripts validate, or the errors if not. Leaves the original not finalized |

## Page: `/settings/psbt-sign`

| Condition | Page behavior |
|---|---|
| Locked | Existing `AuthGuard` behavior |
| Simple mode | Explains that Advanced mode is required. No input controls |
| Watch-only | Can review. Sign and broadcast are unavailable, with an explanation |
| Chain unavailable | Review labeled unverified. Sign is unavailable. Retry offered |

**Order on Sign**: Each step must pass before the next starts. `PsbtConfirmationFlow` in `DigiByte.Web` owns this order, so it can be tested without the page. It takes each check as a delegate and stops at the first that fails.

1. Required acknowledgment is given (FR-017).
2. Spending limit check passes (FR-033), using `NetAmount`, or zero when the wallet gains overall. If the check itself fails to run, it does not block.
3. Address and amount warnings are shown when every input is the wallet's, or the shared-transaction note otherwise (FR-031).
4. Biometrics if enabled, with PIN as the fallback, otherwise PIN, throttled (FR-019).
5. `SignAsync` runs.

**Already signed path**: Steps 1 to 4 run through the same flow before the broadcast confirmation, and step 5 is replaced by `PrepareSignedAsync` (FR-034).

**After broadcast**: Clear the same caches as `Send.razor`.

**Settings entry**: Shown near Recovery Tools only when `AppState.IsSimpleMode` is false (FR-030).

## Formats

| Direction | Formats |
|---|---|
| In | Base64 text (surrounding whitespace ignored) or a binary `.psbt` file, up to 1 MB |
| Out | Base64 text to the clipboard, or a binary `.psbt` download |
