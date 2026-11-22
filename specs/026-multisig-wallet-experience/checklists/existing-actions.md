# Existing Actions Checklist: Multisig Wallet Experience

**Purpose**: Every action a person can take on the multisig screens before this feature must still be possible after it (FR-039, SC-012). Walk this list after each story and at the end (task T090).
**Created**: 2026-11-23
**Feature**: [spec.md](../spec.md)

"Today" is where the action lives on `main` before the feature. "After" is where it must be found once the feature is complete. Mark "Still works" during the walkthrough.

## Shared wallet list (`/multisig`)

| Action | Today | After | Still works |
|---|---|---|---|
| Open a shared wallet | Card on the list | Card on the list | |
| Create a shared wallet | "+" button in the header | "Create" button | |
| Import a shared wallet | "Import Existing Multisig Wallet" link | "Import" button | |
| Rename a shared wallet | Pencil on the card | Card's "more" button | |
| Continue a draft | "Continue" on the draft | Same, restyled | |
| Rename a draft | Tap the draft's name | Same, restyled | |
| Delete a draft (with confirm) | "Delete" on the draft | Same, restyled | |
| Claim an unassigned shared wallet | "Claim" in the unassigned box | Same, restyled | |
| Dismiss the unassigned box | "Not now" | Same, restyled | |

## Dashboard (`/multisig/{id}`)

| Action | Today | After | Still works |
|---|---|---|---|
| Rename the shared wallet | Tap the title | Tap the title | |
| Refresh the balance / pull to refresh | Balance card, pull down | Unchanged | |
| Hide or show the balance | Eye on the balance card | Unchanged | |
| Copy the address / show the receive QR | Balance card, Receive tile | Unchanged | |
| Start a payment | Send tile | Send tile | |
| Scan a co-signer's proposal | Scan tile | Menu's Scan button | |
| Open Pending | Pending tile, "needs your signature" banner | Menu's Pending tab, banner | |
| Open Activity | Activity tile, "View all" | Menu's Activity tab, "View all" | |
| Back up the redeem script (copy, download, QR) | "Redeem script & backup" panel | Unchanged | |
| See and verify a co-signer's key, rename a co-signer | "Co-signers" panel | Unchanged | |
| Open an activity entry | Recent activity list | Unchanged | |
| Remove the shared wallet (with confirm) | Link at the bottom | Unchanged | |

## Pending (`/multisig/{id}/pending`)

| Action | Today | After | Still works |
|---|---|---|---|
| Add a proposal by scanning a QR | "Scan QR" at the top | "Add a proposal" sheet | |
| Add a proposal by pasting | "Paste" at the top | "Add a proposal" sheet | |
| Add a proposal from a file | "Choose file" at the top | "Add a proposal" sheet | |
| Join a room with a code | Button at the top | "Add a proposal" sheet | |
| Restore a proposal that was cancelled here | Prompt after adding it again | Same prompt | |
| Open the other shared wallet a proposal belongs to | Prompt after adding it | Same prompt | |
| Sign a payment (with confirm) | "Sign" on the card | Payment's own screen | |
| Broadcast (slide) | Slide on the card | Payment's own screen | |
| Retry a failed broadcast | "Retry broadcast" on the card | Payment's own screen | |
| Sign together online: open a room (length, name) | Panel on the card | Payment's own screen | |
| Open the live room for a payment | "Open room" on the card | "Open room" on the card | |
| Share: copy PSBT | Share panel on the card | Payment's own screen | |
| Share: save as file | Share panel on the card | Payment's own screen | |
| Share: show QR | Share panel on the card | Payment's own screen | |
| Import a signature by pasting | Import panel on the card | Payment's own screen | |
| Import a signature by scanning | Import panel on the card | Payment's own screen | |
| Import a signature from a file | Import panel on the card | Payment's own screen | |
| Cancel a proposal (with confirm) | "Cancel" on the card | Payment's own screen | |
| Remove one cancelled proposal | "Remove from list" on the card | Payment's own screen | |
| Clear all cancelled (with confirm) | Link above the list | Inside the Cancelled group | |
| Create a payment from an empty list | Empty state button | Empty state button | |

## Signing room (`/multisig/sign-room`)

| Action | Today | After | Still works |
|---|---|---|---|
| Join with a typed code and a name | Join card | Join screen | |
| Join from an invite link (code prefilled, unlock first if needed) | Link opens the page | Unchanged | |
| Switch to or unlock another wallet and retry | Buttons under the join card | Join screen | |
| Review and sign (with confirm) | Headline button | Pinned button | |
| Decline to sign (with confirm) | Link under the headline | Beside the review | |
| Broadcast | Headline button | Pinned button | |
| Copy the invite link / copy the code / show the QR | Invite card at the bottom | Invite under the headline, or folded row | |
| Copy the transaction ID after broadcast | Green box | Result screen | |
| Leave the room | Link at the bottom | Room menu, Back | |
| Close the room for everyone (host, with confirm) | Link at the bottom | Room menu | |
| Go on to Pending after the room ends | Button on the ended card | "Open payment" on the result screen | |

## Send, Activity, Create, Import

| Action | Today | After | Still works |
|---|---|---|---|
| Create a payment (recipient, amount, note, fee, review) | `/multisig/{id}/send` | Unchanged, plus a scanned recipient can be filled in | |
| Scan a recipient from the send form | Scan button in the form | Unchanged | |
| Filter and open activity entries | `/multisig/{id}/activity` | Unchanged | |
| Create online in a room / with manual keys / save a draft | `/multisig/create`, `/multisig/room` | Unchanged | |
| Import from a redeem script (paste, scan) | `/multisig/import` | Unchanged | |

## Notes

- Record the date and the build each time the list is walked.
