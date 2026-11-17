# Research: Remove Cancelled Proposals

No open unknowns. Decisions taken:

- **Delete vs hide**: delete the stored record. *Rationale*: the clarification chose "removed everywhere on this device", and the Activity feed (`GetActivityAsync`) reads the same stored list, so deleting gives that for free. *Alternative*: a "hidden" flag on the proposal, rejected as extra state for no requested benefit.
- **Which states are removable**: `Cancelled` only. *Rationale*: "Cancelled or expired" in the UI maps to this single status (`PendingStateClassifier`); open, failed and broadcast proposals still matter. 
- **Confirmation**: bulk action confirms inline (same pattern as the existing cancel confirmation); single removal is instant. *Rationale*: matches the clarification; reuses an established pattern.
- **Re-appearance**: a co-signer re-sharing the proposal re-creates it as a new proposal; accepted and listed as an edge case.
- **Sync**: no cross-device effect; storage is per device.
