// node --test for the POS's new-version check in wwwroot/pos-store.js: a till left open reloads itself after an
// update, but only while it is idle (keypad, no amount, no dialog, no sale), and only on /pos.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, '..', '..', 'wwwroot', 'pos-store.js'), 'utf8');

function load({ path = '/pos', pageBuild = 'aaa', serverBuild = 'bbb', idle = true, fetchFails = false } = {}) {
    const state = { idle, reloads: 0, intervals: [], fetches: 0, serverBuild };
    const meta = { content: pageBuild };
    const root = { getAttribute: name => (name === 'data-idle' ? String(state.idle) : null) };
    const document = {
        activeElement: null,
        querySelector: sel => (sel.startsWith('meta[name="digipay-build"]') ? meta : sel === '.dp-pos' ? root : null),
        addEventListener() {}, removeEventListener() {},
    };
    const window = { navigator: {}, addEventListener() {}, removeEventListener() {} };
    const location = { pathname: path, reload: () => { state.reloads++; } };
    const fetch = async () => {
        state.fetches++;
        if (fetchFails) throw new Error('offline');
        return { ok: true, json: async () => ({ build: state.serverBuild }) };
    };
    const setInterval = (fn, ms) => { state.intervals.push({ fn, ms }); return state.intervals.length; };
    const localStorage = { getItem: () => null, setItem() {}, removeItem() {} };
    vm.runInNewContext(source, { window, document, localStorage, location, fetch, setInterval, setTimeout, crypto: globalThis.crypto, TextEncoder });
    return { api: window.digipayPos, state };
}

test('an idle till reloads when the server runs a newer build', async () => {
    const { api, state } = load();
    await api.checkBuild();
    assert.equal(state.reloads, 1);
});

test('a busy till waits, then reloads as soon as it is idle', async () => {
    const { api, state } = load({ idle: false });
    await api.checkBuild();
    assert.equal(state.reloads, 0, 'never interrupts a sale');

    // The 5-second idle watcher picks it up.
    const idleWatcher = state.intervals.find(i => i.ms === 5000);
    idleWatcher.fn();
    assert.equal(state.reloads, 0);
    state.idle = true;
    idleWatcher.fn();
    assert.equal(state.reloads, 1);
});

test('the same build, or no answer from the server, never reloads', async () => {
    const same = load({ serverBuild: 'aaa' });
    await same.api.checkBuild();
    assert.equal(same.state.reloads, 0);

    const offline = load({ fetchFails: true });
    await offline.api.checkBuild();
    assert.equal(offline.state.reloads, 0);
});

test('only the POS page checks, every five minutes', () => {
    const pos = load();
    assert.deepEqual(pos.state.intervals.map(i => i.ms).sort((a, b) => a - b), [5000, 300000]);

    const other = load({ path: '/merchant' });
    assert.equal(other.state.intervals.length, 0);
});
