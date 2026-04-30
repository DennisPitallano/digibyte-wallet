// Headless capture script for the WooCommerce plugin docs.
//
// Drives Chromium through the running stack and writes 8 PNGs straight into
// this folder. Runs inside the official Playwright container so installs are
// deterministic across machines:
//
//   docker run --rm \
//       --network digipay-wc-net \
//       -v <repo>/samples/woocommerce-plugin/docs/screenshots:/out \
//       mcr.microsoft.com/playwright:v1.48.0-jammy \
//       node /out/capture.mjs
//
// (The repo's screenshots/README.md has the canonical command, including a
// cross-network pass-through for Pay.Api on the host.)
//
// Filenames match the README references exactly — overwrite-in-place so
// re-running captures replaces the existing PNGs.

import { chromium } from 'playwright';
import { writeFile } from 'node:fs/promises';

const WP   = process.env.WP_URL   ?? 'http://digipay-wc';
const PAY  = process.env.PAY_URL  ?? 'http://host.docker.internal:5252';
const PAY_API = process.env.PAY_API_URL ?? 'http://host.docker.internal:5008';
const OUT  = process.env.OUT_DIR  ?? '/out';

const ADMIN_USER = 'admin';
const ADMIN_PASS = 'adminpw';

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();

async function shot(name) {
    const path = `${OUT}/${name}`;
    await page.screenshot({ path, fullPage: false });
    console.log('saved', path);
}

// ---- 1) Login to WP ----
await page.goto(`${WP}/wp-login.php`, { waitUntil: 'networkidle' });
await page.fill('#user_login', ADMIN_USER);
await page.fill('#user_pass', ADMIN_PASS);
await Promise.all([
    page.waitForLoadState('networkidle'),
    page.click('#wp-submit'),
]);

// ---- Capture #1: Plugin upload page ----
await page.goto(`${WP}/wp-admin/plugin-install.php?tab=upload`, { waitUntil: 'networkidle' });
await shot('01-install-upload.png');

// ---- Capture #2: Settings page filled ----
await page.goto(`${WP}/wp-admin/admin.php?page=wc-settings&tab=checkout&section=digipay`, { waitUntil: 'networkidle' });
// Replace the api_key + webhook_secret values in-place with placeholders so
// the screenshot doesn't leak the real test credentials.
await page.evaluate(() => {
    const k = document.getElementById('woocommerce_digipay_api_key');
    if (k) { k.value = 'dgp_test_••••••••••••••••••••••••••••••'; k.type = 'text'; }
    const s = document.getElementById('woocommerce_digipay_webhook_secret');
    if (s) { s.value = '••••••••••••••••••••••••••••••••'; s.type = 'text'; }
});
await shot('02-settings.png');

// ---- Pre-step: ensure cart has the test product, then capture block checkout ----
await page.goto(`${WP}/?add-to-cart=10&quantity=1`, { waitUntil: 'networkidle' });
await page.goto(`${WP}/checkout/`, { waitUntil: 'networkidle' });
// Give Block checkout's JS time to render the payment options.
await page.waitForTimeout(2500);
await shot('03-checkout-radio.png');

// ---- Capture #4: Hosted checkout QR (fresh pending session) ----
const fresh = process.env.FRESH_SESSION_ID;
if (fresh) {
    await page.goto(`${PAY}/pay/${fresh}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2000);
    await shot('04-hosted-checkout-qr.png');
} else {
    console.warn('FRESH_SESSION_ID not set — skipped #4');
}

// ---- Capture #5: Hosted checkout confirmed + return button ----
const confirmed = process.env.CONFIRMED_SESSION_ID;
if (confirmed) {
    await page.goto(`${PAY}/pay/${confirmed}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await shot('05-payment-confirmed.png');
} else {
    console.warn('CONFIRMED_SESSION_ID not set — skipped #5');
}

// ---- Capture #6: WC thank-you page ----
const thankYou = process.env.THANK_YOU_URL;
if (thankYou) {
    await page.goto(thankYou, { waitUntil: 'networkidle' });
    await shot('06-thankyou.png');
} else {
    console.warn('THANK_YOU_URL not set — skipped #6');
}

// ---- Capture #7: WC order admin (admin still logged in via context) ----
const orderId = process.env.WC_ORDER_ID;
if (orderId) {
    await page.goto(`${WP}/wp-admin/post.php?post=${orderId}&action=edit`, { waitUntil: 'networkidle' });
    await shot('07-wc-order-admin.png');
} else {
    console.warn('WC_ORDER_ID not set — skipped #7');
}

// ---- Capture #8: DigiPay dashboard (best-effort; needs separate auth) ----
// The dashboard is gated behind Digi-ID / merchant login which doesn't fit a
// headless flow. Skip rather than capture a login page that misrepresents
// the dashboard view. The customer-flow doc handles this gracefully.

await browser.close();
console.log('done');
