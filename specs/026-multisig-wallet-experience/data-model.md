# Data Model: Multisig Wallet Experience

Almost everything here is computed on the fly from data the wallet already has. One preference is stored. Nothing is sent anywhere.

## Stored

| Item | Key | Value | Notes |
|---|---|---|---|
| Last-used shared wallet | `pref:multisigLastUsed:{personalWalletId}` | shared wallet id | Written when a dashboard opens. Device-local. Ignored when the id is no longer available to that personal wallet. A stale id is harmless and is not cleaned up. |

No change to `MultisigWalletConfig`, `PendingMultisigTransaction`, drafts, or any room contract.

## Computed (presentation only)

### ShellContext (`DigiByte.Web/Layout`)

| Field | Type | Meaning |
|---|---|---|
| Kind | `Personal` \| `Shared` | Which menu to show |
| SharedWalletId | string? | Set when `Shared` |
| Section | `Overview` \| `Activity` \| `Pending` \| `None` | Which tab is current (`send` → `None`; `pending/{id}` → `Pending`) |

Rule: `multisig/{id}[/send|/pending[/{pendingId}]|/activity]` with `{id}` not in {`create`, `import`, `room`, `sign-room`} → `Shared`. Everything else → `Personal`.

### ShellChrome (`DigiByte.Web/Services`, scoped)

| Field | Type | Meaning |
|---|---|---|
| HideNav | bool | True while a signing room is live or showing its result |
| Changed | event | Layout re-renders |

### AttentionSummary (extended, `DigiByte.Wallet`)

Per shared wallet and in total:

| Field | Meaning |
|---|---|
| NeedsSignature | Open payments the viewer has a key for and has not signed (today's figure) |
| ReadyToBroadcast | Payments with enough signatures, not yet sent |
| FailedBroadcast | Payments whose last send failed |
| NeedsYou | Sum of the three; the number on Pending, More, the Multisig row and the home shortcut |

Display rule: `CountBadge.Text(n)` → "" for 0, the number up to 99, "99+" above.

### PendingGroups (`DigiByte.Wallet`)

| Group | Contains | Order |
|---|---|---|
| NeedsYou | Needs your signature; ready to broadcast; failed broadcast; live room the viewer has not signed | Newest first |
| WaitingForOthers | You signed; waiting for others (view only); live room the viewer has signed | Newest first |
| Cancelled | Cancelled | Newest first; folded by default |

Sent payments are never in a group (FR-018a).

### PendingCardAction (`DigiByte.Wallet`)

`ReviewAndSign`, `OpenRoom`, `GetSignatures`, `ReviewAndBroadcast`, `ReviewAndRetry`, `ReviewAndCancel`, `View`, `None`, each with its label. See research R5 for the table. Every action except `OpenRoom` navigates to the payment's own screen.

### WalletCardModel (`DigiByte.Wallet`)

| Field | Source |
|---|---|
| Id, Name, Scheme, CoSignerCount | `MultisigWalletConfig` |
| IsViewOnly | `OwnKeyIndex < 0` |
| NotBackedUp | `BackupAt is null` |
| Status | `WalletCardStatusLine` (built by `WalletCardStatus.For`): one of NeedsSignature(n), ReadyToBroadcast(n), LiveRoom (only a room this device has joined), WaitingForOthers(n co-signers), None |
| NeedsYou | From `AttentionSummary` |
| LastActivity | Latest of newest pending `UpdatedAt` and the wallet's `CreatedAt` |
| Balance | Filled later: Loading → Amount(sat) \| Unavailable |

### RoomStage (`DigiByte.Wallet`)

```text
Invite ──(present-or-signed ≥ required)──▶ Sign ──(signed ≥ required)──▶ Broadcast ──(sent)──▶ Done
```

A room can enter at any stage (for example signatures imported offline before it opened). Within one visit to the room the tracker only moves forward: a co-signer going offline does not move it back from Sign to Invite.

### Room view extras (`DigiByte.Wallet`)

| Item | Rule |
|---|---|
| InviteProminent (`RoomStage.InviteProminent`) | Host, room open, present-or-signed < required |
| SignerOrder | You, then signed, then online, then offline |
| Countdown | Text; `IsUrgent` when ≤ 5:00; announced once on crossing |

### SharedWalletShortcut (`DigiByte.Wallet`)

`Target(lastUsedId, availableIds)` → last used if in `availableIds`; else the single id when there is exactly one; else none (open the list).

## Events

| Event | Raised by | Heard by |
|---|---|---|
| `MultisigWalletService.PendingChanged` (new) | Saving, removing or deleting pending items | `MultisigAttentionState` |
| `MultisigAttentionState.Changed` (new) | After each refresh | `NavMenu`, `Home`, list, dashboard |
| `ShellChrome.Changed` (new) | Room page | `MainLayout` |
