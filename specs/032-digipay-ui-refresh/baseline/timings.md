# Baseline (T001, quickstart Q0)

Recorded on 2026-11-25 from `feature/digipay-ui-refresh` at commit `d8670da`. Only spec documents had changed since `main`, so the application code was identical to `main`.

## Checkout load time (before the restyle)

**Method**: The built-in browser loaded `/pay/{id}` in a 375×800 iframe. Each run used a fresh demo session (`POST /v1/pay/test/demo-session`). A run was timed from setting the iframe `src` until the QR image (`img[alt="Scan to pay"]`) had loaded. Setup:

- Pay.Web on http://localhost:5252.
- Pay.Api on http://localhost:5008, using SQLite (launch config `digipay-api-sqlite`).
- No network throttling.
- The browser cache was warm after the first run.

| Run | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| ms | 92 | 82 | 71 | 92 | 82 | 86 | 72 | 75 | 76 | 74 |

**Median 79 ms, max 92 ms.**

**Gap: no throttling.** These numbers are not throttled. The built-in browser can't apply the Chrome DevTools "Fast 4G" profile that FR-013a and SC-011 name, and no Playwright image is available locally, since pulling one needs your OK.

- **What they're good for**: comparing before and after on the same machine, which tests the "no slower than before" half of SC-011.
- **What's still owed**: the 2-second Fast 4G half still needs a throttled run (quickstart Q6).

## Checkout speed after the restyle (T044, quickstart Q6)

**Why the method changed.** After a few minutes hidden, the browser pane throttles timers to about once a second, so the timer-polling method above stopped being usable. Both builds were measured again with timers out of the loop:

- The pre-change build was run from a temporary worktree on port 5262.
- Both builds loaded the same four demo sessions, in a 375×800 iframe, with a warm cache.

| Build | Metric | Median | Max | Runs |
|---|---|---|---|---|
| Before the restyle | Payment card with the amount inserted (MutationObserver) | **76 ms** | 119 ms | 20 |
| Restyled, without prerendering | same | **94 ms** | 158 ms | 20 |
| Restyled, with the R8 fallback (shopper pages prerendered) | HTML with the amount received / render-blocking CSS done | **20 ms / 49 ms** | — | 12 |

**Without prerendering, the restyle was slower.** It took about 18 ms longer than before, which fails the "no slower than before" half of SC-011. So the research R8 fallback was applied:

- `/pay/*` pages are prerendered on the server.
- The session is handed to the interactive render with `PersistentComponentState`, so it doesn't flash "Loading…".

**With prerendering, it's faster than before.** The amount and QR code are in the first HTML and can paint as soon as the CSS has loaded, about 50 ms in, against 76 ms before. Every run came back prerendered.

**Throttled run done (T044).** On Fast 4G at 375px with a cold cache, the median was 1018 ms and 19/20 runs finished within 2 s. Details: [../a11y/README.md](../a11y/README.md).

## Wallet style gallery snapshot (for Q1)

**Method**: Instead of PNG screenshots, a computed-style fingerprint of `/test-animations`. For every element, the full `getComputedStyle` property list is serialised and hashed, at 375px and 1280px in the light and dark themes. Two runs match only when every element's computed style is identical, which is stricter than a pixel comparison of what happens to be on screen.

The fingerprints are in [gallery-fingerprint.json](gallery-fingerprint.json). They were taken from the pre-change wallet, run from a temporary worktree, and compared with the wallet after the extraction (T013). All four sizes and themes are identical.
