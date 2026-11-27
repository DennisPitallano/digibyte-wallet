// Where the two DigiPay servers listen during an end-to-end run, and how to start them.
// Shared by playwright.config.mjs (webServer) and scripts/lighthouse.mjs.
//
//   E2E_API_PORT / E2E_WEB_PORT   ports (default 5008 / 5252, the normal dev ports). Use others, e.g.
//                                 5108 / 5352, when dev servers are already running on the defaults.
//   E2E_SKIP_BUILD=1              skip `dotnet build` (CI builds once in its own step).
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
export const repoRoot = resolve(here, '..', '..');

export const apiPort = Number(process.env.E2E_API_PORT || 5008);
// The fake Esplora explorer and price feed (scripts/fake-esplora.mjs), so DigiDollar journeys run without the internet.
export const nodePort = Number(process.env.E2E_NODE_PORT || 5262);
export const nodeUrl = `http://localhost:${nodePort}`;
export const webPort = Number(process.env.E2E_WEB_PORT || 5252);
export const apiUrl = `http://localhost:${apiPort}`;
export const webUrl = `http://localhost:${webPort}`;

const apiProject = join('src', 'DigiByte.Pay.Api');
const webProject = join('src', 'DigiByte.Pay.Web');

/**
 * A fresh SQLite file per run, so every run starts from an empty database. Kept in an env var so the
 * Playwright workers (which load the config again and inherit the runner's env) reuse it.
 */
function freshDbPath() {
    process.env.E2E_DB_PATH ||= join(mkdtempSync(join(tmpdir(), 'digipay-e2e-')), 'digipay.db');
    return process.env.E2E_DB_PATH;
}

const quote = (s) => `"${s}"`;

/**
 * The two server commands. The API command builds BOTH projects first (they share DigiByte.Crypto and
 * DigiByte.UI, and two parallel `dotnet run` builds fight over the same obj/bin files), then the web
 * command waits for the API to answer before it starts with --no-build.
 */
export function serverCommands() {
    const skipBuild = process.env.E2E_SKIP_BUILD === '1';
    const build = skipBuild
        ? ''
        : `dotnet build ${quote(webProject)} --nologo -v q && dotnet build ${quote(apiProject)} --nologo -v q && `;
    const api =
        `${build}dotnet run --no-build --no-launch-profile --project ${quote(apiProject)} -- ` +
        `--urls ${apiUrl} ` +
        `--DigiPay:ConnectionString= ` +
        `--DigiPay:DbPath=${quote(freshDbPath())} ` +
        `--DigiPay:DevSignIn=true ` +
        `--ClientOrigin=${webUrl} ` +
        `--ClientWebUrl=${webUrl} ` +
        // DigiDollar (specs/033, 035): the dev store has DD on with its own testnet DD key; the fake explorer answers.
        `--DigiPay:DdSurfacesEnabled=true ` +
        `--DigiPay:DigiDollar:EsploraMainnetUrl=${nodeUrl}/api ` +
        `--DigiPay:DigiDollar:EsploraTestnetUrl=${nodeUrl}/testnet/api ` +
        `--DigiPay:Prices:CoinGeckoUrl=${quote(`${nodeUrl}/coingecko?vs_currencies=`)} ` +
        `--DigiPay:Prices:FxUrl=${nodeUrl}/fx`;
    const web =
        // /api/health, not /v1/pay/dev/status: the dev group shares the 10-per-minute "auth" rate limit
        // with the sign-ins the tests make.
        `node ${quote(join(here, 'scripts', 'wait-for.mjs'))} ${apiUrl}/api/health && ` +
        `dotnet run --no-build --no-launch-profile --project ${quote(webProject)} -- ` +
        `--urls ${webUrl} --DigiPay:ApiUrl=${apiUrl}`;
    const env = { ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' };
    const node = `node ${quote(join(here, 'scripts', 'fake-esplora.mjs'))} ${nodePort}`;
    return { api, web, node, env, cwd: repoRoot };
}
