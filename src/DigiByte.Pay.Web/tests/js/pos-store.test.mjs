// node --test for wwwroot/pos-store.js: per-tablet settings, recent currencies, the hardware keypad filter,
// the haptic tick and the online/offline hooks. Loads the real script into a sandbox with a fake DOM.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, '..', '..', 'wwwroot', 'pos-store.js'), 'utf8');

function load({ stored = {}, vibrate = true } = {}) {
    const store = { ...stored };
    const docListeners = {};
    const winListeners = {};
    const vibrations = [];
    const document = {
        activeElement: null,
        querySelector: () => null,
        addEventListener: (t, fn) => { (docListeners[t] ||= []).push(fn); },
        removeEventListener: (t, fn) => { docListeners[t] = (docListeners[t] || []).filter(f => f !== fn); },
    };
    const window = {
        navigator: vibrate ? { vibrate: ms => { vibrations.push(ms); return true; } } : {},
        addEventListener: (t, fn) => { (winListeners[t] ||= []).push(fn); },
        removeEventListener: (t, fn) => { winListeners[t] = (winListeners[t] || []).filter(f => f !== fn); },
    };
    const localStorage = {
        getItem: k => (k in store ? store[k] : null),
        setItem: (k, v) => { store[k] = String(v); },
        removeItem: k => { delete store[k]; },
    };
    vm.runInNewContext(source, { window, document, localStorage, setTimeout, crypto: globalThis.crypto, TextEncoder });
    const calls = [];
    const dotnet = { invokeMethodAsync: (name, ...args) => { calls.push([name, ...args]); return Promise.resolve(); } };
    return {
        api: window.digipayPos, store, calls, dotnet, vibrations, document,
        fire: (type, event) => (docListeners[type] || []).forEach(fn => fn(event)),
        fireWindow: type => (winListeners[type] || []).forEach(fn => fn({})),
        listenerCount: type => (docListeners[type] || []).length,
    };
}

// A keypad key inside (or outside) the POS root.
function key({ haptics = 'on', disabled = false, isKey = true } = {}) {
    const root = { getAttribute: n => (n === 'data-haptics' ? haptics : null) };
    const el = {
        disabled,
        closest: sel => (sel === '[data-pos-haptic]' ? (isKey ? el : null) : sel === '.dp-pos' ? root : null),
    };
    return el;
}

test('prefs round-trip and anything but a plain object reads back as null', () => {
    const t = load();
    assert.equal(t.api.loadPrefs(), null);
    t.api.savePrefs({ layout: 'split', keypad: 'calculator', quickAmounts: { PHP: [100, 200] } });
    assert.deepEqual(JSON.parse(JSON.stringify(t.api.loadPrefs())), { layout: 'split', keypad: 'calculator', quickAmounts: { PHP: [100, 200] } });

    for (const raw of ['[1,2]', '"split"', '42', '{not json']) {
        assert.equal(load({ stored: { 'digipay.pos.prefs:v1': raw } }).api.loadPrefs(), null, raw);
    }
});

test('recent currencies keep four distinct upper-case strings, most recent first', () => {
    const t = load();
    assert.deepEqual([...t.api.loadRecent()], []);
    t.api.saveRecent(['php', 'USD', 7, 'PHP', null, 'eur', 'jpy', 'kes']);
    assert.deepEqual([...t.api.loadRecent()], ['PHP', 'USD', 'EUR', 'JPY']);
    assert.deepEqual([...load({ stored: { 'digipay.pos.recentCurrencies:v1': '{"a":1}' } }).api.loadRecent()], []);
    assert.deepEqual([...load({ stored: { 'digipay.pos.recentCurrencies:v1': 'oops' } }).api.loadRecent()], []);
});

test('the hardware keypad forwards digits, the point, Backspace, Delete and Enter only', () => {
    const t = load();
    t.api.bindKeys(t.dotnet);
    const prevented = [];
    for (const k of ['5', '.', ',', 'Backspace', 'Delete', 'Enter', 'a', 'Escape', 'F5']) {
        t.fire('keydown', { key: k, preventDefault: () => prevented.push(k) });
    }
    assert.deepEqual(t.calls.map(c => c[1]), ['5', '.', ',', 'Backspace', 'Delete', 'Enter']);
    assert.deepEqual(prevented, ['Backspace', 'Enter']);
});

test('a key press vibrates only when haptics are on and the device can', () => {
    const t = load();
    t.api.bindKeys(t.dotnet);
    t.fire('pointerdown', { target: key() });
    t.fire('pointerdown', { target: key({ haptics: 'off' }) });
    t.fire('pointerdown', { target: key({ disabled: true }) });
    t.fire('pointerdown', { target: key({ isKey: false }) });
    assert.deepEqual(t.vibrations, [8]);

    const quiet = load({ vibrate: false });
    quiet.api.bindKeys(quiet.dotnet);
    assert.doesNotThrow(() => quiet.fire('pointerdown', { target: key() }));
});

test('online and offline are reported, and unbinding removes every listener', () => {
    const t = load();
    t.api.bindKeys(t.dotnet);
    t.fireWindow('offline');
    t.fireWindow('online');
    assert.deepEqual(t.calls, [['OnNetworkChanged', false], ['OnNetworkChanged', true]]);

    t.api.unbindKeys();
    t.fireWindow('offline');
    assert.equal(t.calls.length, 2);
    assert.equal(t.listenerCount('keydown'), 0);
    assert.equal(t.listenerCount('pointerdown'), 0);
});
