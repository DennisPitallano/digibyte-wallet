// node --test for wwwroot/theme.js (specs/032-digipay-ui-refresh, T018).
// Loads the real script into a sandbox with a fake DOM, localStorage and matchMedia.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, '..', '..', 'wwwroot', 'theme.js'), 'utf8');

function load({ path = '/', search = '', stored = {}, deviceDark = false } = {}) {
    const store = { ...stored };
    const classes = new Set();
    const listeners = [];
    const meta = { content: '', setAttribute(k, v) { this[k] = v; } };
    const media = {
        get matches() { return state.deviceDark; },
        addEventListener(_t, fn) { listeners.push(fn); },
    };
    const state = { deviceDark };
    const window = {
        location: { pathname: path, search },
        localStorage: {
            getItem: k => (k in store ? store[k] : null),
            setItem: (k, v) => { store[k] = String(v); },
        },
        matchMedia: () => media,
        MutationObserver: class { observe() { } },
        document: {
            documentElement: {
                classList: {
                    contains: c => classes.has(c),
                    toggle: (c, on) => { on ? classes.add(c) : classes.delete(c); },
                },
                style: {},
            },
            querySelector: sel => (sel === 'meta[name="theme-color"]' ? meta : null),
            addEventListener() { },
        },
    };
    vm.runInNewContext(source, { window });
    return {
        dark: () => classes.has('dark'),
        store,
        meta,
        api: window.DigiPayTheme,
        setDevice(dark) { state.deviceDark = dark; listeners.forEach(fn => fn()); },
    };
}

test('a saved light or dark choice is kept', () => {
    assert.equal(load({ stored: { 'dp-theme': 'dark' } }).dark(), true);
    assert.equal(load({ stored: { 'dp-theme': 'light' }, deviceDark: true }).dark(), false);
});

test('no saved choice follows the device', () => {
    assert.equal(load({ deviceDark: true }).dark(), true);
    assert.equal(load({ deviceDark: false }).dark(), false);
    assert.equal(load().api.getMode(), 'system');
});

test('an invalid saved value is treated as system', () => {
    const t = load({ stored: { 'dp-theme': 'purple' }, deviceDark: true });
    assert.equal(t.api.getMode(), 'system');
    assert.equal(t.dark(), true);
});

test('?theme=light on a /pay page wins over a saved dark choice and is not saved', () => {
    const t = load({ path: '/pay/ses_x', search: '?theme=light', stored: { 'dp-theme': 'dark' } });
    assert.equal(t.dark(), false);
    assert.equal(t.store['dp-theme'], 'dark');
});

test('?theme= is case-insensitive and works on the receipt and donation page', () => {
    assert.equal(load({ path: '/pay/ses_x/receipt', search: '?theme=DARK' }).dark(), true);
    assert.equal(load({ path: '/pay/button', search: '?to=dgb1&theme=dark' }).dark(), true);
});

test('?theme= with another value is ignored', () => {
    assert.equal(load({ path: '/pay/ses_x', search: '?theme=purple', deviceDark: true }).dark(), true);
});

test('?theme= outside /pay pages is ignored', () => {
    assert.equal(load({ path: '/merchant', search: '?theme=dark', deviceDark: false }).dark(), false);
});

test('the POS screen starts dark and uses its own key', () => {
    assert.equal(load({ path: '/pos', deviceDark: false }).dark(), true);
    assert.equal(load({ path: '/pos', stored: { 'digipay.pos.theme:v1': 'light' } }).dark(), false);
    assert.equal(load({ path: '/pos', stored: { 'dp-theme': 'light' } }).dark(), true);
});

test('device changes re-apply only in system mode', () => {
    const sys = load({ deviceDark: false });
    sys.setDevice(true);
    assert.equal(sys.dark(), true);

    const fixed = load({ stored: { 'dp-theme': 'light' }, deviceDark: false });
    fixed.setDevice(true);
    assert.equal(fixed.dark(), false);
});

test('setMode saves the choice for the page type and applies it', () => {
    const site = load({ deviceDark: false });
    site.api.setMode('dark');
    assert.equal(site.store['dp-theme'], 'dark');
    assert.equal(site.dark(), true);

    const pos = load({ path: '/pos' });
    pos.api.setMode('light');
    assert.equal(pos.store['digipay.pos.theme:v1'], 'light');
    assert.equal(pos.store['dp-theme'], undefined);
    assert.equal(pos.dark(), false);
});

test('the browser chrome colour matches the theme', () => {
    assert.equal(load({ stored: { 'dp-theme': 'dark' } }).meta.content, '#002352');
    assert.equal(load({ stored: { 'dp-theme': 'light' } }).meta.content, '#f8fafc');
});
