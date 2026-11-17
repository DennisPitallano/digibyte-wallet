# Data Model: Multisig UX Polish

All types are presentation-level, computed on the fly in `DigiByte.Wallet`. Nothing is persisted or sent over the wire.

## Enums
- **SignerPresence**: Online, Offline
- **SignerStatus**: Waiting, Signed, Declined (creation rooms: Joined, Ready)
- **MultisigState** (shared vocabulary): NeedsYourSignature, YouSigned, WaitingForOthers, ReadyToBroadcast, LiveRoom, Sent, CancelledOrExpired, Failed
- **PrimaryAction**: None, ReviewAndSign, Broadcast, CreateWallet, OpenRoom, Sign, View, GoToPending

## SignerCardModel
| Field | Source |
|---|---|
| PublicKeyHex | participant / co-signer key |
| DisplayName, IsYou | participant name; key equals local key |
| Initials | first letters of up to two words; fallback "?" for empty or emoji-only |
| Fingerprint | first 4 hex chars of the key; shown only when two cards share a name |
| Presence | Signing rooms only: `Online` flag, passed through `PresenceDebouncer`. Creation rooms have none (a disconnected co-signer is removed); missing co-signers are "Not joined yet" slots |
| Status | `Action` / `SignedKeys` (signing); `IsReady` (creation) |
| IsCreator | `IsCreator` / `IsInitiator` |

Rule: a signer who has signed stays Signed when Offline (both facts shown).

## RoomHeadline
`{ Title, Detail?, Tone (Neutral|Action|Positive|Warning), PrimaryAction, PrimaryLabel? }`, derived from room status, viewer key, `SignedKeys`, `RequiredSignatures`, `Unreachable`, and the watch-only flag.

| Situation | Title | Primary |
|---|---|---|
| Viewer not signed, below threshold | Your signature is needed | ReviewAndSign |
| Viewer signed, below threshold | Waiting for N more signature(s) | None |
| Threshold met, not broadcast | Ready to broadcast | Broadcast |
| Unreachable | Can't reach the required signatures | GoToPending |
| Broadcast | Sent | View |
| Watch-only viewer | View only | None |

## PaymentSummary (view model)
`AmountSat, RecipientAddress (grouped), FeeSat, TotalOutSat (= amount + fee), Memo?, ChangeNote, Details (inputs, tx ids)`. Same field order everywhere; empty memo and change are omitted.

## AttentionCount
`{ WalletId, NeedsSignature }` per wallet plus a total. Counts pending items with Status AwaitingSignatures and the local key not in `SignedBy`.

## PresenceDebouncer state
Per key: last raw presence and the time it went offline. Displayed Offline only after being offline for at least 10 s (a constant); Online is immediate.

## Step indicator
Creation steps: Set up, Invite, Confirm, Ready. Manual and import paths use the applicable subset with the same labels.
