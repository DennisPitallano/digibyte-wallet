# Data Model: Wallet Uses Our Own Node When Ready

All of this lives in memory for one session. Nothing new is stored on disk.

## Node Source Options (deployment settings)

Loaded from the runtime config ([contracts/runtime-config.md](contracts/runtime-config.md)).

| Field | Type | Default | Rule |
|---|---|---|---|
| enabled | bool | false | Off means today's behaviour and no health calls (FR-010) |
| apiUrl | absolute http(s) URL | none | Must be allowed by the page's CSP; invalid or empty counts as unavailable (FR-015). Used by the health check and the node blockchain client only; DigiDollar and the usage counter keep the existing `NodeApiUrl` |
| checkInterval | seconds | 30 | 10 to 300 |
| stablePeriod | seconds | 120 | At least two check intervals |
| heightTolerance | blocks | 5 | 1 to 50 |
| healthTimeout | seconds | 3 | 1 to 10 |
| devView | bool | false | Shows the diagnostics page when true (FR-013) |

Out-of-range values fall back to the default for that field.

## Node Readiness (one snapshot per check)

| Field | Meaning |
|---|---|
| checkedAt | When the check finished |
| reachable | The health request returned a readable answer within the timeout |
| status | `starting`, `syncing`, `ok`, `unhealthy` or unknown |
| nodeHeight | `blocks` from the health report |
| lag | `headers - blocks` |
| peers | Connected peers |
| txindex | Transaction index ready |
| versionOk | Node version at or above the deployment's recommended version (absent means true) |
| explorerHeight | Height from a public explorer, if one answered |
| heightDelta | `nodeHeight - explorerHeight` when both exist |
| ready | See the predicate below |
| reason | Why not ready: `disabled`, `unreachable`, `starting`, `syncing`, `unhealthy`, `txindex`, `version`, `height-drift`, `malformed`, `read-failures` |

**Ready predicate**: reachable, `status == ok`, `txindex`, `versionOk`, and `|heightDelta| <= heightTolerance`. A missing `explorerHeight` makes the check **inconclusive** (not ready, but not "wrong"), see the state rules.

## Active Source

| Field | Meaning |
|---|---|
| source | `own-node`, an explorer name, or `none` |
| why | `preferred`, `fallback`, `node-syncing`, `node-unavailable`, `all-failed`, `feature-off` |
| since | When this source became active |
| nodeWasActive | The node was `Active` earlier in this session (drives the banner, R8 in the research) |

## Monitor States and Transitions

| State | Entered when | Leaves when |
|---|---|---|
| Disabled | `enabled` is false | Never (config is read at startup) |
| Unavailable | The check failed, timed out or was unreadable | Next check is ready (to Stabilizing) or reachable but not ready (to NotReady) |
| NotReady(reason) | Reachable, not ready | A ready check (to Stabilizing); a failed check (to Unavailable) |
| Stabilizing | A ready check after a not-ready state | Ready for the stable period (to Active); any not-ready check (to NotReady or Unavailable) |
| Active | Stabilizing for the full stable period | Any not-ready or failed check, immediately; or 3 consecutive node read failures reported by the fallback service (`NotReady(read-failures)`), immediately |

An inconclusive check (no explorer height): Active stays Active; Stabilizing and below stay as they are.

## Comparison Report (developer view only)

| Field | Meaning |
|---|---|
| address | Address checked |
| nodeBalance / explorerBalance | In satoshis |
| nodeUtxoCount / explorerUtxoCount | Number of spendable outputs |
| nodeHeight / explorerHeight | Chain heights |
| matches | Balances and output counts equal, height within tolerance |
| error | Which side failed, if one did |
