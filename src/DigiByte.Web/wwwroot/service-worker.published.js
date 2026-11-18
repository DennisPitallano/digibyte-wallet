// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

// Allow the page to tell us to skip waiting and activate immediately
self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const apiCacheName = 'api-cache-v1';
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.webp$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.txt$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// API domains whose responses should be cached for offline use.
// Never add our own node API or /config.json here: a cached /api/health would make a dead node look ready, and a cached
// config.json would keep old deployment settings (specs/005-wallet-own-node-source).
// Sponsors (specs/006-sponsors-page): the list at raw.githubusercontent.com is deliberately NOT cached here. The browser cache's short
// lifetime bounds how fresh it is, and the Sponsors page keeps its own last good copy for the offline "may be out of date" state.
const cacheableApiPatterns = [
    /api\.coingecko\.com/,
    /digiexplorer\.info\/api/,
    /api\.github\.com/
];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Cache every asset, then verify. If any asset could not be cached the install FAILS, so a
    // half-filled cache never replaces a working worker (the old worker and cache stay in use and
    // the install is retried on the next update check). A failed install is invisible to the user.
    const cache = await caches.open(cacheName);
    const assets = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));

    const failed = [];
    await Promise.all(assets.map(async asset => {
        try {
            const request = new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' });
            const response = await fetch(request);
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            await cache.put(request, response);
        } catch {
            failed.push(asset.url);
        }
    }));

    if (failed.length > 0) {
        console.warn('SW install: failed to cache', failed.length, 'asset(s), aborting install', failed.slice(0, 5));
        await caches.delete(cacheName);
        throw new Error('Service worker install incomplete');
    }

    // Force activate — unstick users on old versions
    self.skipWaiting();
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Claim all clients so they immediately use the new SW + new cached assets.
    // This triggers controllerchange → page reload with fresh index.html.
    await self.clients.claim();

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    const request = event.request;

    if (request.method !== 'GET') {
        return fetch(request);
    }

    // Network-first for external API calls — cache successful responses
    if (cacheableApiPatterns.some(p => p.test(request.url))) {
        return networkFirstWithCache(request);
    }

    // Cache-first for app shell assets
    const shouldServeIndexHtml = request.mode === 'navigate'
        && !manifestUrlList.some(url => url === request.url);

    const cacheRequest = shouldServeIndexHtml ? 'index.html' : request;
    const cache = await caches.open(cacheName);
    const cachedResponse = await cache.match(cacheRequest);

    if (cachedResponse) return cachedResponse;

    // Network fetch — catch errors to avoid unhandled rejection spam
    try {
        return await fetch(request);
    } catch {
        // Offline and not in cache. For page navigations show a helpful recovery page instead of bare text.
        if (request.mode === 'navigate') {
            const offlineCache = await caches.open(cacheName);
            const fallback = await offlineCache.match('index.html');
            if (fallback) return fallback;
            return new Response(offlinePageHtml, {
                status: 503,
                statusText: 'Service Unavailable',
                headers: { 'Content-Type': 'text/html; charset=utf-8' }
            });
        }
        return new Response('Offline', { status: 503, statusText: 'Service Unavailable' });
    }
}

// Shown when the wallet cannot load and nothing is cached. Self-contained (no external files).
// Repairing only removes service workers and cached app files; wallet data (localStorage / IndexedDB) is untouched.
const offlinePageHtml = `<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>DigiByte Wallet - cannot load</title>
<style>
body{margin:0;padding:2rem 1.25rem;background:#001529;color:rgba(255,255,255,.85);font:15px/1.5 system-ui,sans-serif}
h1{font-size:1.25rem;color:#fff;margin:0 0 .5rem}p{margin:.5rem 0}ol{margin:.5rem 0 1rem 1.25rem;padding:0}li{margin:.35rem 0}
.row{display:flex;gap:.75rem;margin:1rem 0;flex-wrap:wrap}
button{flex:1 1 140px;padding:.75rem 1rem;border:0;border-radius:.5rem;background:#06c;color:#fff;font:inherit;font-weight:600}
button+button{background:rgba(255,255,255,.15)}
.warn{padding:.75rem;border-radius:.5rem;background:rgba(239,68,68,.15)}
</style></head><body>
<h1>DigiByte Wallet could not load</h1>
<p>You appear to be offline, or the connection dropped while the app was loading. Your wallet and funds are safe.</p>
<div class="row"><button id="r">Try again</button><button id="f">Repair app files</button></div>
<p><b>What you can do:</b></p>
<ol>
<li>Check your internet connection, turn off VPN or data saver, then tap Try again.</li>
<li>Tap <b>Repair app files</b>. This clears only downloaded app files and keeps your wallet.</li>
<li>Close other tabs and apps to free memory, then reopen dgbwallet.app.</li>
<li>Try another browser or the installed app.</li>
</ol>
<p class="warn"><b>Do not</b> clear site data / cookies for dgbwallet.app: that erases the wallet stored in this browser. Keep your 24-word recovery phrase safe.</p>
<script>
document.getElementById('r').onclick=function(){location.reload()};
document.getElementById('f').onclick=async function(){
try{var g=await navigator.serviceWorker.getRegistrations();await Promise.all(g.map(function(x){return x.unregister()}))}catch(e){}
try{var k=await caches.keys();await Promise.all(k.map(function(x){return caches.delete(x)}))}catch(e){}
location.reload()};
</script></body></html>`;

async function networkFirstWithCache(request) {
    const cache = await caches.open(apiCacheName);
    try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 8000); // 8s timeout
        const networkResponse = await fetch(request, { signal: controller.signal });
        clearTimeout(timeoutId);
        if (networkResponse.ok) {
            // Store with timestamp header for TTL checks
            const headers = new Headers(networkResponse.headers);
            headers.set('sw-cached-at', Date.now().toString());
            const timedResponse = new Response(await networkResponse.clone().blob(), {
                status: networkResponse.status,
                statusText: networkResponse.statusText,
                headers
            });
            cache.put(request, timedResponse);
        }
        return networkResponse;
    } catch {
        // Network failed — return cached version if available
        const cached = await cache.match(request);
        if (cached) return cached;
        // No cache available — return a minimal offline JSON response
        return new Response(JSON.stringify({ offline: true }), {
            status: 503,
            headers: { 'Content-Type': 'application/json' }
        });
    }
}
