// Journey: a paired POS tablet charges a sale and shows the result once it confirms.
import { test, expect } from '@playwright/test';
import { apiContext, devSignin, bearer, advance, latestSessions } from './helpers.mjs';

test('pos: paired till → type amount → charge → QR → confirmed result', async ({ page }) => {
    const api = await apiContext();
    const { token } = await devSignin(api);

    // Pair a device the way the dashboard does, then claim it the way the tablet does.
    const pairingRes = await api.post('/v1/pay/pos/pairings', {
        headers: bearer(token),
        data: { storeId: 'sto_devmerchant000001', deviceLabel: 'E2E till' },
    });
    expect(pairingRes.ok(), await pairingRes.text()).toBeTruthy();
    const { code } = await pairingRes.json();
    const claimRes = await api.post('/v1/pay/pos/claim', { data: { code } });
    expect(claimRes.ok(), await claimRes.text()).toBeTruthy();
    const claim = await claimRes.json();

    const device = {
        apiKey: claim.apiKey,
        storeId: claim.storeId,
        storeName: claim.storeName ?? 'Dev store (testnet)',
        merchantName: claim.merchantName ?? 'Dev merchant',
    };
    await page.addInitScript((d) => {
        localStorage.setItem('digipay.pos.device:v1', JSON.stringify(d));
        localStorage.setItem('digipay.pos.currency:v1', 'DGB');
    }, device);

    await page.goto('/pos');
    const keypad = page.getByRole('region', { name: 'Keypad' });
    await expect(keypad).toBeVisible();

    // A hardware keyboard drives the keypad; Enter charges.
    await page.keyboard.type('12');
    const charge = page.locator('.dp-pos-charge');
    await expect(charge).toContainText('12');
    await expect(charge).toBeEnabled();
    await page.keyboard.press('Enter');

    // Waiting for the customer, with a QR to scan.
    await expect(page.getByRole('img', { name: 'Scan to pay' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Payment for the customer' })).toBeVisible();

    const [session] = await latestSessions(api, token, 1);
    expect(session, 'the till created a session').toBeTruthy();
    expect(session.storeId).toBe(device.storeId);
    await advance(api, token, session.id, { to: 'confirmed' });

    // The result screen.
    await expect(page.locator('.dp-pos-result-title')).toHaveText(/confirmed/i, { timeout: 30_000 });
    await expect(page.getByRole('button', { name: 'New sale' })).toBeVisible();

    await api.dispose();
});
