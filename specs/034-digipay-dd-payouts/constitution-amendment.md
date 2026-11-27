# Draft: constitution amendment for DigiPay-held DigiDollar (specs/034 research R12, task T003)

This is a draft for a **separate** pull request against `.specify/memory/constitution.md`. Nothing in spec 034 edits the constitution. It should be agreed before spec 034's payout code merges.

## Why

The constitution's Principle I protects wallet users' keys and seeds, and Principle II limits what this project's servers may do with secret material. Neither speaks to a project server **holding merchant funds**. DigiPay already does that for DigiDollar (DD): DD payment addresses come from a node wallet DigiPay runs, because DD addresses can't yet be derived from a merchant's key. Spec 034 reduces that custody (DD is paid out as soon as it's confirmed), but the rule a reviewer can block on should be written down.

## Proposed version change

1.2.0 → **1.3.0** (MINOR: a principle's scope is materially expanded to cover a new class of change).

## Proposed text

Add to **Security & Custody Requirements**:

> - **DigiPay custodial DigiDollar**: DigiPay MAY hold DigiDollar paid to a merchant's store only because DD addresses can't yet be derived from the merchant's own key, and only until it is paid out. While it holds DD, DigiPay MUST:
>   - pay it out to the merchant's own DD payout address by default as soon as the payment has the required confirmations, and never hold it longer than the merchant's chosen schedule allows (at most 7 days for a balance threshold);
>   - show each merchant, per store, how much it holds, why it isn't being paid out, and every payout;
>   - keep its DD wallet covering everything it owes every merchant before any payout is sent, so one merchant's DD is never used for another, and make good from its own funds any DD lost after it was paid out;
>   - require a fresh, purpose-bound Digi-ID sign-in to set or change a payout address, hold a change for at least 48 hours with payouts held, record every change in the audit log, and tell the merchant on every Dashboard page and by webhook;
>   - never let API keys set or change a payout address, and never let network position authorise payout operations;
>   - describe the custody plainly wherever DD is offered, and never call DigiDollar on DigiPay non-custodial.
>
>   This exception covers DigiPay merchants' DigiDollar only. It never permits holding wallet users' keys, seeds or funds (Principle I). It ends when DD can be paid straight to merchant-derived addresses, at which point it MUST be removed.

## Sync Impact Report (for the amendment PR)

- Version change: 1.2.0 → 1.3.0
- Modified sections: Security & Custody Requirements (new "DigiPay custodial DigiDollar" item)
- Principles unchanged: I–V
- Motivation: specs/033-digipay-dd-everywhere and specs/034-digipay-dd-payouts
- Templates requiring updates: none
- Follow-up: remove the exception once merchant-derived DD addresses exist
