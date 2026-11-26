# Specification Quality Checklist: DigiDollar payouts to merchants

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-27
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Passed on the first check.
- Digi-ID, webhooks, API keys and the audit log are named because they are existing DigiPay features merchants already use, not implementation choices. Background and Constitution Alignment name today's DD design only to explain the custody problem; requirements stay at the behaviour level.
- No [NEEDS CLARIFICATION] markers. Clarify (2026-11-27, 5 questions) confirmed the 48-hour wait with payouts held (FR-005, FR-006), requiring a payout address to turn DigiDollar on (FR-031), the merchant refunding extra buyer payments, "as soon as confirmed" as the default (FR-011), and DigiPay absorbing reversals (FR-023).
- Changed during planning and analysis: FR-015 (DD can't be sent below 1.00 DD, so a store-deletion remainder moves to the merchant-level balance), FR-014 (6 network confirmations before payout), and the daily-payout time-zone default (Assumptions). Re-checked after each change, and all items still pass.
- Constitution check: Principles I–V and the Security & Custody section were reviewed (see the spec's Constitution Alignment section and the plan's Constitution Check). The constitution doesn't address DigiPay holding merchant funds; the plan records this and tasks.md drafts an amendment in Setup. Address-change auth is security-relevant and must be flagged in its PR.
- `.specify/feature.json` still points at 033, because this work shared a checkout with the 033 session. Point it at `specs/034-digipay-dd-payouts` before running `/speckit-implement` on 034.
