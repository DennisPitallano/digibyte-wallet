// The pure helpers of the scan panel's browser code (specs/030-desktop-paste-scan).
// qrscanner.js is a plain browser script that sets window.qrScanner, so it is run here in a small sandbox.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const source = readFileSync(new URL("../../wwwroot/js/qrscanner.js", import.meta.url), "utf8");
const sandbox = { window: {} };
vm.createContext(sandbox);
vm.runInContext(source, sandbox);
const scanner = sandbox.window.qrScanner;

// ----- _fitWithin: pictures are scaled down before reading, never up -----

test("a picture larger than the limit is scaled so its longer side fits", () => {
    assert.deepEqual({ ...scanner._fitWithin(4000, 3000, 2048) }, { w: 2048, h: 1536 });
});

test("a portrait picture is scaled on its height", () => {
    assert.deepEqual({ ...scanner._fitWithin(3000, 4000, 2048) }, { w: 1536, h: 2048 });
});

test("a picture within the limit is left as it is", () => {
    assert.deepEqual({ ...scanner._fitWithin(1000, 800, 2048) }, { w: 1000, h: 800 });
    assert.deepEqual({ ...scanner._fitWithin(2048, 2048, 2048) }, { w: 2048, h: 2048 });
});

test("a zero, negative or missing size gives an empty size", () => {
    assert.deepEqual({ ...scanner._fitWithin(0, 800, 2048) }, { w: 0, h: 0 });
    assert.deepEqual({ ...scanner._fitWithin(-5, 800, 2048) }, { w: 0, h: 0 });
    assert.deepEqual({ ...scanner._fitWithin(800, undefined, 2048) }, { w: 0, h: 0 });
});

// ----- decodeBlob: refusals happen before any reading -----

test("a file that is not an image is refused without reading it", async () => {
    assert.equal(await scanner.decodeBlob({ type: "text/plain", size: 10 }), "notimage");
    assert.equal(await scanner.decodeBlob({ type: "", size: 10 }), "notimage");
    assert.equal(await scanner.decodeBlob(null), "notimage");
});

test("an image over 10 MB is refused without reading it", async () => {
    assert.equal(await scanner.decodeBlob({ type: "image/png", size: 10 * 1024 * 1024 + 1 }), "toolarge");
});

// ----- _giveUpClock: Scan from screen stops looking after a minute (FR-017) -----

test("the screen scan keeps looking before a minute has passed", () => {
    assert.equal(scanner._giveUpClock(1000, 1000), false);
    assert.equal(scanner._giveUpClock(1000, 60999), false);
});

test("the screen scan gives up at a minute and after", () => {
    assert.equal(scanner._giveUpClock(1000, 61000), true);
    assert.equal(scanner._giveUpClock(1000, 500000), true);
});

test("a different limit is honoured", () => {
    assert.equal(scanner._giveUpClock(0, 4999, 5000), false);
    assert.equal(scanner._giveUpClock(0, 5000, 5000), true);
});

test("stopping a screen scan that never started does nothing", () => {
    assert.doesNotThrow(() => scanner.stopScreen());
    assert.doesNotThrow(() => scanner.stopScreen());
});

test("a share granted after the scan was stopped is ended at once", async () => {
    const stopped = [];
    let grant;
    const track = { stop: () => stopped.push("track"), addEventListener: () => { } };
    const stream = { getTracks: () => [track], getVideoTracks: () => [track] };
    sandbox.navigator = { mediaDevices: { getDisplayMedia: () => new Promise(resolve => { grant = resolve; }) } };
    vm.runInContext("navigator = this.navigator", sandbox);

    const pending = scanner.startScreen({ invokeMethodAsync: () => { } });
    scanner.stopScreen();          // the panel closed while the browser's prompt was open
    grant(stream);                 // ...and then the person picked a window

    assert.equal(await pending, "cancelled");
    assert.deepEqual(stopped, ["track"]);
    assert.equal(scanner.screen, null);
});
