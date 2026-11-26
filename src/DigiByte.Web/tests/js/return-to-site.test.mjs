// "Return to {host}" (domHelpers.returnToSite in dom-helpers.js): close a tab a script opened, else go to the site.
// dom-helpers.js is a plain browser script, so it runs here in a small sandbox with stand-ins for the window.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const source = readFileSync(new URL("../../wwwroot/js/dom-helpers.js", import.meta.url), "utf8");

function sandbox({ opener = null, closes = true, displayMode = "browser", iosStandalone = false } = {}) {
    const assigned = [];
    const opened = [];
    let closeCalls = 0;
    const window = {
        opener,
        closed: false,
        navigator: { standalone: iosStandalone },
        matchMedia: (q) => ({ matches: q === `(display-mode: ${displayMode})` }),
        open: (u, target, features) => { opened.push([u, target, features]); return null; },
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
    return { helpers: window.domHelpers, assigned, opened, closeCalls: () => closeCalls };
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

for (const displayMode of ["standalone", "window-controls-overlay", "minimal-ui", "fullscreen"]) {
    test(`in the installed app (${displayMode}) the site opens in the browser, never inside the wallet`, () => {
        const s = sandbox({ displayMode, opener: {} });
        const result = s.helpers.returnToSite("https://pay.dgbwallet.app/merchant/signin");
        assert.equal(result, "browser");
        assert.deepEqual(s.opened, [["https://pay.dgbwallet.app/merchant/signin", "_blank", "noopener,noreferrer"]]);
        assert.deepEqual(s.assigned, []);
        assert.equal(s.closeCalls(), 0);
    });
}

test("the iOS home-screen app also opens the site in the browser", () => {
    const s = sandbox({ iosStandalone: true });
    assert.equal(s.helpers.returnToSite("https://pay.dgbwallet.app/pay/ses_1"), "browser");
    assert.equal(s.opened.length, 1);
    assert.deepEqual(s.assigned, []);
});

test("in a browser tab nothing opens a new window", () => {
    const s = sandbox();
    assert.equal(s.helpers.returnToSite("https://pay.dgbwallet.app/pay/ses_1"), "navigated");
    assert.deepEqual(s.opened, []);
});

test("an installed app still refuses anything but http(s)", () => {
    const s = sandbox({ displayMode: "standalone" });
    assert.equal(s.helpers.returnToSite("javascript:alert(1)"), "refused");
    assert.deepEqual(s.opened, []);
});
