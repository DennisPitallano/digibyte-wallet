// End-to-end journeys for DigiPay (Pay.Api + Pay.Web), see README in package.json scripts.
// Starts both servers on a fresh SQLite database, then runs tests/*.spec.mjs in Chromium.
// PW_CHANNEL=msedge uses the locally installed Edge instead of Playwright's Chromium download.
import { defineConfig, devices } from '@playwright/test';
import { apiUrl, webUrl, serverCommands } from './servers.mjs';

const { api, web, env, cwd } = serverCommands();
const isCI = !!process.env.CI;

export default defineConfig({
    testDir: './tests',
    testMatch: '**/*.spec.mjs',
    // The journeys share one dev merchant and the API rate limits per IP, so run them one at a time.
    workers: 1,
    fullyParallel: false,
    forbidOnly: isCI,
    retries: isCI ? 1 : 0,
    timeout: 90_000,
    expect: { timeout: 15_000 },
    reporter: [['list'], ['html', { open: 'never' }]],
    outputDir: 'test-results',
    use: {
        baseURL: webUrl,
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
        video: 'retain-on-failure',
    },
    projects: [
        {
            name: 'chromium',
            use: {
                ...devices['Desktop Chrome'],
                ...(process.env.PW_CHANNEL ? { channel: process.env.PW_CHANNEL } : {}),
            },
        },
    ],
    webServer: [
        {
            name: 'Pay.Api',
            command: api,
            cwd,
            env,
            url: `${apiUrl}/v1/pay/dev/status`,
            // Never attach to a server someone left running: the tests need the fresh database.
            reuseExistingServer: false,
            timeout: 300_000,
            stdout: 'ignore',
            stderr: 'pipe',
        },
        {
            name: 'Pay.Web',
            command: web,
            cwd,
            env,
            url: `${webUrl}/`,
            reuseExistingServer: false,
            timeout: 300_000,
            stdout: 'ignore',
            stderr: 'pipe',
        },
    ],
});
