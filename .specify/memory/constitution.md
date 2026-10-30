<!--
Sync Impact Report — 2026-10-30
- Version change: 1.0.0 → 1.1.0 (MINOR: scope of existing rules materially expanded)
- Modified principles:
  * II. In-Browser Cryptography — permitted server roles now include a
    "public-contribution intake relay", with mandatory limits
- Modified sections:
  * Security & Custody Requirements → Content Security Policy: adds a narrow,
    path-scoped exception for the project's own GitHub raw content
  * Security & Custody Requirements → Auth boundary: adds a narrow exception for
    anonymous intake endpoints that can only create reviewable items
- Added sections: none
- Removed sections: none
- Motivation: specs/006-sponsors-page (donor-facing sponsors list read live from the
  project's own repository, and an in-wallet submission form for donors without a
  GitHub account). No key, seed, or signing flow is touched by this amendment.
- Templates requiring updates: none (plan-template.md Constitution Check reads this
  file directly)
- Deferred / TODOs: none
- Previous report (2026-09-27): initial ratification, version 1.0.0
- Note: this scratch report is intended for human review of the amendment; remove
  before committing the amended constitution if the reviewer prefers a clean file.
-->

# DigiByte Wallet Constitution

## Core Principles

### I. Self-Custody Is Non-Negotiable

Users MUST at all times remain in sole control of their private keys and seed material.
The seed phrase, its encrypted representation, the wallet password, and any derived
private key MUST NOT leave the user's device by any channel operated by this project
(no telemetry field, no error report, no backup service, no support tool). Any feature
that would require the operator to hold, escrow, or recover user keys is out of scope
by default and MUST be escalated as a constitutional amendment before design begins.

**Rationale**: This is the wallet's public identity ("your seed, your funds, no
exceptions"), documented in [SELF_CUSTODY.md](../../SELF_CUSTODY.md). Silently walking
it back would betray existing users and invalidate audits. Every downstream feature —
including DigiDollar send in `specs/001-digidollar-wallet/` — inherits this constraint.

### II. In-Browser Cryptography

All cryptographic operations that touch a user's secret material (mnemonic generation,
key derivation, encryption/decryption of the seed store, transaction signing, Digi-ID
signing) MUST run inside the browser, in the WebAssembly sandbox and WebCrypto APIs
already used by `DigiByte.Web` / `DigiByte.Crypto`. Servers operated by this project
MUST behave as static asset hosts, price/data proxies, public-chain relays, or
public-contribution intake relays only; they MUST NEVER be a required participant in a
signing flow.

A **public-contribution intake relay** accepts non-secret, public submissions (for
example a sponsor-listing request) and forwards them as reviewable items to a
project-owned repository. It MUST NOT handle keys, seeds, passwords, or signatures; MUST
hold its forwarding credential only in server configuration, scoped to this project's
repository with the least privilege needed to create review items (never to merge or
publish); MUST validate, rate-limit, and size-limit every submission; MUST NOT publish
anything without maintainer review; and MUST NOT be a dependency of any core wallet flow
(balance, receive, send, history).

**Rationale**: This is what makes Principle I enforceable rather than a promise.
A server that never sees keys cannot leak keys. The intake-relay role is allowed because
it carries only public text and can only create review items, so it adds no custody or
signing risk. The WebAssembly / WebCrypto pipeline
is documented in [SELF_CUSTODY.md](../../SELF_CUSTODY.md) and the `docs/walletscrutiny-*`
audits; any change to it is a security-relevant change.

### III. Test-First for Crypto & Wallet Logic (NON-NEGOTIABLE)

Every change to `DigiByte.Crypto/` and `DigiByte.Wallet/` MUST be accompanied by unit
tests in the matching `tests/DigiByte.Crypto.Tests/` or `tests/DigiByte.Wallet.Tests/`
project. Tests MUST be written or updated in the same pull request as the code change,
MUST fail before the code change is applied (or cover a previously untested branch),
and MUST pass before merge. Address validators, key derivation paths, signing paths,
and encryption/decryption paths MUST have negative-path tests (wrong network, invalid
checksum, wrong password, truncated input) in addition to happy-path tests.

**Rationale**: These modules protect real user funds. A regression here can lose
someone's savings; a slower change velocity is an acceptable trade for that. This
principle extends the existing CONTRIBUTING.md guidance ("Write unit tests for
crypto/wallet logic") into a merge gate.

### IV. Boundary Discipline Across Projects

The monorepo's project boundaries are load-bearing and MUST be preserved:
`DigiByte.Crypto` is dependency-free of higher layers; `DigiByte.Wallet` depends on
`DigiByte.Crypto` only; `DigiByte.Web` depends on `DigiByte.Wallet` and
`DigiByte.Crypto`; `DigiByte.Api`, `DigiByte.NodeApi`, `DigiByte.Pay.Api`, and
`DigiByte.Pay.Web` remain independently deployable services. Cross-cutting behavior
(fee estimation, address validation, price fetching, network health) MUST live in the
lowest layer that owns the concept, not be duplicated per consumer. Introducing a new
reference direction that inverts these dependencies MUST be treated as a constitutional
amendment.

**Rationale**: The layered structure — described in the README and
`docs/ARCHITECTURE.md` — is what lets the wallet ship as a static PWA independent of
the backends, what lets DigiPay run without the wallet, and what makes each service
individually auditable. Blurring the boundaries costs all three of those properties.

### V. Resilience & Graceful Degradation

User-facing surfaces MUST keep working, in a clearly-labeled degraded mode, when a
non-critical backend or third-party dependency is unavailable. Existing patterns —
multi-explorer fallback, Polly-based retry / circuit-breaker / timeout in the wallet's
HTTP clients, PWA offline caching, and cached balance/history views — are the baseline
and MUST NOT be regressed. New features that introduce a backend dependency MUST
define, in their spec, what the surface looks like when that dependency is unreachable,
and MUST NOT block DGB core flows (balance, receive, send, history) on the new
dependency's health.

**Rationale**: The wallet holds real funds and is used from mobile browsers on
unreliable networks. Users who cannot see or move DGB during a partial outage lose
trust regardless of who is actually at fault. This principle turns "the app still
works" from a nice-to-have into a review criterion.

## Security & Custody Requirements

- **Vulnerability reporting**: Follow [SECURITY.md](../../SECURITY.md). Public GitHub
  issues MUST NOT be used for security reports; the private advisory channel is
  mandatory. Response SLA (3 working days acknowledgement, 10 working days
  confirm/disprove) is a project commitment.
- **Content Security Policy**: All CSS, JS, fonts, and images shipped with the wallet
  MUST be self-hosted. Third-party CDNs are prohibited from the wallet origin. The
  strict CSP configured in `DigiByte.Web` is the baseline and MUST NOT be relaxed to
  ship a feature. The only permitted exception is a path-scoped allowance, in `img-src`
  and `connect-src`, for content this project itself publishes in its own GitHub
  repository through `raw.githubusercontent.com` (for example
  `https://raw.githubusercontent.com/<owner>/<repo>/<branch>/`). Allowing a whole host
  or another repository remains prohibited, and any such allowance MUST be flagged as
  security-relevant in the pull request.
- **Secret hygiene**: Wallet passwords, seed material, private keys, API bearer tokens
  (`dgp_*`), and node RPC credentials MUST NOT appear in logs, telemetry, error
  reports, or committed configuration. Server code MUST use environment variables /
  secret stores; client code MUST NOT persist secrets outside the encrypted IndexedDB
  store already in use.
- **Address handling**: Every user-entered address (DGB, DigiDollar, multisig,
  Digi-ID) MUST be validated client-side with the appropriate network-specific
  version-byte and checksum check before being displayed, saved, or used as a
  transaction destination.
- **Auth boundary**: DigiPay merchant APIs are authenticated via `dgp_*` bearer keys;
  wallet-to-backend calls that mutate state MUST use an equivalent header-based key
  and MUST NOT rely on network position for authorization. Exception: a
  public-contribution intake endpoint (Principle II) MAY be anonymous where a key
  shipped in the static client would be public and add no protection, provided it can
  only create a review item that cannot publish, move funds, or touch keys or merchant
  data, and provided it is rate-limited, size-limited, deduplicated, validated against
  the same rules as the client, and its forwarding credential cannot merge or publish.

## Development Workflow & Quality Gates

- **Branching**: Feature branches follow `feature/<slug>` (per CONTRIBUTING.md).
  Spec-Kit feature directories (`specs/NNN-<slug>/`) MAY share the slug but are
  independent of the git branch name.
- **Conventional Commits**: All commit messages MUST use Conventional Commits
  (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`). PR titles inherit the
  same convention. This is what feeds release automation and CHANGELOG generation.
- **Merge gates**: Every pull request MUST pass, at minimum, `dotnet build` and
  `dotnet test` across the affected test projects. Crypto or wallet-behavior changes
  MUST include tests per Principle III. Security-relevant changes (dependency updates
  in `DigiByte.Crypto`, changes to CSP, changes to auth) MUST be flagged in the PR
  description.
- **Spec-Kit workflow**: Non-trivial user-visible features MUST start with a
  `/speckit-specify` spec under `specs/NNN-<slug>/`, followed by `/speckit-clarify`
  (or an equivalent design doc) before implementation begins. `plan.md` and
  `tasks.md` produced by `/speckit-plan` and `/speckit-tasks` are the source of truth
  for scope; scope creep during implementation MUST be reflected back into these
  artifacts, not merged silently.
- **Documentation**: Behaviour changes visible to end users, integrators, or
  merchants MUST update the appropriate doc in `docs/` (`ARCHITECTURE.md`,
  `PROCESS_FLOWS.md`, `ROADMAP.md`) or the affected SDK README in the same PR.

## Governance

This constitution supersedes prior informal practice and short-lived conventions for
this repository. Where it conflicts with `CONTRIBUTING.md`, `SELF_CUSTODY.md`, or a
per-feature `spec.md`, this constitution wins and the other document MUST be updated
in the same PR that surfaces the conflict.

- **Compliance check**: Every pull-request review MUST confirm compliance with the
  Core Principles that apply to the change. Reviewers MAY request evidence (test
  output, spec link, security note) and MAY block on unaddressed principle violations.
- **Amendments**: Amendments MUST be proposed as a PR touching this file, MUST update
  the version per the versioning policy below, and MUST include a Sync Impact Report
  at the top of the file summarizing the change for reviewers.
- **Versioning policy** (semantic):
  * **MAJOR**: A core principle is removed or materially weakened, or a governance
    rule that gated merges is dropped.
  * **MINOR**: A new principle is added, or a principle's scope is materially
    expanded (a new class of change now covered by the principle).
  * **PATCH**: Clarifications, wording fixes, or non-semantic tightening of language.
- **Ratification and amendment dates**: Set on ratification; the amendment date is
  updated any time the file changes.
- **Runtime guidance**: For per-language / per-project style rules (naming, folder
  conventions, Tailwind usage, Blazor component sizing), refer to the existing
  `CONTRIBUTING.md` — that document is the runtime guide; this constitution is the
  set of rules a reviewer can block a PR on.

**Version**: 1.1.0 | **Ratified**: 2026-09-27 | **Last Amended**: 2026-10-30
