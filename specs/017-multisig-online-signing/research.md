# Research: Online Multisig Signing

All Technical Context items were resolved; no NEEDS CLARIFICATION remains. Each decision below lists its rationale
and the alternatives considered.

## R1. New hub vs. extending the creation-room hub

- **Decision**: Add a separate `MultisigSigningHub` at `/hubs/multisig-signing` with its own contracts in
  `DigiByte.P2P.Shared`. Reuse the invite-code generator and the client connection pattern.
- **Rationale**: Creation rooms close when the initiator leaves, reset ready states, and expire after 15 minutes.
  Signing rooms need the opposite (FR-019, FR-017). Keeping them apart avoids breaking older clients of
  `IMultisigRoomHub` and keeps each hub small.
- **Alternatives**: Extend `MultisigRoomHub` with a room "kind". Rejected because the shared lifecycle code would
  branch everywhere, and a contract change risks the shipped creation flow.

## R2. Proving a joiner holds a co-signer key (FR-004)

- **Decision**: Challenge–response with compact (recoverable) ECDSA signatures:
  1. The client calls `RequestChallenge()` and receives a random 32-byte nonce, valid 60 s and bound to the connection.
  2. The client signs the message `"DigiByte multisig signing room\n{purpose}\n{code-or-roomId}\n{nonceHex}\n{connectionId}"`
     (`purpose` = `open` | `join` | `rejoin`) with the wallet's multisig key, using double-SHA256 and `Key.SignCompact`.
     This is the same primitive Digi-ID already uses (`DigiIdService`).
  3. The hub recovers the public key, checks it equals the claimed key and is in the room's co-signer list, and
     uses up the nonce.
  - The same proof is required to **open** a room (the opener must be one of the co-signer keys they submit).
- **Rationale**: The proof never reveals the private key, can't be replayed (one-time nonce, bound to the
  connection id), and can't be confused with a transaction signature (fixed domain-separation prefix). Watch-only
  devices have no key, so they're excluded naturally, as the spec requires.
- **Alternatives**: (a) A shared room secret in the link. Rejected: it doesn't work for typed 6-character codes,
  and it proves knowing the code, not holding a key. (b) Proof by knowing the redeem script. Rejected: watch-only
  holders and anyone given the backup know it too.
- **Helper**: implemented by the shared `DigiByte.Crypto.Multisig.CoSignerProof` (added with the creation-room
  key-proof change), using `CoSignerProof.SigningRoomDomain` as the message prefix so creation-room and
  signing-room proofs can't be swapped.
- **Unknown-code behaviour**: A wrong code and a valid code with a non-member key both return the same
  `Error("Invalid or expired code.")`, and both count as a failed join attempt (FR-004, FR-027).
- **Opening when a room already exists**: `OpenRoom` for a payment that already has a live room returns that
  room's code only when the proven key is one of *that room's* co-signer keys. Anyone else gets the same
  `invalid_code` reply, counted as a failed join attempt, so a connection never sees a room before proving it
  belongs to it (constitution 1.2.0, Auth boundary).

## R3. Can the server be kept from seeing payment data (end-to-end encryption)?

- **Decision**: No end-to-end encryption in this version. The hub sees the PSBT (public data, per FR-023), keeps it
  in memory only, and never logs it (FR-024, FR-028).
- **Rationale**: To check FR-004 the hub must know the co-signer public keys and the threshold. From those it can
  rebuild the sorted-multisig redeem script, so any key derived from the redeem script would give no real
  protection. A key carried in the link fragment would not work for typed codes (FR-002).
- **Alternatives**: A key in the link fragment for link joins only. Deferred: possible future hardening, but it
  creates two classes of room.

## R4. Identifying the proposal and the multisig across devices

- **Decision**: The room carries (a) the multisig's sorted co-signer public keys and threshold M, and (b) the
  unsigned transaction id (`UnsignedTxId`) as the proposal key. The hub enforces one active room per
  (UnsignedTxId) (FR-019a). Each joining wallet finds its local multisig by matching the redeem script rebuilt from
  those keys and M, which has to equal a local `MultisigWalletConfig.RedeemScriptHex`, and its local proposal by
  `UnsignedTxId`.
- **Rationale**: Local `WalletId` and proposal `Id` values differ per device. The redeem script and the unsigned
  txid are the shared identities, and `ImportSignedPSBTAsync` already uses `UnsignedTxId` as the "same proposal"
  check.
- **Server check on open**: the hub rebuilds the sorted multisig script from the submitted keys and M and
  requires every payment input to spend it, as P2WSH or P2SH-P2WSH (inputs without a witness UTXO are refused).
  This stops an opener from listing keys that are not really co-signers, which would otherwise pass the join
  check. Joiners still check everything themselves (FR-005).
- **Alternatives**: The hub assigning a global proposal id. Rejected: it can't be checked locally, and it breaks
  for proposals created offline.

## R5. Saving a proposal on a device that doesn't have it yet (FR-006)

- **Decision**: Add `MultisigWalletService.AdoptProposalAsync(config, psbtBase64, memo)`. A new
  `MultisigProposalReader` works out the destination, amount, fee, and change from the PSBT itself:
  - inputs must spend this multisig's script, with witness UTXO values present;
  - the fee is the sum of inputs minus the sum of outputs;
  - change is an output paying the multisig address;
  - at most one non-change output is allowed in this version, matching what `MultisigSend` creates.

  The room's descriptive fields (memo, display names) are shown as unverified notes only.
- **Rationale**: Never trust relayed metadata for amounts or addresses (FR-005, address-handling rule). The same
  reader is used to compare a room's PSBT with an already-held proposal (same `UnsignedTxId` means the same inputs
  and outputs).
- **Alternatives**: Trusting the opener's summary. Rejected as unsafe.

## R6. Checking and merging signatures (FR-010–FR-012)

- **Decision**: Clients send partially signed PSBTs (`SubmitSignature(roomId, psbtBase64)`). The hub does a cheap
  check that the PSBT's global unsigned tx hash equals the room's `UnsignedTxId` and that every *new* partial
  signature belongs to the sending participant's key; anything else is rejected and never relayed. It then appends
  the PSBT to the room's in-memory signature list and relays it. Each client runs `MergeRoomSignaturesAsync`, which
  reuses the `ImportSignedPSBTAsync` rules (same tx, only co-signer keys). It also **checks each partial signature
  against the input's sighash**, which the offline import doesn't do today, before combining. Invalid input raises a
  room warning naming the participant.
  - On join, each client submits whatever signatures it already holds locally (FR-012); duplicates combine
    harmlessly and are counted once (by public key).
- **Rationale**: Client-side checking is the trust anchor (FR-024); the hub check only stops noise and spoofing.
  Adding real signature checking closes a gap where a bad signature could be counted and only fail at finalize.
- **Alternatives**: The server combining PSBTs. Rejected: it would make the server a trusted part of signing.

## R7. Broadcasting at most once (FR-014–FR-016)

- **Decision**: A broadcast claim held by the hub. `ClaimBroadcast(roomId)` succeeds for one participant at a time
  (others get `BroadcastClaimed(by)`); the claim is released when that participant reports `BroadcastResult`, after
  60 s, or when they disconnect. The claimant runs the existing `FinalizeAndBroadcastAsync`; an "already in
  mempool/known" reply from the node is treated as success with the computed txid. The result is relayed, and every
  client updates its local proposal (`MarkBroadcastAsync` with the txid, so it shows once in Activity as Sent).
- **Rationale**: Two broadcasts of the same fully signed transaction give the same txid, so the claim mainly stops
  confusing double errors. Treating "already known" as success covers the race where it isn't held.
- **Alternatives**: Auto-broadcast by the hub. Rejected: the server would need the final transaction and would act
  for users.

## R8. Room lifetime and options (FR-017)

- **Decision**: `SigningRoomOptions` bound from configuration section `MultisigSigning`:
  `MaxLifetimeMinutes` (default 120), `AllowedLifetimesMinutes` = [15, 30, 60, 120], `DefaultLifetimeMinutes` = 30.
  The client calls `GetOptions()` and shows only choices ≤ the max; `OpenRoom` rejects anything longer. The
  remaining time is sent as an absolute `ExpiresAt` (UTC). A single timer sweep (every 15 s) ends expired rooms,
  instead of a `Task.Delay` per room.
- **Rationale**: Matches the clarified choices. A sweep avoids timers piling up and makes tests deterministic
  through an injected `TimeProvider`.

## R9. Presence and reconnection (FR-018, FR-019)

- **Decision**: Participants are identified by public key, not connection. On disconnect the participant is marked
  `Offline` and stays in the room; the creator leaving does not close it. The client uses
  `WithAutomaticReconnect` (0, 2, 5, 10, 20, 30 s) and, after reconnecting, calls `Rejoin(roomId)` with a fresh proof.
  The hub moves the participant to the new connection and sends a full `RoomState`. A second live connection for the
  same key replaces the first, which receives `Superseded`.
- **Rationale**: Meets SC-006 (back within 60 s) without manual steps and keeps one presence per key.

## R10. Rate limiting (FR-027)

- **Decision**: An in-hub `SigningRoomRateLimiter` keyed by connection id: a sliding window of 5 *failed* join
  attempts per 60 s and 10 room opens per 3600 s. Over the limit, the hub sends `Error("Too many attempts, try
  again shortly.")` without looking up the code. Defence in depth: an ASP.NET fixed-window policy on the hub's
  negotiate endpoint (30 connections/min per client address, reusing `SponsorEndpoints.ClientKey`), so reconnecting
  can't reset the per-connection counters cheaply.
- **Rationale**: ASP.NET's rate-limiting middleware covers HTTP requests, not individual hub calls, so the hub
  needs its own limiter.

## R11. Logging (FR-028)

- **Decision**: Structured logs and metrics via `ILogger` / `System.Diagnostics.Metrics` with fixed event names and
  numbers only: `signing_room.opened` (lifetime), `signing_room.ended` (reason, participant count, signature count),
  `signing_room.broadcast` (outcome), `signing_room.rate_limited` (kind), `signing_room.error` (kind). No room id,
  code, key, address, amount, PSBT, or name. Unhandled hub exceptions are caught and logged with the exception
  *type* only, since messages may echo input.

## R12. Availability detection (FR-021)

- **Decision**: The Pending page tries to connect to the hub lazily, with a 5 s timeout, the first time the user
  opens the "Sign together online" sheet, and caches the result for the session. On failure the action shows "Online
  signing is unavailable right now. You can still share and import signatures." Core pages never connect to the hub.

## R13. Several wallets on one device (US3 scenario 2)

- **Decision**: The join uses the **active** wallet's multisig key. If it isn't a co-signer but another wallet on
  the device owns a matching multisig, the page says "Switch to <wallet name> to join" with a one-tap switch, then
  continues the join. That is how the user "picks which wallet" here.
- **Rationale**: Key access (`GetPrivateKeyForMultisig`, `GetPublicKey`) is scoped to the unlocked active wallet;
  signing with another wallet needs that wallet unlocked anyway.
