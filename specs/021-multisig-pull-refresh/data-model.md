# Data Model: Pull-Down Refresh on Multi-Sig Dashboard

No persistent or new data entities. Page-local transient state only:

| State | Type | Purpose |
|---|---|---|
| `isRefreshing` | bool (new) | True while a refresh (pull or button) runs; later triggers are ignored (FR-006) |
| `balanceSatoshis` | long? (existing) | Last known balance; kept when a pull refresh fails (FR-007) |
| `recentActivity` / `activityPartial` | list / bool (existing) | Last known activity; kept when a pull refresh fails |

Transition: idle -> refreshing -> idle. On success the values are replaced. On failure the previous values are retained and an error toast is shown.
