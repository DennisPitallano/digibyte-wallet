# Quickstart: Checking Multiple Recipients in a Multisig Payment

A walkthrough that proves the feature works. Screen details are in [contracts/ui-contracts.md](contracts/ui-contracts.md), service rules in [contracts/wallet-services.md](contracts/wallet-services.md), and data in [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK, and the repository restored.
- Testnet mode. A 2-of-3 shared wallet with a small balance (at least 1 DGB), held on two devices or browser profiles (A and B). At least one saved contact on A.
- For the room check, the `digibyte-api` configuration from `.claude/launch.json` running as well.

## Automated checks

```powershell
dotnet build DigiByte.slnx
```

```powershell
dotnet test tests/DigiByte.Crypto.Tests --filter "FullyQualifiedName~Multisig"
```

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Multisig|FullyQualifiedName~PaymentSummary|FullyQualifiedName~QrCapacity"
```

Expected: build succeeds. The new tests pass:

- the multi-payment builder (order, change, dust, under-funded refusal; the single overload produces the same transaction as before)
- row validation and the multi-amount estimate
- the reader's `Recipients` and warnings
- creating with N recipients, and signing and finalizing it into a transaction with every output
- the shortfall on an under-funded estimate
- `CheckProposalMatches` on a reordered or changed amount
- legacy records without `Recipients`
- activity `MoreCount` and `Recipients`
- payment summary with several recipients
- `QrCapacity`

All existing multisig tests still pass.

## Run the app

Start `digibyte-web` (and `digibyte-api` for step 4) from `.claude/launch.json`. View at 375 px wide, in light and dark themes, in English and then Spanish.

## Walkthrough

### 1. Create (Story 1, Story 4)

1. On A, open the shared wallet → Send → turn on **Multiple recipients**. Expect: two empty rows, a "2 / 20 recipients" counter, and no Max or quick shares.
2. Fill row 1 from a contact, row 2 by scanning a `digibyte:` link with an amount, and add row 3 by pasting. Expect: each row fills only itself; the fee and totals update.
3. Make row 3 repeat row 1, then make it 0.0005 DGB, then use the shared wallet's own address. Expect: a row error each time and Continue disabled.
4. Make the total more than the balance. Expect: "Not enough in this shared wallet: X DGB short", where X = total + fee − balance.
5. Fix the rows, turn the toggle off and on again. Expect: the rows are unchanged.
6. Continue → Review. Expect: three numbered lines (contact name on row 1), then total, fee, total with fee and remaining.
7. Create. Expect: the Done screen with the total, "to 3 recipients", the list, and the up-to-date-wallet notice.

### 2. Lists and activity (Story 3)

1. On A, open Pending. Expect: the card shows the total and "to 3 recipients".
2. Open Activity. Expect: the proposal line shows the first address and "+2 more". Open its detail sheet. Expect: all three addresses and amounts, then the total and fee. After it's broadcast (step 3.2), the sent line's detail sheet lists the same three.
3. Open an older single-recipient proposal. Expect: it looks as it did before.

### 3. Share and sign offline (Story 2)

1. On A, open the proposal's detail → Share. With 3 recipients and one coin, expect a QR code. Copy it and import it on B by pasting. Expect: B lists every recipient, amount, fee and the change returning to the shared wallet. Names show only for addresses in B's contacts.
2. On B, sign. Bring the signed copy back to A (file). Expect: A merges it and the proposal is ready. Broadcast. Expect: Sent, and the explorer shows three payment outputs plus change.
3. Create a 20-recipient proposal and sign it on A and B (or make one that spends several coins), so its text is over 2,200 characters. One coin, unsigned, is about 1,500 and still shows a QR code. Open Share. Expect: Copy and Save as file, no QR code, and the line "This proposal is too large for a QR code. Use Copy, Save as file, or sign together in a signing room." The exact limit is covered by `QrCapacityTests`.

### 4. Sign online (Story 2)

1. On A, open a signing room for a multi-recipient proposal and join from B. Expect: B's room shows the full recipient list, read from the PSBT; signing works; the threshold is reached and it broadcasts.

### 5. Received proposals that break the rules (FR-013a)

1. Build a PSBT outside the app (or with the test helper in `MultisigProposalReaderTests`) that pays one recipient 0.0005 DGB and another address twice. Import it on B. Expect: it is accepted, the two lines carry amber warnings, and B can still sign.
2. Edit the saved proposal so its stored amount differs from the PSBT (test only). Expect: the "saved recipients, amounts or fee do not match" refusal.

### 6. Spanish

Switch the language to Español and repeat 1.1, 1.6 and 3.3. Expect: no English text in the new parts.
