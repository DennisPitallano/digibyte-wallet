// Journey (specs/033 T070): a POS sale priced in USD lets the customer switch the code between DGB and DigiDollar.
// The dev store is seeded with DigiDollar on and a payout address; scripts/fake-nodeapi.mjs plays the DigiDollar node
// and the price feeds, and the run has DIGIPAY_DD_SURFACES on (servers.mjs).
import { test, expect } from '@playwright/test';
import { apiContext, devSignin, bearer } from './helpers.mjs';

async function pairTill(api, token, page, currency) {
    const pairingRes = await api.post('/v1/pay/pos/pairings', {
        headers: bearer(token),
        data: { storeId: 'sto_devmerchant000001', deviceLabel: 'E2E DD till' },
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
    await page.addInitScript(([d, c]) => {
        localStorage.setItem('digipay.pos.device:v1', JSON.stringify(d));
        localStorage.setItem('digipay.pos.currency:v1', c);
    }, [device, currency]);
}

test('pos: a USD sale can be paid in DGB or DigiDollar, with one DD address and a quick switch', async ({ page }) => {
    const api = await apiContext();
    const { token } = await devSignin(api);
    await pairTill(api, token, page, 'USD');

    await page.goto('/pos');
    await expect(page.getByRole('region', { name: 'Keypad' })).toBeVisible();
    await expect(page.locator('.dp-pos-rate')).toContainText('1 DGB =');

    await page.keyboard.type('25');
    const charge = page.locator('.dp-pos-charge');
    await expect(charge).toBeEnabled();
    await page.keyboard.press('Enter');

    // Waiting for the customer: DGB first, with a DGB/DigiDollar switch.
    await expect(page.getByRole('img', { name: 'Scan to pay' })).toBeVisible();
    const toggle = page.locator('[data-pay-switch]');
    await expect(toggle).toBeVisible();
    const address = page.locator('.dp-pos-address');
    const dgbAddress = (await address.textContent()).trim();
    expect(dgbAddress).not.toMatch(/^TD/);

    // Switch to DigiDollar: a DD address and the dollars as DD, within 2 seconds (SC-008).
    let started = Date.now();
    await toggle.getByRole('button', { name: 'DigiDollar' }).click();
    await expect(address).toHaveText(/^TD/, { timeout: 2_000 });
    expect(Date.now() - started).toBeLessThan(2_000);
    await expect(page.locator('[data-dd-due]')).toContainText('25.00');
    const ddAddress = (await address.textContent()).trim();

    // Back to DGB and to DD again: the same addresses, no new DD address.
    await toggle.getByRole('button', { name: 'DGB' }).click();
    await expect(address).toHaveText(dgbAddress, { timeout: 2_000 });
    started = Date.now();
    await toggle.getByRole('button', { name: 'DigiDollar' }).click();
    await expect(address).toHaveText(ddAddress, { timeout: 2_000 });
    expect(Date.now() - started).toBeLessThan(2_000);

    await api.dispose();
});
