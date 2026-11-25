// The CSV export's save script (specs/031-transaction-csv-export, research R4/R5).
// download.js is a plain browser script of global functions, so it is run here in a small sandbox with stand-ins for the
// browser pieces it touches: the document (an anchor to click), URL object URLs, matchMedia and the share sheet.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const source = readFileSync(new URL("../../wwwroot/js/download.js", import.meta.url), "utf8");

function sandbox({ coarse = false, share = null, canShare = null, locale = "en-US" } = {}) {
    const clicked = [];
    const revoked = [];
    const shared = [];
    const anchor = () => ({
        href: "",
        download: "",
        click() { clicked.push({ href: this.href, download: this.download }); },
    });
    const ctx = {
        Blob,
        File,
        setTimeout: (fn) => fn(),
        URL: {
            createObjectURL: () => "blob:test",
            revokeObjectURL: (u) => revoked.push(u),
        },
        document: {
            createElement: () => anchor(),
            body: { appendChild() {}, removeChild() {} },
        },
        window: { matchMedia: () => ({ matches: coarse }) },
        navigator: {},
        Intl: {
            NumberFormat: function () { return new Intl.NumberFormat(locale); },
        },
    };
    if (share) {
        ctx.navigator.share = async (data) => { shared.push(data); return share(data); };
        ctx.navigator.canShare = canShare ?? (() => true);
    }
    vm.createContext(ctx);
    vm.runInContext(source, ctx);
    return { ctx, clicked, revoked, shared };
}

const csv = "﻿Date,Asset\r\n2026-11-26 10:00:00,DGB\r\n";
const name = "digibyte-test-DGB-2026-11-26.csv";

// ----- saveCsvFile -----

test("on a desktop the file is downloaded under its name and the object URL is released", async () => {
    const { ctx, clicked, revoked, shared } = sandbox({ coarse: false, share: () => {} });
    assert.equal(await ctx.saveCsvFile(csv, name), "saved");
    assert.deepEqual(clicked, [{ href: "blob:test", download: name }]);
    assert.deepEqual(revoked, ["blob:test"]);
    assert.equal(shared.length, 0);   // a fine pointer never uses the share sheet
});

test("on a phone that can share files the share sheet gets a CSV file", async () => {
    const { ctx, clicked, shared } = sandbox({ coarse: true, share: () => {} });
    assert.equal(await ctx.saveCsvFile(csv, name), "shared");
    assert.equal(clicked.length, 0);
    assert.equal(shared[0].files[0].name, name);
    assert.equal(shared[0].files[0].type, "text/csv");
});

test("closing the share sheet saves nothing", async () => {
    const abort = Object.assign(new Error("closed"), { name: "AbortError" });
    const { ctx, clicked } = sandbox({ coarse: true, share: () => { throw abort; } });
    assert.equal(await ctx.saveCsvFile(csv, name), "cancelled");
    assert.equal(clicked.length, 0);
});

test("a share failure other than closing falls back to a download", async () => {
    const { ctx, clicked } = sandbox({ coarse: true, share: () => { throw new Error("NotAllowedError"); } });
    assert.equal(await ctx.saveCsvFile(csv, name), "saved");
    assert.equal(clicked.length, 1);
});

test("a phone that can't share files downloads instead", async () => {
    const { ctx, clicked } = sandbox({ coarse: true, share: () => {}, canShare: () => false });
    assert.equal(await ctx.saveCsvFile(csv, name), "saved");
    assert.equal(clicked.length, 1);
});

test("the saved file keeps the byte-order mark and the exact text", async () => {
    let blob;
    const { ctx } = sandbox();
    ctx.URL.createObjectURL = (b) => { blob = b; return "blob:test"; };
    await ctx.saveCsvFile(csv, name);
    const bytes = new Uint8Array(await blob.arrayBuffer());
    assert.deepEqual([...bytes.slice(0, 3)], [0xef, 0xbb, 0xbf]);
    assert.equal(new TextDecoder().decode(bytes.slice(3)), csv.slice(1));
    assert.match(blob.type, /^text\/csv/);
});

// ----- getDecimalMark: decides the CSV separator -----

test("a dot-decimal region gives a dot", () => {
    assert.equal(sandbox({ locale: "en-US" }).ctx.getDecimalMark(), ".");
});

test("a comma-decimal region gives a comma", () => {
    assert.equal(sandbox({ locale: "de-DE" }).ctx.getDecimalMark(), ",");
    assert.equal(sandbox({ locale: "es-ES" }).ctx.getDecimalMark(), ",");
});

test("a broken number format falls back to a dot", () => {
    const { ctx } = sandbox();
    ctx.Intl = { NumberFormat: function () { throw new Error("no Intl"); } };
    assert.equal(ctx.getDecimalMark(), ".");
});
