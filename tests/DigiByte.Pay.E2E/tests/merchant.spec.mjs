// Journey: a merchant signs in, creates a payment, and follows it from waiting to a receipt.
import { test, expect } from '@playwright/test';
import { apiContext, advance } from './helpers.mjs';

test('merchant: dev sign-in → new payment → live updates → checkout → receipt', async ({ page }) => {
    const api = await apiContext();

    // Sign in with the Development-only button.
    await page.goto('/merchant/signin');
    await page.getByRole('button', { name: 'Sign in as dev merchant (local testing)' }).click();
    await expect(page).toHaveURL(/\/merchant\/?$/);
    await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'New payment', level: 2 })).toBeVisible();

    const token = await page.evaluate(() => localStorage.getItem('digipay-token'));
    expect(token, 'sign-in stores the session token').toBeTruthy();

    // New payment for 25 DGB with a label.
    await page.locator('main').getByRole('link', { name: 'New payment' }).first().click();
    await expect(page.getByRole('heading', { name: 'New payment', level: 1 })).toBeVisible();
    await page.getByLabel('Amount (DGB)').fill('25');
    await page.getByLabel(/^Label/).fill('E2E coffee');
    await page.getByLabel(/^Label/).press('Tab'); // @bind updates on change
    const create = page.getByRole('button', { name: 'Create payment' });
    await expect(create).toBeEnabled();
    await create.click();
    await expect(page.getByText('Payment created')).toBeVisible();
    await page.getByRole('link', { name: 'View payment' }).click();

    // The payment page: waiting for the customer.
    await expect(page).toHaveURL(/\/merchant\/sessions\/[^/?#]+/);
    const sessionId = decodeURIComponent(new URL(page.url()).pathname.split('/').pop());
    await expect(page.getByRole('heading', { name: 'Payment', level: 1 })).toBeVisible();
    await expect(page.locator('.ui-badge[data-status="pending"]').first()).toBeVisible();
    await expect(page.getByText('25 DGB').first()).toBeVisible();
    await expect(page.getByText('E2E coffee').first()).toBeVisible();

    // Paid with 3 confirmations, from the sandbox. The page must pick it up by itself (it polls every 10s).
    await page.evaluate(() => { window.__e2eNoReload = true; });
    await advance(api, token, sessionId, { to: 'paid', confirmations: 3 });
    await expect(page.locator('.ui-badge[data-status="paid"]').first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('3 of 6 confirmations')).toBeVisible();
    expect(await page.evaluate(() => window.__e2eNoReload), 'updated without a reload').toBe(true);

    // The customer's checkout shows the same progress.
    await page.goto(`/pay/${encodeURIComponent(sessionId)}`);
    await expect(page.locator('.ui-confirm-panel-value')).toHaveText('3 of 6');

    // Fully confirmed: the open checkout updates live.
    await advance(api, token, sessionId, { to: 'confirmed' });
    await expect(page.getByRole('heading', { name: 'Payment confirmed' })).toBeVisible({ timeout: 30_000 });

    // Receipt with the Blockchair PDF.
    await page.getByRole('link', { name: 'View receipt' }).click();
    await expect(page).toHaveURL(new RegExp(`/pay/${sessionId}/receipt`));
    await expect(page.locator('#receipt-heading')).toBeVisible();
    const pdf = page.getByRole('link', { name: /Download PDF receipt/ });
    await expect(pdf).toBeVisible();
    await expect(pdf).toHaveAttribute('href', /^https:\/\/blockchair\.com\/digibyte\/transaction\/.+\.pdf$/);

    await api.dispose();
});
