// "Return to {host}" (domHelpers.returnToSite in dom-helpers.js): close a tab a script opened, else go to the site.
// dom-helpers.js is a plain browser script, so it runs here in a small sandbox with stand-ins for the window.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const source = readFileSync(new URL("../../wwwroot/js/dom-helpers.js", import.meta.url), "utf8");

function sandbox({ opener = null, closes = true } = {}) {
    const assigned = [];
    let closeCalls = 0;
    const window = {
        opener,
        closed: false,
        close() { closeCalls++; if (closes) this.closed = true; },
        location: { assign: (u) => assigned.push(u) },
    };
    const ctx = {
        window,
        URL,
        setTimeout: (fn) => fn(),
        localStorage: { getItem: () => null },
        navigator: { language: "en-US" },
        document: { getElementById: () => null },
    };
    vm.createContext(ctx);
    vm.runInContext(source, ctx);
    return { helpers: window.domHelpers, assigned, closeCalls: () => closeCalls };
}

test("a tab a script opened is closed, and nothing is navigated", () => {
    const s = sandbox({ opener: {} });
    s.helpers.returnToSite("https://pay.dgbwallet.app/merchant/signin");
    assert.equal(s.closeCalls(), 1);
    assert.deepEqual(s.assigned, []);
});

test("when closing is blocked the tab goes to the return address", () => {
    const s = sandbox({ opener: {}, closes: false });
    s.helpers.returnToSite("https://pay.dgbwallet.app/checkout/abc");
    assert.equal(s.closeCalls(), 1);
    assert.deepEqual(s.assigned, ["https://pay.dgbwallet.app/checkout/abc"]);
});

test("without an opener the tab goes straight to the return address", () => {
    const s = sandbox();
    s.helpers.returnToSite("http://localhost:5252/donate/x");
    assert.equal(s.closeCalls(), 0);
    assert.deepEqual(s.assigned, ["http://localhost:5252/donate/x"]);
});

test("anything but http(s) is never followed", () => {
    const s = sandbox();
    s.helpers.returnToSite("javascript:alert(1)");
    s.helpers.returnToSite("data:text/html,hi");
    s.helpers.returnToSite("not a url");
    assert.deepEqual(s.assigned, []);
    assert.equal(s.closeCalls(), 0);
});
