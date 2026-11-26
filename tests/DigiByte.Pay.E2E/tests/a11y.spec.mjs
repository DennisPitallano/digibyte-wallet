// Accessibility: axe (WCAG 2.x A/AA) finds no serious or critical violations on the key pages.
import { test, expect } from '@playwright/test';
import { apiContext, devSignin, createSession, advance, seedMerchant, expectNoSeriousA11yViolations } from './helpers.mjs';

for (const path of ['/', '/use-cases', '/docs']) {
    test(`a11y: ${path}`, async ({ page }) => {
        await page.goto(path);
        await expect(page.locator('nav.dp-sitenav')).toBeVisible();
        await expectNoSeriousA11yViolations(page);
    });
}

test('a11y: checkout and receipt', async ({ page }) => {
    const api = await apiContext();
    const { token } = await devSignin(api);
    const session = await createSession(api, token, { amount: 5, label: 'E2E a11y' });

    await page.goto(`/pay/${encodeURIComponent(session.id)}`);
    await expect(page.getByRole('img', { name: 'Scan to pay' })).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await advance(api, token, session.id, { to: 'confirmed' });
    await page.goto(`/pay/${encodeURIComponent(session.id)}/receipt`);
    await expect(page.locator('#receipt-heading')).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await api.dispose();
});

test('a11y: dashboard overview', async ({ page }) => {
    const api = await apiContext();
    await seedMerchant(page, await devSignin(api));
    await page.goto('/merchant');
    await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'New payment', level: 2 })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await api.dispose();
});
