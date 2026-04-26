# Security policy

DigiByte Wallet, DigiPay, and the SDKs in this repository hold real funds on
behalf of real users. We take security reports seriously and aim to respond
quickly.

## Reporting a vulnerability

**Please report security issues privately**, not as public GitHub issues.

The fastest path is the [GitHub Security Advisories][gha] form on this
repository — it gives us a private, audited channel and pings the maintainer
on receipt:

  https://github.com/DennisPitallano/digibyte-wallet/security/advisories/new

If you can't use the form (e.g. you don't have a GitHub account), email the
maintainer directly via the contact link on
<https://dennispitallano.github.io>. Mark the message **"DigiByte Wallet —
security report"** in the subject so it routes correctly.

## What to include

- A clear description of the issue and its impact.
- Steps to reproduce, including any payloads, URLs, or test transactions.
- The affected component (wallet origin `dgbwallet.app`, DigiPay API, a
  specific SDK, etc.) and version / commit if you can identify it.
- Your suggested fix, if you have one.

We'll acknowledge receipt within **3 working days** and aim to confirm or
disprove the issue within **10 working days**. We'll keep you in the loop on
remediation timelines, and credit you in the advisory unless you ask us not
to.

## Scope

The following are in scope:

- The DigiByte wallet web app (`dgbwallet.app`) and its source under
  `src/DigiByte.Web/`, `src/DigiByte.Wallet/`, `src/DigiByte.Crypto/`.
- DigiPay API + dashboard (`pay.dgbwallet.app`) and its source under
  `src/DigiByte.Pay.Api/`, `src/DigiByte.Pay.Web/`.
- Official SDKs in this repo: `sdk/node/`, `sdk/python/`, `sdk/dotnet/`.
- Sample apps under `samples/` — but only when the issue would also affect
  production users following the patterns.
- Build / release pipeline (GitHub Actions) and the published packages
  (`@dgbwallet/digipay`, `digipay`, `DigiPay`).

The following are explicitly **out of scope**:

- The DigiByte chain itself, mining, consensus, or P2P networking — those
  belong upstream at <https://github.com/DigiByte-Core>.
- Third-party services we depend on but don't operate: Google Analytics,
  CoinGecko, DigiExplorer, GitHub, Railway, Postgres providers.
- Self-inflicted damage on a user's machine: malware, browser extensions,
  shoulder-surfing of seed phrases, OS-level keyloggers.
- Social engineering of users into entering their seed on a phishing site
  that *looks* like ours but isn't on the `dgbwallet.app` origin.
- Theoretical attacks against AES-256-GCM, secp256k1, PBKDF2-SHA256, or
  BIP39 / BIP84 themselves.

## Disclosure

We follow a **coordinated disclosure** model. Once a fix is shipped to
production, we'll publish the advisory (with credit) within ~30 days so the
ecosystem can learn from it. If a fix needs longer than 90 days, we'll let
you know and explain why; if you disagree with the timeline, we'd rather
hear about it before disclosure than after.

For issues that require an emergency push (active exploitation, fund loss
in progress), we may ship the fix immediately and publish the advisory at
the same time.

## What we will not do

- We will not pursue legal action against good-faith researchers acting
  within this policy.
- We will not run a paid bug bounty at this time. We do publicly credit
  reporters in advisories and release notes; if you'd like the credit
  removed or replaced with a pseudonym, just say so.

## Verifying what you're running

Self-custodial wallets ask users to trust that the JavaScript loaded by
their browser actually matches the open-source code. The
[`SELF_CUSTODY.md`](SELF_CUSTODY.md) document walks through what the wallet
does and doesn't send to the server, where keys live, and how to verify.
For the deeper architectural and reproducibility-build review, see
[`docs/walletscrutiny-self-eval.md`](docs/walletscrutiny-self-eval.md).

[gha]: https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability
