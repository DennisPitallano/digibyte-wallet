# Data Model: Online Multisig Signing

## Server side (in memory only, `DigiByte.Api`)

### SigningRoom

| Field | Type | Rules |
|---|---|---|
| RoomId | string (12 hex) | Random; never logged |
| InviteCode | string (6) | Same alphabet as creation rooms (no 0/O/1/I); unique among live rooms |
| UnsignedTxId | string (64 hex) | Proposal identity; **unique among live rooms** (FR-019a) |
| CoSignerKeys | list of string (66 hex) | Sorted, 2–7, each a valid compressed key; includes the creator's key |
| RequiredSignatures | int | 1..N |
| Network | enum `main` \| `test` | From the open request; the PSBT and every submitted signature are parsed for this network; joiners on another network are refused |
| PsbtBase64 | string | The opener's PSBT; its global tx hash must equal UnsignedTxId; ≤ 100 KB |
| Memo | string? | ≤ 200 chars, shown as an unverified note |
| CreatorKey | string | Must be in CoSignerKeys and proven at open |
| CreatedAt / ExpiresAt | DateTimeOffset | ExpiresAt = CreatedAt + chosen lifetime (15/30/60/120, ≤ MaxLifetimeMinutes) |
| Participants | map key → SigningParticipant | At most one per co-signer key |
| SignedPsbts | list of (Key, PsbtBase64) | Appended on accepted SubmitSignature; at most one entry per key (newest replaces) |
| BroadcastClaim | (Key, ClaimedAt)? | One holder at a time; released on result, after 60 s, or on the holder disconnecting |
| BroadcastTxId | string? | Set from a successful BroadcastResult |
| Status | enum SigningRoomStatus | See state machine |

### SigningParticipant

| Field | Type | Rules |
|---|---|---|
| PublicKeyHex | string | Proven at join/rejoin |
| DisplayName | string | 1–40 chars, trimmed, chosen by the participant (FR-025) |
| State | enum ParticipantState | Joined, Offline, Signed, Declined |
| ConnectionId | string? | Server only, never sent to clients |
| IsCreator | bool | |

`ParticipantState` combines presence and action for display. Presence (`Online` bool) and action (`None`, `Signed`,
`Declined`) are stored separately, so an offline signer still counts as signed. Co-signer keys with no participant
are shown as **Not joined**.

### PendingChallenge

`(ConnectionId, Nonce[32], Purpose, IssuedAt)`. Valid 60 s, used once, at most 1 outstanding per connection.

### RateWindow (per connection)

Failed-join timestamps (5 per 60 s) and room-open timestamps (10 per 3600 s). Dropped on disconnect.

### SigningRoomStatus state machine

```text
Open ──threshold met (server counts valid-by-key signatures)──▶ Complete
Open/Complete ──BroadcastResult(success)──▶ Broadcast ──(grace 60 s)──▶ removed
Open/Complete ──creator CloseRoom──▶ Closed ──▶ removed
Open/Complete ──now ≥ ExpiresAt──▶ Expired ──▶ removed
Open ──declines make the threshold unreachable──▶ Open (flag `Unreachable` = true; FR-022)
```

On removal all data is dropped (FR-024) and clients get `RoomEnded(reason)`.

**Mapping to the spec's room statuses** (spec Key Entities: open, complete, broadcast, closed, expired): `Open`,
`Complete` and `Broadcast` are sent as `SigningRoomView.Status`; `closed` and `expired` are not live statuses but the
`RoomEnded` reasons `closed` and `expired`, sent once as the room is removed. `Complete` is advisory; each client
decides readiness from its own checked merge.

## Wallet side (`DigiByte.Wallet`, encrypted IndexedDB)

### PendingMultisigTransaction (existing, unchanged shape)

Used as is. The fields this feature relies on: `UnsignedTxId` (room proposal key), `PsbtBase64`, `SignedBy`,
`Status`, `BroadcastTxId`, `Memo`, `FailureReason`. Changes in behaviour:

- `AdoptProposalAsync` creates one from a room PSBT. `DestinationAddress`, `AmountSatoshis` and `FeeSatoshis` come
  from `MultisigProposalReader`, never from room metadata. `Memo` = the room memo, marked as from a co-signer.
- `MergeRoomSignaturesAsync` = `ImportSignedPSBTAsync` rules plus a check of each partial signature against its
  sighash.
- `MarkBroadcastAsync(pending, txId)` sets `Status = Broadcast` and `BroadcastTxId` when another participant
  broadcast it.
- A proposal with `Status = Cancelled` can't open or join a room (edge case).

### ProposalFacts (new, value object from `MultisigProposalReader`)

| Field | Notes |
|---|---|
| UnsignedTxId | Global tx hash |
| Inputs | list of (OutPoint, ValueSat); all must spend the multisig script |
| DestinationAddress, AmountSat | The single non-change output |
| ChangeSat | Output(s) to the multisig address (0 if none) |
| FeeSat | Σinputs − Σoutputs, must be > 0 |
| Problems | list of plain-language reasons (foreign input, missing UTXO value, more than one external output, wrong network) |

Validation: if `Problems` is not empty, joining and signing are blocked (FR-005).

### SigningRoomSession (new, client memory only, `DigiByte.Web`)

Room id, code, ExpiresAt, local multisig `WalletId`, local proposal `Id`, my key, my display name, the latest
`SigningRoomView`, and room warnings (invalid signature from X, payment can't be completed). It is not persisted;
the durable state is the merged proposal.
