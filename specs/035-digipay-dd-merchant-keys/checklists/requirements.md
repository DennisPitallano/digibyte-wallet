# Specification Quality Checklist: DigiDollar on the merchant's own keys, without a node

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

- The derivation path (`m/86'/20'/0'`) and the explorer (digiexplorer.info) appear only under Assumptions, as the user named them. Requirements speak of "the store's DD key" and "the public explorer".
- Decisions taken as defaults instead of clarification markers:
  - The key is the wallet's existing DigiDollar account, not a new account dedicated to DigiPay.
  - The donation address uses one reserved index.
  - The wrong-account case is caught by the first-address check, since an account key doesn't record its path.
  - DD payments created while the explorer is down are still created.
- Worth confirming in `/speckit-clarify` if wanted: whether a store should get a dedicated DigiDollar account (privacy between stores sharing one wallet) rather than the wallet's main one.
