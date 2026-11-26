// Lighthouse for the public site: home and /docs.
// Fails when accessibility < 90, SEO < 90 or best practices < 85. Performance is reported only
// (a CI runner's numbers swing too much to gate on).
//
//   npm run lighthouse                 starts both servers (fresh SQLite), audits, stops them
//   E2E_BASE_URL=http://localhost:5252 npm run lighthouse   audits servers you already started
//   CHROME_PATH=...                    browser to use (default: Playwright's Chromium, or PW_CHANNEL=msedge → Edge)
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import { serverCommands, apiUrl, webUrl } from '../servers.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const outDir = join(here, '..', 'lighthouse-results');
const LIGHTHOUSE = 'lighthouse@13.6.0';
const paths = ['/', '/docs'];
const thresholds = { accessibility: 90, seo: 90, 'best-practices': 85 };

function browserPath() {
    if (process.env.CHROME_PATH) return process.env.CHROME_PATH;
    if (process.env.PW_CHANNEL === 'msedge') {
        const candidates = [
            'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
            'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
            '/usr/bin/microsoft-edge',
            '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
        ];
        const found = candidates.find(existsSync);
        if (found) return found;
    }
    return chromium.executablePath();
}

async function waitFor(url, seconds = 300) {
    const deadline = Date.now() + seconds * 1000;
    while (Date.now() < deadline) {
        try { if ((await fetch(url)).ok) return; } catch { /* not up yet */ }
        await new Promise((r) => setTimeout(r, 1000));
    }
    throw new Error(`Timed out waiting for ${url}`);
}

function startServers() {
    const { api, web, env, cwd } = serverCommands();
    const opts = { cwd, env: { ...process.env, ...env }, shell: true, stdio: ['ignore', 'ignore', 'inherit'], detached: process.platform !== 'win32' };
    return [spawn(api, opts), spawn(web, opts)];
}

function stopServers(children) {
    for (const child of children) {
        if (child.exitCode !== null) continue;
        if (process.platform === 'win32') {
            try { execFileSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { }
        } else {
            try { process.kill(-child.pid, 'SIGTERM'); } catch { }
        }
    }
}

let children = [];
const base = process.env.E2E_BASE_URL || webUrl;
let failed = false;
try {
    if (!process.env.E2E_BASE_URL) {
        children = startServers();
        await waitFor(`${apiUrl}/api/health`);
        await waitFor(`${webUrl}/`);
    }
    mkdirSync(outDir, { recursive: true });
    const chromePath = browserPath();
    console.log(`Lighthouse with ${chromePath}`);

    const rows = [];
    for (const path of paths) {
        const name = path === '/' ? 'home' : path.replace(/^\//, '').replace(/\//g, '-');
        // With several --output formats Lighthouse writes <path>.report.json and <path>.report.html.
        const outPath = join(outDir, name);
        const args = [
            '--yes', LIGHTHOUSE, `${base}${path}`,
            '--quiet',
            '--output=json', '--output=html', `--output-path=${outPath}`,
            '--only-categories=performance,accessibility,best-practices,seo',
            '--chrome-flags=--headless=new --no-sandbox --disable-gpu --disable-extensions',
        ];
        execFileSync(process.platform === 'win32' ? 'npx.cmd' : 'npx', args, {
            stdio: 'inherit',
            shell: process.platform === 'win32',
            env: { ...process.env, CHROME_PATH: chromePath },
        });
        const report = JSON.parse(readFileSync(`${outPath}.report.json`, 'utf8'));
        const score = (id) => Math.round((report.categories[id]?.score ?? 0) * 100);
        const row = { page: path, performance: score('performance'), accessibility: score('accessibility'), 'best-practices': score('best-practices'), seo: score('seo') };
        rows.push(row);
        for (const [cat, min] of Object.entries(thresholds)) {
            if (row[cat] < min) {
                failed = true;
                console.error(`✗ ${path}: ${cat} ${row[cat]} < ${min}`);
                const audits = Object.values(report.audits)
                    .filter((a) => a.score !== null && a.score < 1 && report.categories[cat].auditRefs.some((r) => r.id === a.id && r.weight > 0));
                for (const a of audits) console.error(`    - ${a.id}: ${a.title}`);
            }
        }
    }
    console.table(rows);
    console.log(`Reports: ${outDir}`);
} catch (err) {
    failed = true;
    console.error(err);
} finally {
    stopServers(children);
}
process.exit(failed ? 1 : 0);
