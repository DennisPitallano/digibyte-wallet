// Waits until a URL answers 2xx (default up to 5 minutes, the first run builds the solution).
// Usage: node scripts/wait-for.mjs <url> [timeoutSeconds]
const [url, timeoutArg] = process.argv.slice(2);
if (!url) {
    console.error('usage: wait-for.mjs <url> [timeoutSeconds]');
    process.exit(2);
}
const deadline = Date.now() + Number(timeoutArg || 300) * 1000;
while (Date.now() < deadline) {
    try {
        const res = await fetch(url);
        if (res.ok) process.exit(0);
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 1000));
}
console.error(`Timed out waiting for ${url}`);
process.exit(1);
