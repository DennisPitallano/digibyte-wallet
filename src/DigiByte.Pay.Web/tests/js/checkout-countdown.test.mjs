// node --test for wwwroot/checkout-countdown.js: status messages to the embedding page, and the
// return-to-shop countdown (never navigates the embed iframe itself; pauses on the receipt link).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, '..', '..', 'wwwroot', 'checkout-countdown.js'), 'utf8');

function el(attrs = {}) {
    return {
        attrs: { ...attrs },
        textContent: '',
        getAttribute(k) { return k in this.attrs ? this.attrs[k] : null; },
        setAttribute(k, v) { this.attrs[k] = String(v); },
        removeAttribute(k) { delete this.attrs[k]; },
        hasAttribute(k) { return k in this.attrs; },
        contains(other) { return other === this; },
        matches() { return false; },
    };
}

// A fake clock: setInterval/setTimeout run when advance() passes their due time.
function clock() {
    let now = 0, seq = 0;
    const timers = new Map();
    const add = (fn, ms, repeat) => { const id = ++seq; timers.set(id, { fn, ms: Math.max(1, ms || 0), due: now + Math.max(1, ms || 0), repeat }); return id; };
    return {
        setInterval: (fn, ms) => add(fn, ms, true),
        setTimeout: (fn, ms) => add(fn, ms, false),
        clearInterval: id => timers.delete(id),
        clearTimeout: id => timers.delete(id),
        advance(ms) {
            const end = now + ms;
            for (;;) {
                let next = null;
                for (const [id, t] of timers) if (t.due <= end && (!next || t.due < next[1].due)) next = [id, t];
                if (!next) break;
                const [id, t] = next;
                now = t.due;
                if (t.repeat) t.due += t.ms; else timers.delete(id);
                t.fn();
            }
            now = end;
        },
    };
}

function load({ inFrame = true, status = 'pending', returnUrl = null, returnLinkUrl = null } = {}) {
    const posted = [];
    const assigned = [];
    const listeners = {};
    const card = el({ 'data-digipay-status': status, 'data-digipay-session': 'ses_abc' });
    const hosts = [];
    const pausers = [];
    const time = clock();
    const document = {
        readyState: 'complete',
        activeElement: null,
        addEventListener(type, fn) { (listeners[type] ||= []).push(fn); },
        querySelector(sel) {
            if (sel === '[data-digipay-status]') return card;
            return null;
        },
        querySelectorAll(sel) {
            if (sel.startsWith('[data-digipay-return-merchant]')) return hosts.filter(h => !h.hasAttribute('data-digipay-return-armed'));
            if (sel === '[data-digipay-return-pause]') return pausers;
            return [];
        },
    };
    const ctx = {
        document,
        location: { assign: url => assigned.push(url), href: 'https://pay.example/pay/ses_abc' },
        addEventListener() { },
        ...time,
        Date,
        parseInt,
        isNaN,
        Math,
        String,
        Array,
    };
    ctx.window = ctx;
    ctx.parent = inFrame ? { postMessage: msg => posted.push(msg) } : ctx;
    if (!inFrame) ctx.postMessage = msg => posted.push(msg);

    if (returnUrl) {
        const host = el({ 'data-digipay-return-merchant': '', 'data-digipay-return-url': returnUrl, 'data-digipay-return-seconds': '8' });
        const counter = el();
        host.querySelector = () => counter;
        host.counter = counter;
        hosts.push(host);
    }
    vm.runInNewContext(source, ctx);

    return {
        posted, assigned, card, hosts, pausers, document, time,
        click(target) {
            const e = { target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
            (listeners.click || []).forEach(fn => fn(e));
            return e;
        },
        key(key) { (listeners.keydown || []).forEach(fn => fn({ key })); },
    };
}

const statusMessages = posted => posted.filter(m => m.type === 'digipay-checkout-status');

test('inside the embed, each status change is posted to the parent once', () => {
    const page = load({ status: 'pending' });
    assert.deepEqual(statusMessages(page.posted).map(m => m.status), ['pending']);
    assert.equal(statusMessages(page.posted)[0].sessionId, 'ses_abc');

    page.time.advance(2000);
    assert.equal(statusMessages(page.posted).length, 1, 'no repeat without a change');

    page.card.setAttribute('data-digipay-status', 'paid');
    page.time.advance(600);
    page.card.setAttribute('data-digipay-status', 'confirmed');
    page.time.advance(600);
    assert.deepEqual(statusMessages(page.posted).map(m => m.status), ['pending', 'paid', 'confirmed']);
});

test('standalone, nothing is posted', () => {
    const page = load({ inFrame: false });
    page.time.advance(2000);
    assert.equal(statusMessages(page.posted).length, 0);
});

test('inside the embed, the return countdown asks the parent to navigate and never moves the iframe', () => {
    const page = load({ status: 'confirmed', returnUrl: 'https://shop.example/thanks' });
    page.time.advance(9000);
    const ret = page.posted.find(m => m.type === 'digipay-checkout-return');
    assert.ok(ret, 'a return message was posted');
    assert.equal(ret.url, 'https://shop.example/thanks');
    assert.equal(ret.sessionId, 'ses_abc');
    assert.equal(ret.status, 'confirmed');
    assert.deepEqual(page.assigned, []);
});

test('standalone, the return countdown navigates after 8 seconds', () => {
    const page = load({ inFrame: false, status: 'confirmed', returnUrl: 'https://shop.example/thanks' });
    page.time.advance(7000);
    assert.deepEqual(page.assigned, [], 'not before 8 seconds');
    page.time.advance(2000);
    assert.deepEqual(page.assigned, ['https://shop.example/thanks']);
});

test('the countdown holds while the receipt link has focus or the pointer', () => {
    const page = load({ inFrame: false, status: 'confirmed', returnUrl: 'https://shop.example/thanks' });
    const receipt = el({ 'data-digipay-return-pause': '' });
    page.pausers.push(receipt);
    page.document.activeElement = receipt;
    page.time.advance(20000);
    assert.deepEqual(page.assigned, []);
    assert.equal(page.hosts[0].getAttribute('data-digipay-return-paused'), '1');

    page.document.activeElement = null;
    receipt.matches = sel => sel === ':hover';
    page.time.advance(20000);
    assert.deepEqual(page.assigned, [], 'hover pauses too');

    receipt.matches = () => false;
    page.time.advance(9000);
    assert.deepEqual(page.assigned, ['https://shop.example/thanks']);
});

test('inside the embed, a Back to the shop link click goes to the parent instead', () => {
    const page = load({ status: 'expired' });
    const link = el({ href: 'https://shop.example/cart', 'data-digipay-return-link': '' });
    const target = { closest: sel => (sel === 'a[data-digipay-return-link]' ? link : null) };
    const e = page.click(target);
    assert.equal(e.defaultPrevented, true);
    const ret = page.posted.find(m => m.type === 'digipay-checkout-return');
    assert.equal(ret.url, 'https://shop.example/cart');
    assert.deepEqual(page.assigned, []);
});

test('standalone, a Back to the shop link is left alone', () => {
    const page = load({ inFrame: false });
    const link = el({ href: 'https://shop.example/cart' });
    const e = page.click({ closest: () => link });
    assert.equal(e.defaultPrevented, false);
});

test('Escape inside the embed asks the parent to close', () => {
    const page = load();
    page.key('Escape');
    assert.ok(page.posted.some(m => m.type === 'digipay-checkout-escape'));
});
