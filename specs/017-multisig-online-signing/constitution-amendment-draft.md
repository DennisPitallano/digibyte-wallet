# Draft: Constitution Amendment 1.1.0 → 1.2.0

**Status**: Applied to `.specify/memory/constitution.md` on 2026-11-17 (version 1.2.0). Not committed yet; the
constitution asks for amendments to go in their own PR.

**Why**: `specs/017-multisig-online-signing` adds signing rooms. Their hub calls change server state (open and close
rooms, record signatures, hand out broadcast claims) without the header-based key the Auth-boundary rule requires.
A key shipped in the public static wallet would be readable by anyone and add no protection. The real control is
proof that the caller holds a co-signer key. The existing creation-room hub (`/hubs/multisig-room`) has the same gap
today, and this amendment covers it too.

**Version bump**: MINOR, because the scope of existing rules is materially widened (a new permitted server role
and a new auth exception).

## Proposed changes

### 1. Principle II: In-Browser Cryptography, permitted server roles

Replace:

> Servers operated by this project MUST behave as static asset hosts, price/data proxies, public-chain relays, or
> public-contribution intake relays only; they MUST NEVER be a required participant in a signing flow.

With:

> Servers operated by this project MUST behave as static asset hosts, price/data proxies, public-chain relays,
> public-contribution intake relays, or co-signer coordination relays only; they MUST NEVER be a required
> participant in a signing flow.

Add after the intake-relay paragraph:

> A **co-signer coordination relay** passes public data (public keys, unsigned or partially signed transactions,
> signatures, display names, participant status) between the co-signers of a multisig in real time. It MUST NOT
> handle private keys, seeds, or passwords; MUST NOT be able to sign, alter, or broadcast a transaction by itself;
> MUST keep session data in memory only and discard it when the session ends; MUST NOT log keys, addresses,
> amounts, transaction data, invite codes, or names; MUST be rate-limited; and MUST NOT be a dependency of any core
> wallet flow. Every participant's wallet MUST independently check everything it receives. An equivalent offline
> path (share/import) MUST always exist.

### 2. Security & Custody Requirements: Auth boundary

Add to the end of the Auth-boundary bullet:

> Exception: a co-signer coordination relay (Principle II) MAY accept anonymous connections, provided a connection
> can neither see nor change session data until it presents a fresh, single-use proof that it holds one of the
> session's co-signer keys (a signature over a server nonce bound to that connection and to the purpose). That
> proof authorises only that connection, only for that session, and only until the connection closes;
> reconnecting requires a new proof. Failed attempts MUST be rate-limited without revealing whether a session
> exists.

_Revised 2026-11-17 (before commit): the first wording required a proof on every call, which the design does not
do; the proof now covers its connection's session until disconnect._

### 3. Sync Impact Report (top of file)

```text
- Version change: 1.1.0 → 1.2.0 (MINOR: new permitted server role and auth exception)
- Modified principles: II. In-Browser Cryptography (adds co-signer coordination relay)
- Modified sections: Security & Custody Requirements → Auth boundary (key-possession-proof exception)
- Motivation: specs/017-multisig-online-signing (signing rooms); also brings the existing
  multisig creation-room hub into compliance
- Templates requiring updates: none
- Follow-up: creation-room hub should adopt key-possession proof (separate change)
```

Update the footer to `**Version**: 1.2.0 | **Ratified**: 2026-09-27 | **Last Amended**: <merge date>`.
