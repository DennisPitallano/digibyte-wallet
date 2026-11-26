// Lighthouse for the public site: home and /docs, with Lighthouse's default mobile settings.
// Fails when performance < 75 (LH_MIN_PERF), accessibility < 95, SEO < 95 or best practices < 90 on either page.
//
// Pay.Web is audited the way it runs in production (docker/pay-web/Dockerfile): a Release publish started with
// ASPNETCORE_ENVIRONMENT=Production. Development mode serves static files uncompressed and uncached, which no
// visitor ever sees. Pay.Api (for the home page's live numbers) runs as in the Playwright suite.
//
// Each page is audited LH_RUNS times (default 3) and the run with the median performance score counts, so one
// noisy run on a shared CI machine doesn't decide the result.
//
//   npm run lighthouse                 publishes Pay.Web, starts both servers (fresh SQLite), audits, stops them
//   E2E_BASE_URL=http://localhost:5252 npm run lighthouse   audits servers you already started
//   CHROME_PATH=...                    browser to use (default: Playwright's Chromium, or PW_CHANNEL=msedge → Edge)
//   LH_RUNS=1                          one run per page (quicker, noisier)
//   LH_MIN_PERF=70                     performance floor (default 75)
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, copyFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import { serverCommands, apiUrl, webUrl, repoRoot } from '../servers.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const outDir = join(here, '..', 'lighthouse-results');
const LIGHTHOUSE = 'lighthouse@13.6.0';
const paths = ['/', '/docs'];
const runs = Math.max(1, Number(process.env.LH_RUNS || 3));
// Locally the median run scores 82–99 for performance (100 elsewhere). First paint is stable at about 1.5 s; what
// moves the score is Total Blocking Time while Blazor takes over the page, which depends on the machine's CPU.
// LH_MIN_PERF overrides the performance floor (for example on a slower runner).
const thresholds = { performance: Number(process.env.LH_MIN_PERF || 75), accessibility: 95, seo: 95, 'best-practices': 90 };

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

const spawnOpts = (cwd, env) => ({
    cwd, env: { ...process.env, ...env }, shell: true, stdio: ['ignore', 'ignore', 'inherit'], detached: process.platform !== 'win32',
});

/** Pay.Api as in the Playwright suite (its command also builds both projects unless E2E_SKIP_BUILD=1). */
function startApi() {
    const { api, env, cwd } = serverCommands();
    return spawn(api, spawnOpts(cwd, env));
}

/** Pay.Web as production runs it: a Release publish, ASPNETCORE_ENVIRONMENT=Production. */
function startPublishedWeb() {
    const publishDir = join(mkdtempSync(join(tmpdir(), 'digipay-web-')), 'publish');
    console.log(`Publishing Pay.Web (Release) to ${publishDir}`);
    execFileSync('dotnet', ['publish', join('src', 'DigiByte.Pay.Web'), '-c', 'Release', '-o', publishDir, '--nologo', '-v', 'q'], {
        cwd: repoRoot, stdio: 'inherit', shell: process.platform === 'win32',
    });
    const cmd = `dotnet "${join(publishDir, 'DigiByte.Pay.Web.dll')}" --urls ${webUrl} --DigiPay:ApiUrl=${apiUrl}`;
    return spawn(cmd, spawnOpts(publishDir, { ASPNETCORE_ENVIRONMENT: 'Production', DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' }));
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

function audit(url, outPath, chromePath) {
    // With several --output formats Lighthouse writes <path>.report.json and <path>.report.html.
    // On Windows npx runs through cmd.exe (shell: true), which splits unquoted arguments at spaces: quote the
    // ones that contain spaces, or --chrome-flags would arrive as "--headless=new" alone.
    const quote = (a) => (process.platform === 'win32' && /\s/.test(a) ? `"${a}"` : a);
    const args = [
        '--yes', LIGHTHOUSE, url,
        '--quiet',
        '--output=json', '--output=html', `--output-path=${outPath}`,
        '--only-categories=performance,accessibility,best-practices,seo',
        '--chrome-flags=--headless=new --no-sandbox --disable-gpu --disable-extensions',
    ].map(quote);
    execFileSync(process.platform === 'win32' ? 'npx.cmd' : 'npx', args, {
        stdio: 'inherit',
        shell: process.platform === 'win32',
        env: { ...process.env, CHROME_PATH: chromePath },
    });
    return JSON.parse(readFileSync(`${outPath}.report.json`, 'utf8'));
}

let children = [];
const base = process.env.E2E_BASE_URL || webUrl;
let failed = false;
try {
    if (!process.env.E2E_BASE_URL) {
        children.push(startApi());
        await waitFor(`${apiUrl}/api/health`);
        children.push(startPublishedWeb());
        await waitFor(`${webUrl}/`);
    }
    mkdirSync(outDir, { recursive: true });
    const chromePath = browserPath();
    console.log(`Lighthouse with ${chromePath}, ${runs} run(s) per page`);

    const rows = [];
    for (const path of paths) {
        const name = path === '/' ? 'home' : path.replace(/^\//, '').replace(/\//g, '-');
        const results = [];
        for (let i = 1; i <= runs; i++) {
            const outPath = join(outDir, runs === 1 ? name : `${name}-run${i}`);
            const report = audit(`${base}${path}`, outPath, chromePath);
            const score = (id) => Math.round((report.categories[id]?.score ?? 0) * 100);
            results.push({
                outPath, report,
                row: { page: path, performance: score('performance'), accessibility: score('accessibility'), 'best-practices': score('best-practices'), seo: score('seo') },
            });
        }
        // The run with the median performance score stands for the page; it is also copied to <name>.report.*.
        results.sort((a, b) => a.row.performance - b.row.performance);
        const { row, report, outPath } = results[Math.floor((results.length - 1) / 2)];
        if (runs > 1) {
            row['perf (all runs)'] = results.map((r) => r.row.performance).join(' / ');
            for (const ext of ['json', 'html']) copyFileSync(`${outPath}.report.${ext}`, join(outDir, `${name}.report.${ext}`));
        }
        rows.push(row);
        for (const [cat, min] of Object.entries(thresholds)) {
            if (row[cat] < min) {
                failed = true;
                console.error(`✗ ${path}: ${cat} ${row[cat]} < ${min}`);
                const audits = Object.values(report.audits)
                    .filter((a) => a.score !== null && a.score < 1 && report.categories[cat].auditRefs.some((r) => r.id === a.id && r.weight > 0));
                for (const a of audits) console.error(`    - ${a.id}: ${a.title}${a.displayValue ? ` (${a.displayValue})` : ''}`);
            }
        }
    }
    console.table(rows);
    console.log(`Thresholds: ${Object.entries(thresholds).map(([k, v]) => `${k} ≥ ${v}`).join(', ')}`);
    console.log(`Reports: ${outDir}`);
} catch (err) {
    failed = true;
    console.error(err);
} finally {
    stopServers(children);
}
process.exit(failed ? 1 : 0);
