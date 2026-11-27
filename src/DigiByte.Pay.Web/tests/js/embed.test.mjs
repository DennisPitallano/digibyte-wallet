// node --test for wwwroot/embed/digipay.js: the checkout modal is an accessible dialog, and it
// learns the payment's outcome from the checkout iframe's messages.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, '..', '..', 'wwwroot', 'embed', 'digipay.js'), 'utf8');
const PAY = 'https://pay.example';

function load({ reducedMotion = false } = {}) {
    const timers = [];
    const winListeners = {};
    const docListeners = {};
    const assigned = [];
    const byId = {};

    function makeEl(tag) {
        const e = {
            tagName: tag.toUpperCase(),
            style: {},
            attrs: {},
            children: [],
            parent: null,
            listeners: {},
            setAttribute(k, v) { this.attrs[k] = String(v); },
            getAttribute(k) { return k in this.attrs ? this.attrs[k] : null; },
            appendChild(c) { c.parent = this; this.children.push(c); if (c.id) byId[c.id] = c; return c; },
            addEventListener(t, fn) { (this.listeners[t] ||= []).push(fn); },
            remove() { if (this.parent) this.parent.children = this.parent.children.filter(x => x !== this); this.parent = null; },
            focus() { document.activeElement = this; },
            querySelectorAll() { return []; },
        };
        if (tag === 'iframe') e.contentWindow = { frame: true };
        return e;
    }

    const document = {
        readyState: 'complete',
        currentScript: { src: PAY + '/embed/digipay.js' },
        activeElement: null,
        body: makeEl('body'),
        head: makeEl('head'),
        createElement: makeEl,
        createTextNode: text => ({ nodeType: 3, textContent: String(text) }),
        getElementById: id => byId[id] || null,
        getElementsByTagName: () => [],
        querySelectorAll: () => [],
        addEventListener(t, fn) { (docListeners[t] ||= []).push(fn); },
        removeEventListener(t, fn) { docListeners[t] = (docListeners[t] || []).filter(f => f !== fn); },
    };
    const ctx = {
        document,
        URL,
        console: { error() { } },
        innerHeight: 1000,
        location: { href: 'https://shop.example/cart', assign: url => assigned.push(url) },
        matchMedia: q => ({ matches: reducedMotion && q.includes('reduced-motion') }),
        setTimeout: (fn, ms) => { timers.push({ fn, ms }); return timers.length; },
        clearTimeout: id => { if (timers[id - 1]) timers[id - 1].fn = () => { }; },
        addEventListener(t, fn) { (winListeners[t] ||= []).push(fn); },
        removeEventListener(t, fn) { winListeners[t] = (winListeners[t] || []).filter(f => f !== fn); },
    };
    ctx.window = ctx;
    vm.runInNewContext(source, ctx);

    const modal = () => document.body.children.find(c => c.className === 'digipay-modal') || null;
    const dialog = () => modal()?.children[0];
    const iframe = () => dialog()?.children.find(c => c.tagName === 'IFRAME');
    const closeButton = () => dialog()?.children.find(c => c.tagName === 'BUTTON');

    return {
        api: ctx.DigiPay, document, assigned, modal, dialog, iframe, closeButton,
        flushTimers() { while (timers.length) { const t = timers.shift(); t.fn(); } },
        send(data, { source = iframe()?.contentWindow, origin = PAY } = {}) {
            (winListeners.message || []).slice().forEach(fn => fn({ data, source, origin }));
        },
        key(key) { (docListeners.keydown || []).slice().forEach(fn => fn({ key })); },
    };
}

test('the modal is an accessible dialog with a titled iframe and a 44px close button', () => {
    const w = load();
    const opener = { focus() { w.document.activeElement = opener; } };
    w.document.activeElement = opener;
    w.api.checkout({ sessionId: 'ses_abc' });

    assert.equal(w.dialog().getAttribute('role'), 'dialog');
    assert.equal(w.dialog().getAttribute('aria-modal'), 'true');
    assert.ok(w.dialog().getAttribute('aria-label'));
    assert.equal(w.iframe().title, 'DigiPay checkout');
    assert.match(w.closeButton().style.cssText, /width:44px;height:44px/);
    assert.ok(w.closeButton().getAttribute('aria-label'));

    w.flushTimers();
    assert.equal(w.document.activeElement, w.iframe(), 'focus moves into the checkout');

    w.key('Escape');
    assert.equal(w.modal(), null);
    assert.equal(w.document.activeElement, opener, 'focus returns to the opener');
});

test('reduced motion turns the open animation off', () => {
    const animated = load();
    animated.api.checkout({ sessionId: 'ses_abc' });
    assert.match(animated.modal().style.cssText, /animation/);

    const still = load({ reducedMotion: true });
    still.api.checkout({ sessionId: 'ses_abc' });
    assert.doesNotMatch(still.modal().style.cssText, /animation/);
    assert.doesNotMatch(still.dialog().style.cssText, /animation/);
});

test('status messages reach onStatus; confirmed calls onSuccess once and closes after a short delay', () => {
    const w = load();
    const statuses = [];
    const successes = [];
    w.api.checkout({ sessionId: 'ses_abc', onStatus: e => statuses.push(e.status), onSuccess: e => successes.push(e) });

    w.send({ type: 'digipay-checkout-status', sessionId: 'ses_abc', status: 'paid' });
    w.send({ type: 'digipay-checkout-status', sessionId: 'ses_abc', status: 'confirmed', returnUrl: 'https://shop.example/thanks' });
    w.send({ type: 'digipay-checkout-status', sessionId: 'ses_abc', status: 'confirmed' });

    assert.deepEqual(statuses, ['paid', 'confirmed', 'confirmed']);
    assert.equal(successes.length, 1);
    assert.deepEqual({ ...successes[0] }, { sessionId: 'ses_abc', status: 'confirmed' });
    assert.ok(w.modal(), 'still open until the delay passes');

    w.flushTimers();
    assert.equal(w.modal(), null);
    assert.deepEqual(w.assigned, [], 'with onSuccess the merchant decides where to go');
});

test('without onSuccess, a confirmed payment goes to the return URL after closing', () => {
    const w = load();
    w.api.checkout({ sessionId: 'ses_abc' });
    w.send({ type: 'digipay-checkout-status', sessionId: 'ses_abc', status: 'confirmed', returnUrl: 'https://shop.example/thanks' });
    w.flushTimers();
    assert.equal(w.modal(), null);
    assert.deepEqual(w.assigned, ['https://shop.example/thanks']);
});

test('a return request closes the modal and navigates the shop page, not the iframe', () => {
    const w = load();
    w.api.checkout({ sessionId: 'ses_abc' });
    w.send({ type: 'digipay-checkout-return', sessionId: 'ses_abc', status: 'expired', url: 'https://shop.example/cart' });
    assert.equal(w.modal(), null);
    assert.deepEqual(w.assigned, ['https://shop.example/cart']);
});

test('messages from other windows or origins, and non-http URLs, are ignored', () => {
    const w = load();
    const onStatus = [];
    w.api.checkout({ sessionId: 'ses_abc', onStatus: e => onStatus.push(e) });

    w.send({ type: 'digipay-checkout-status', status: 'confirmed' }, { source: { other: true } });
    w.send({ type: 'digipay-checkout-status', status: 'confirmed' }, { origin: 'https://evil.example' });
    assert.equal(onStatus.length, 0);

    w.send({ type: 'digipay-checkout-return', url: 'javascript:alert(1)' });
    assert.equal(w.modal(), null);
    assert.deepEqual(w.assigned, []);
});

test('Escape pressed inside the checkout frame closes the modal', () => {
    const w = load();
    w.api.checkout({ sessionId: 'ses_abc' });
    w.send({ type: 'digipay-checkout-escape' });
    assert.equal(w.modal(), null);
});

// specs/033 US4: a donation button can point the tip page at a store, so it can offer DigiDollar.
test('button() takes a store instead of an address, plus the opening currency and a DD amount', () => {
    const w = load();
    const a = w.api.button({ store: 'sto_1', currency: 'dd', ddAmount: '5.00', name: 'Shop' });
    const url = new URL(a.href);
    assert.equal(url.pathname, '/pay/button');
    assert.equal(url.searchParams.get('store'), 'sto_1');
    assert.equal(url.searchParams.get('to'), null);
    assert.equal(url.searchParams.get('currency'), 'DD');
    assert.equal(url.searchParams.get('ddAmount'), '5.00');
    assert.equal(url.searchParams.get('name'), 'Shop');
});

test('button() keeps the DGB address alongside a store, and ignores an unknown currency', () => {
    const w = load();
    const a = w.api.button({ address: 'dgb1qabc', store: 'sto_1', currency: 'EUR' });
    const url = new URL(a.href);
    assert.equal(url.searchParams.get('to'), 'dgb1qabc');
    assert.equal(url.searchParams.get('store'), 'sto_1');
    assert.equal(url.searchParams.get('currency'), null);
});

test('button() still needs an address or a store', () => {
    const w = load();
    assert.throws(() => w.api.button({ name: 'Shop' }), /address.*store/);
});
