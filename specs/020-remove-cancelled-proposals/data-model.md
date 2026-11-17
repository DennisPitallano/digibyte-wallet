# Data Model: Remove Cancelled Proposals

No new data. One existing entity is affected.

**PendingMultisigTransaction** (stored per wallet as a JSON list): has a status of AwaitingSignatures, ReadyToFinalize, Failed, Broadcast or **Cancelled**, plus the cancelled time.

**Rule added**: a proposal in the Cancelled state may be deleted from the list. Deleting is permanent on this device; there is no transition back out of Cancelled.

**Effects**: deleted proposals no longer appear in Pending or in Activity. Chain transactions in Activity are separate data and unaffected.
