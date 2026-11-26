// The public website renders with its sticky header and never scrolls sideways, at phone and desktop widths,
// in both themes.
import { test, expect } from '@playwright/test';

const pages = ['/', '/use-cases', '/docs', '/docs/api', '/features'];
const widths = [375, 1280];
const themes = ['light', 'dark'];

for (const theme of themes) {
    for (const width of widths) {
        test(`public site: ${theme} at ${width}px`, async ({ page }) => {
            await page.setViewportSize({ width, height: 900 });
            await page.addInitScript((t) => localStorage.setItem('dp-theme', t), theme);

            for (const path of pages) {
                await test.step(path, async () => {
                    await page.goto(path);
                    const nav = page.locator('nav.dp-sitenav');
                    await expect(nav, `${path}: site header`).toBeVisible();
                    await expect(page.locator('h1').first(), `${path}: page heading`).toBeVisible();

                    const isDark = await page.evaluate(() => document.documentElement.classList.contains('dark'));
                    expect(isDark, `${path}: theme`).toBe(theme === 'dark');

                    // Polled: the prerendered header can be swapped for the interactive one while we look.
                    await expect
                        .poll(() => nav.evaluate((el) => getComputedStyle(el).position), { message: `${path}: header is sticky` })
                        .toBe('sticky');

                    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
                    expect(overflow, `${path}: horizontal overflow (px)`).toBeLessThanOrEqual(0);

                    // Still pinned to the top after scrolling down.
                    await page.evaluate(() => window.scrollTo(0, Math.min(1200, document.body.scrollHeight)));
                    await expect
                        .poll(async () => (await nav.boundingBox())?.y ?? -999, { message: `${path}: header stays at the top` })
                        .toBeLessThanOrEqual(1);
                    expect((await nav.boundingBox()).y).toBeGreaterThanOrEqual(-1);
                });
            }
        });
    }
}
