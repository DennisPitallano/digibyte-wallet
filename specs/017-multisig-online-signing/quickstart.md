# Quickstart: Validate Online Multisig Signing

## Prerequisites

- .NET 10 SDK; repo builds with `dotnet build src/src.sln`.
- `DigiByte.Api` running locally (it hosts `/hubs/multisig-signing`) and the wallet (`DigiByte.Web`) pointing at it
  through `PriceApiUrl`, as for creation rooms.
- Two browser profiles (A and B), each with a different wallet, both co-signers of the same **2-of-3** testnet
  multisig with a funded balance. Create it through the existing creation room if needed.
- A third profile C with a wallet that is **not** a co-signer, and one with a **watch-only** import of the multisig.

## Automated checks

```bash
dotnet test tests/DigiByte.Crypto.Tests --filter SigningRoomProof
```

```bash
dotnet test tests/DigiByte.Wallet.Tests --filter MultisigOnlineSigning
```

```bash
dotnet test tests/DigiByte.Api.Tests --filter "SigningRoomRegistry|SigningRoomRateLimiter"
```

They must cover: a proof replayed, sent from a different connection, made by a wrong key, or for the wrong network;
adopting a PSBT with a foreign input or two external outputs (refused); merging an invalid partial signature
(discarded); one room per txid; failed-join and open limits; expiry via a fake `TimeProvider`; the creator
disconnecting doesn't end the room; one broadcast claim at a time.

## Manual scenarios

| # | Steps | Expected |
|---|---|---|
| 1 (US1) | A: Send 1 DGB → create the proposal and sign → Pending → **Sign together online** (30 min) | Room shows the code, link, QR, a countdown, and the checklist A=Signed, B/C=Not joined |
| 2 (US3) | B: open the link while locked | Unlock prompt, then the room with the code filled in; the payment details match A's; the proposal now appears in B's Pending list |
| 3 (US1) | B: **Sign** → confirm | Within 5 s both show 2/2 and "Ready to broadcast" |
| 4 (US2) | A and B tap **Broadcast** together | One broadcast; both show the same txid; both proposals appear in Activity as Sent, once |
| 5 (FR-004) | C: enter the code | "Invalid or expired code."; nothing about the payment is shown |
| 6 (FR-027) | C: enter wrong codes 6 times within a minute | The 6th shows "Too many attempts, try again shortly." |
| 7 (US4) | New proposal and room; B joins, signs; turn off B's network for 30 s, then back on | B is back in the room by itself, with its signature still counted |
| 8 (FR-019) | A closes the tab mid-room | B still sees the room; A shows as offline; B can broadcast once the threshold is met |
| 9 (FR-019a) | B taps **Sign together online** on a proposal that already has a room | B is offered the existing room; no second code is created |
| 10 (US5) | A: **Close room** | B gets "Room closed"; both still hold every signature; Share/Import offered |
| 11 (FR-021) | Stop `DigiByte.Api`; open Pending | "Sign together online" is unavailable with an explanation; offline sign/share/import/broadcast work |
| 12 (edge) | Watch-only profile opens the link | Online signing unavailable; offline flow offered |
| 13 (FR-028) | Check the API logs after scenarios 1–10 | Only event names and counts; no keys, codes, addresses, amounts, PSBTs, or names |
| 14 (FR-004) | While A's room is open, send `OpenRoom` for the same payment from a test client signed with C's key (the wallet UI won't offer it to a non-co-signer) | "Invalid or expired code."; C sees no code and nothing about the room; the attempt counts toward C's failed-join limit |
