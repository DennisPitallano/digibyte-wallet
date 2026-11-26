// node --test for wwwroot/js/count-up.js: the home page stats count up from zero when they come into view, end on
// exactly the server's text, and stay still for reduced motion.
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

let frames, observed, reduce;

function el(countTo, text, decimals = 0) {
    const classes = new Set();
    return {
        dataset: { countTo: String(countTo), decimals: String(decimals) },
        textContent: text,
        classList: { add: c => classes.add(c), remove: c => classes.delete(c), has: c => classes.has(c) },
    };
}

beforeEach(() => {
    frames = [];
    observed = [];
    reduce = false;
    let now = 0;
    globalThis.performance = { now: () => now };
    globalThis.requestAnimationFrame = fn => frames.push(() => fn((now += 400)));
    globalThis.window = {
        matchMedia: () => ({ matches: reduce }),
        IntersectionObserver: true,
    };
    globalThis.IntersectionObserver = class {
        constructor(cb) { this.cb = cb; }
        observe(t) { observed.push(t); this.cb([{ isIntersecting: true, target: t }]); }
        unobserve() {}
    };
});

const { countUp } = await import('../../wwwroot/js/count-up.js');
const root = items => ({ querySelectorAll: () => items });
const run = () => { while (frames.length) frames.shift()(); };

test('numbers count up and finish on exactly the server text', () => {
    const merchants = el(1234, '1,234');
    countUp(root([merchants]));
    frames.shift()(); // first frame: part way
    assert.notEqual(merchants.textContent, '1,234');
    assert.ok(merchants.classList.has('is-counting'));
    run();
    assert.equal(merchants.textContent, '1,234');
    assert.ok(merchants.classList.has('is-done'));
});

test('zero and non-numbers are left alone', () => {
    const zero = el(0, '0');
    const junk = el('n/a', 'n/a');
    countUp(root([zero, junk]));
    run();
    assert.equal(zero.textContent, '0');
    assert.equal(junk.textContent, 'n/a');
});

test('reduced motion keeps the final numbers without animating', () => {
    reduce = true;
    const n = el(67, '67');
    countUp(root([n]));
    assert.equal(observed.length, 0);
    assert.equal(frames.length, 0);
    assert.equal(n.textContent, '67');
});

test('each number is counted once', () => {
    const n = el(10, '10');
    countUp(root([n]));
    run();
    countUp(root([n]));
    assert.equal(frames.length, 0);
});
