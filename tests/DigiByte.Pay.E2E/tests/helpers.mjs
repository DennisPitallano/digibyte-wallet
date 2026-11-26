// Shared helpers for the DigiPay journeys: API calls against the sandbox and an axe runner.
import { expect, request } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { apiUrl } from '../servers.mjs';

export { apiUrl };

/** A request context pointed at Pay.Api. */
export async function apiContext() {
    return request.newContext({ baseURL: apiUrl });
}

async function json(res, what) {
    const body = await res.text();
    expect(res.ok(), `${what} → ${res.status()} ${body}`).toBeTruthy();
    return body ? JSON.parse(body) : null;
}

/**
 * Signs in as the fixed Dev merchant through the API (Development + DigiPay:DevSignIn only).
 * The token is cached per worker: /v1/pay/dev/* allows 10 requests a minute per IP.
 */
let cachedSignin;
export async function devSignin(api) {
    if (!cachedSignin) {
        cachedSignin = await json(await api.post('/v1/pay/dev/signin'), 'dev sign-in');
    }
    return cachedSignin;
}

export const bearer = (token) => ({ Authorization: `Bearer ${token}` });

/** Waits for every finite CSS animation/transition on the page to end (spinners loop forever and are skipped). */
export async function settleAnimations(page) {
    await page.waitForFunction(() =>
        document.getAnimations().every((a) =>
            a.playState !== 'running' || a.effect?.getComputedTiming().iterations === Infinity));
}

/** Puts a signed-in merchant into the page's storage before any script runs (as the sign-in page would). */
export async function seedMerchant(page, { token, merchantId }) {
    await page.addInitScript(([t, m]) => {
        localStorage.setItem('digipay-token', t);
        localStorage.setItem('digipay-merchant-id', m);
    }, [token, merchantId]);
}

/** Creates a DGB payment on the dev store. Returns the session (id, checkoutUrl, ...). */
export async function createSession(api, token, { amount = 10, label = 'E2E order' } = {}) {
    return json(await api.post('/v1/pay/sessions', { headers: bearer(token), data: { amount, label } }), 'create session');
}

/** Moves a testnet session along (the sandbox; fires live updates and webhooks like a real payment). */
export async function advance(api, token, sessionId, body) {
    return json(
        await api.post(`/v1/pay/test/sessions/${encodeURIComponent(sessionId)}/advance`, {
            headers: bearer(token),
            ...(body ? { data: body } : {}),
        }),
        `advance ${sessionId} ${JSON.stringify(body ?? {})}`,
    );
}

/** The newest sessions for the signed-in merchant (GET /v1/pay/sessions accepts an array or { sessions }). */
export async function latestSessions(api, token, take = 1) {
    const body = await json(await api.get(`/v1/pay/sessions?take=${take}`, { headers: bearer(token) }), 'list sessions');
    return Array.isArray(body) ? body : body.sessions ?? body.items ?? body.data ?? [];
}

/**
 * Runs axe (WCAG 2.x A/AA) and fails on serious or critical violations.
 * `disableRules` is for documented, per-page exceptions only.
 * `[id^="mc-widget"]` is a browser extension's injected element on the dev machine, not the app.
 */
export async function expectNoSeriousA11yViolations(page, { disableRules = [], exclude = [] } = {}) {
    // Let entrance animations (fade/slide-up) finish first: half-faded text reads as a contrast failure.
    await settleAnimations(page);
    let builder = new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        .exclude('[id^="mc-widget"]');
    for (const sel of exclude) builder = builder.exclude(sel);
    if (disableRules.length) builder = builder.disableRules(disableRules);
    const results = await builder.analyze();
    const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
    const report = serious
        .map((v) => `${v.impact} ${v.id}: ${v.help}\n${v.nodes.slice(0, 5).map((n) => `   ${n.target.join(' ')}  ${n.failureSummary?.split('\n').slice(1, 2).join('')}`).join('\n')}`)
        .join('\n');
    expect(serious.map((v) => v.id), `axe violations on ${page.url()}:\n${report}`).toEqual([]);
}
