# Contract: Status UI

Wording is draft; final copy is checked during implementation. Plain language, no jargon. The strings are English only for now, like the existing offline banner, and live in one place so translations (`en` and `es` locale files exist) can be added later without changing the logic.

## Small status indicator

A small dot in the layout, always present only when the feature is on. Opens the detail panel. Nothing else changes on the main screen.

| State | Dot |
|---|---|
| Own node active | Green |
| Public explorers in use (normal, including while the node syncs) | Neutral |
| Real degradation (banner showing) | Amber |
| Nothing available | Red |

## Detail panel (opened on demand)

| Situation | Text |
|---|---|
| Own node active | "Using the DigiByte node run by the wallet team." plus the privacy line below |
| Explorer in use, node syncing | "Using public block explorers. The wallet team's own node is still syncing and will be used automatically when it is ready." |
| Explorer in use, node not reachable | "Using public block explorers. The wallet team's own node is not available right now." |
| Explorer in use, node behind or on a different chain | "Using public block explorers. The wallet team's own node is out of step with the network and is not being used." |
| Feature off | "Using public block explorers." |
| Nothing available | "No data source is reachable. Showing your last known balance, which may be out of date." |

**Privacy line** (own node active only, FR-014a): "The wallet team's servers, not a third-party explorer, see the public addresses this wallet looks up. Your keys never leave your device."

## Banner (degradation only, never during normal syncing)

| Trigger | Text |
|---|---|
| The node was active this session and the wallet fell back to explorers | "The wallet team's node is unavailable. Using public explorers instead." |
| Every source failed | "Can't reach the blockchain right now. Showing your last known balance, which may be out of date. Sending is unavailable until the connection returns." |

Both clear by themselves when the condition ends. The existing offline banner keeps its own behaviour.

## Developer view (only when `OwnNode:DevView` is true)

A page with:

- The latest readiness snapshot (status, lag, peers, version, transaction index, heights, delta, ready or the reason).
- A list of recent state changes with times.
- A form: enter addresses, run a comparison, and see per-address match results (see the Comparison Report in the data model).

Not linked from normal navigation unless the setting is on; with the setting off the route shows the normal "not found" page.
