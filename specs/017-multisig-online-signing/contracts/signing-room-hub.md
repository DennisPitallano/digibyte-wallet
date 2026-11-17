# Contract: Multisig Signing Room Hub

SignalR hub at `{apiBase}/hubs/multisig-signing`. The C# contracts live in
`DigiByte.P2P.Shared/Contracts/IMultisigSigningHub.cs` and the DTOs in `DigiByte.P2P.Shared/Models/`.
All hub methods return `Task`. Errors arrive on `Error(code, message)` unless noted.

## Join / open proof

```text
message  = "DigiByte multisig signing room\n" + purpose + "\n" + target + "\n" + nonceHex + "\n" + connectionId
purpose  = "open" | "join" | "rejoin"
target   = UnsignedTxId (open) | InviteCode upper-case (join) | RoomId (rejoin)
hash     = SHA256(SHA256(UTF8(message)))
proof    = Base64(Key.SignCompact(hash))       // 65 bytes, recoverable
```

Helper: `DigiByte.Crypto.Multisig.CoSignerProof.BuildMessage/Sign/TryVerify` with `domain = CoSignerProof.SigningRoomDomain`
(the same helper the creation-room hub uses with `CreationRoomDomain`; the domain keeps the two from being swapped). The hub recovers the public key from the
proof and requires it to equal `publicKeyHex`.

## Client → server (`IMultisigSigningHub`)

| Method | Arguments | Behaviour |
|---|---|---|
| `GetOptions()` | — | → `Options(SigningRoomOptionsView)`: allowed lifetimes ≤ max, default. |
| `RequestChallenge()` | — | → `Challenge(nonceHex)`. Replaces any outstanding challenge; valid 60 s. |
| `OpenRoom(OpenSigningRoomRequest)` | `Network` (`main` \| `test`), `CoSignerKeys[]`, `RequiredSignatures`, `PsbtBase64`, `Memo?`, `LifetimeMinutes`, `DisplayName`, `PublicKeyHex`, `Proof` | Checks: rate limit (open), proof (purpose `open`, target = PSBT txid), key in CoSignerKeys, PSBT parses with txid and ≤ 100 KB, **every PSBT input spends the multisig script rebuilt from CoSignerKeys and RequiredSignatures** (an input passes when its witness UTXO's script is either the P2WSH or the P2SH-P2WSH form of that script (the app's default address type is `p2sh-p2wsh`); an input without a witness UTXO is refused; else `Error("bad_psbt", …)`), lifetime allowed. If a live room exists for this txid: **only if the proven key is in that room's CoSignerKeys** → `ExistingRoom(inviteCode)` (the client then joins it, FR-019a); otherwise the same `Error("invalid_code", "Invalid or expired code.")` as a wrong code, counted as a failed join attempt, and nothing about the room is revealed. Else creates the room → `RoomState`. |
| `JoinRoom(JoinSigningRoomRequest)` | `InviteCode`, `DisplayName`, `PublicKeyHex`, `Proof` | Rate limit (failed joins) checked first. An unknown code, a bad proof, or a key not in CoSignerKeys all give the same `Error("invalid_code", "Invalid or expired code.")` and count as one failed attempt. On success the caller is added to the group and receives `RoomState` (with PSBT and SignedPsbts); others receive `RoomState`. |
| `Rejoin(RejoinSigningRoomRequest)` | `RoomId`, `PublicKeyHex`, `Proof` | Proof purpose `rejoin`. Moves the participant to this connection; any older connection gets `Superseded`. → `RoomState`. |
| `SubmitSignature(roomId, psbtBase64)` | | Same txid as the room; every partial signature not already held must belong to the caller's key. Accepted → stored, participant marked Signed, group gets `SignatureAdded(publicKeyHex, psbtBase64)` + `RoomState`. Rejected → caller gets `Error("bad_signature", …)`. |
| `Decline(roomId)` | | Marks Declined (allowed only if not Signed); group gets `RoomState`. |
| `ClaimBroadcast(roomId)` | | Granted → caller gets `BroadcastGranted`; group gets `BroadcastClaimed(publicKeyHex)`. Held by another → caller gets `BroadcastClaimed(holderKey)`. |
| `ReportBroadcast(roomId, success, txIdOrReason)` | | Only from the claim holder. Success → status Broadcast, group gets `Broadcasted(txId)`. Failure → claim released, group gets `BroadcastFailed(reason)` (reason ≤ 200 chars, shown to users, never logged). |
| `CloseRoom(roomId)` | | Creator only. Group gets `RoomEnded("closed")`; room data dropped. |
| `LeaveRoom(roomId)` | | Participant marked Offline (not removed); the connection leaves the group. |

## Server → client (`IMultisigSigningClient`)

| Callback | Payload |
|---|---|
| `Options` | `SigningRoomOptionsView { AllowedLifetimesMinutes[], DefaultLifetimeMinutes }` |
| `Challenge` | `nonceHex` |
| `RoomState` | `SigningRoomView` (below) |
| `ExistingRoom` | `inviteCode` |
| `SignatureAdded` | `publicKeyHex`, `psbtBase64` |
| `BroadcastGranted` / `BroadcastClaimed` | — / `publicKeyHex` |
| `Broadcasted` / `BroadcastFailed` | `txId` / `reason` |
| `RoomEnded` | `reason`: `expired` \| `closed` \| `broadcast` |
| `Superseded` | — (this connection was replaced by a newer one for the same key) |
| `Error` | `code`, `message`. Codes: `invalid_code`, `rate_limited` ("Too many attempts, try again shortly."), `bad_proof_open`, `bad_psbt`, `bad_signature`, `bad_lifetime`, `not_allowed`, `room_ended` |

### SigningRoomView

```text
RoomId, InviteCode, ExpiresAt (UTC), Network, UnsignedTxId, RequiredSignatures, CoSignerKeys[],
PsbtBase64, Memo?, SignedPsbts[{PublicKeyHex, PsbtBase64}],
Participants[{PublicKeyHex, DisplayName, Online, Action: None|Signed|Declined, IsCreator}],
Status: Open|Complete|Broadcast, Unreachable (bool), BroadcastClaimHolder?, BroadcastTxId?
```

`ConnectionId` is never included. `SignedPsbts` and `PsbtBase64` may be left out of `RoomState` updates after the
first one sent to a connection (flag `IncludesPayload`) to keep messages small.

## Invite link

`{walletOrigin}/multisig/sign-room?code=XXXXXX`. Opening it while locked goes to unlock, then returns with the code
prefilled (FR-003).

## Configuration (`DigiByte.Api` appsettings)

```json
"MultisigSigning": {
  "MaxLifetimeMinutes": 120,
  "DefaultLifetimeMinutes": 30,
  "AllowedLifetimesMinutes": [15, 30, 60, 120],
  "FailedJoinsPerMinute": 5,
  "RoomsOpenedPerHour": 10
}
```
