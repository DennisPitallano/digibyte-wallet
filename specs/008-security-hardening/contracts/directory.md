# Contract: `/api/directory`

| Route | Hotfix state |
|---|---|
| `GET /lookup/{username}` | Unchanged |
| `GET /lookup/phone/{phone}` | Unchanged |
| `POST /register` | **Removed** (404/405) |
| `DELETE /{username}` | **Removed** (404/405) |

Future registration (FR-002) will be a separate contract: a Digi-ID style signature from the entry's address is required to create, update or delete. The address and network are validated, and the store is durable with a unique index.
