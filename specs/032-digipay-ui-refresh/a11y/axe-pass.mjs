// Spec 032 verification run (tasks T084 and T044; quickstart Q5 step 7 and Q6).
//
// Runs inside the official Playwright container against DigiPay running on the host:
//
//   docker run --rm -v <repo>/specs/032-digipay-ui-refresh/a11y:/out -w /out \
//     -e BASE=http://host.docker.internal:5252 -e TOKEN=<dashboard test token> -e SESSIONS=<ses_a,ses_b> \
//     mcr.microsoft.com/playwright:v1.48.0-jammy \
//     sh -c "npm i --no-audit --no-fund playwright@1.48.0 @axe-core/playwright@4.10.1 >/dev/null && node axe-pass.mjs"
//
// 1. axe-core (WCAG 2.0/2.1 A and AA rules) on every page type, light and dark. Fails on any
//    serious or critical finding (SC-003).
// 2. Checkout load on Chrome DevTools' "Fast 4G" profile (about 9 Mbps down, 150 ms latency),
//    375px viewport, cold cache, 20 runs: time until the amount and QR code are visible
//    (FR-013a, SC-011: within 2 s in at least 95% of runs).
import { chromium } from 'playwright';
import { AxeBuilder } from '@axe-core/playwright';
import { writeFile } from 'node:fs/promises';

const BASE = process.env.BASE ?? 'http://host.docker.internal:5252';
const TOKEN = process.env.TOKEN ?? '';
const SESSIONS = (process.env.SESSIONS ?? '').split(',').filter(Boolean);
const RUNS = Number(process.env.RUNS ?? 20);

const pages = [
    ['Home', '/'], ['Features', '/features'], ['About', '/about'], ['How it works', '/docs'],
    ['API reference', '/docs/api'], ['POS guide', '/docs/pos'], ['WooCommerce guide', '/docs/woocommerce'],
    ['SDKs', '/sdks'], ['Sign in', '/merchant/signin'], ['Not found', '/not-found'],
    ['Tip page', '/pay/button?to=dgb1q9gmf0pv8jdymcly6lz6fl7lf6mhslsd72e2jq8&amount=2.5&name=Creator'],
    ['POS', '/pos'],
    ...SESSIONS.flatMap((s, i) => [[`Checkout ${i + 1}`, `/pay/${s}`], [`Receipt ${i + 1}`, `/pay/${s}/receipt`]]),
    ...(TOKEN ? [
        ['Overview', '/merchant'], ['New payment', '/merchant/new'], ['Sessions', '/merchant/sessions'],
        ['Payment links', '/merchant/links'], ['Analytics', '/merchant/analytics'], ['Donation button', '/merchant/donation'],
        ['Webhook', '/merchant/webhook'], ['Stores', '/merchant/stores'], ['API keys', '/merchant/api-keys'],
        ['Audit log', '/merchant/audit'], ['POS mode', '/merchant/pos'], ['Settings', '/merchant/settings'],
    ] : []),
];

const browser = await chromium.launch({ headless: true });
const results = { base: BASE, ranAt: new Date().toISOString(), axe: [], timing: null };

// ---- 1. Accessibility --------------------------------------------------------------------
for (const theme of ['light', 'dark']) {
    for (const [name, path] of pages) {
        const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: theme });
        await context.addInitScript(([t, token]) => {
            try {
                localStorage.setItem('dp-theme', t);
                localStorage.setItem('digipay.pos.theme:v1', t);
                if (token) localStorage.setItem('digipay-token', token);
            } catch { }
        }, [theme, TOKEN]);
        const page = await context.newPage();
        let entry = { name, path, theme };
        try {
            await page.goto(BASE + path, { waitUntil: 'load', timeout: 30000 });
            await page.waitForSelector('main h1, main h2, [role=dialog]', { timeout: 15000 }).catch(() => { });
            await page.waitForTimeout(1500); // interactive render, entrance animations
            const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
            const bad = r.violations.filter(v => v.impact === 'serious' || v.impact === 'critical');
            entry.serious = bad.map(v => ({
                id: v.id, impact: v.impact, help: v.help,
                nodes: v.nodes.slice(0, 5).map(n => ({ target: n.target.join(' '), summary: (n.failureSummary || '').split('\n').slice(0, 3).join(' | ') })),
                count: v.nodes.length,
            }));
            entry.minorCount = r.violations.length - bad.length;
        } catch (e) {
            entry.error = String(e).slice(0, 300);
        }
        results.axe.push(entry);
        console.log(`${theme.padEnd(5)} ${name.padEnd(20)} ${entry.error ? 'ERROR ' + entry.error : (entry.serious.length ? 'SERIOUS: ' + entry.serious.map(v => `${v.id}(${v.count})`).join(', ') : 'ok')}`);
        await context.close();
    }
}

// ---- 2. Checkout speed on Fast 4G --------------------------------------------------------
if (SESSIONS.length && RUNS > 0) {
    const runs = [];
    for (let i = 0; i < RUNS; i++) {
        const context = await browser.newContext({ viewport: { width: 375, height: 812 } }); // fresh = cold cache
        const page = await context.newPage();
        const cdp = await context.newCDPSession(page);
        await cdp.send('Network.enable');
        await cdp.send('Network.setCacheDisabled', { cacheDisabled: true });
        await cdp.send('Network.emulateNetworkConditions', {
            offline: false,
            latency: 150,
            downloadThroughput: (9 * 1000 * 1000) / 8,
            uploadThroughput: (1.5 * 1000 * 1000) / 8,
        });
        const t0 = Date.now();
        await page.goto(`${BASE}/pay/${SESSIONS[i % SESSIONS.length]}`, { waitUntil: 'commit', timeout: 30000 });
        // "Usable": the amount is on screen (and, for a live payment, the QR code with it).
        await page.waitForSelector('[data-digipay-measure] .ui-checkout-big', { state: 'visible', timeout: 15000 });
        const visibleMs = Date.now() - t0;
        const fcp = await page.evaluate(() => performance.getEntriesByType('paint').find(p => p.name === 'first-contentful-paint')?.startTime ?? null);
        runs.push({ visibleMs, fcpMs: fcp === null ? null : Math.round(fcp) });
        await context.close();
    }
    const sorted = runs.map(r => r.visibleMs).sort((a, b) => a - b);
    const median = (sorted[Math.floor((sorted.length - 1) / 2)] + sorted[Math.ceil((sorted.length - 1) / 2)]) / 2;
    const within2s = runs.filter(r => r.visibleMs <= 2000).length;
    results.timing = { profile: 'Fast 4G: 9 Mbps down, 1.5 Mbps up, 150 ms latency; 375px; cache disabled', runs, median, max: sorted.at(-1), within2s, total: runs.length };
    console.log(`timing: median ${median} ms, max ${sorted.at(-1)} ms, ${within2s}/${runs.length} within 2 s`);
}

await browser.close();
await writeFile('/out/axe-results.json', JSON.stringify(results, null, 2));
const serious = results.axe.filter(e => e.serious?.length).length;
const errors = results.axe.filter(e => e.error).length;
console.log(`pages with serious/critical findings: ${serious}; errors: ${errors}`);
