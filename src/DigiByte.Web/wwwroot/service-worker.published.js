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
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.webp$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/, /\.txt$/, /\.mp3$/ ];
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

    // A finished download does not take over a running wallet by itself: taking over reloads the page, which
    // locks the wallet and loses whatever the user was doing. It waits, the page shows "Update available",
    // and the user chooses when (or the page applies it while the wallet is locked). It takes over at once
    // only when there is nothing sound to protect: see shouldTakeOverNow.
    if (await shouldTakeOverNow()) self.skipWaiting();
}

// True when no complete copy of the app is in use: nothing has been saved yet (a first install), or the saved
// copy has no index.html. Versions before 0.7.0 kept a cache even when some downloads had failed; such a cache
// serves old stylesheets and scripts next to new page code, so replacing it must not wait for anyone's say-so.
async function shouldTakeOverNow() {
    try {
        const others = (await caches.keys()).filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName);
        if (others.length === 0) return true;
        for (const key of others) {
            const old = await caches.open(key);
            if (!(await old.match('index.html'))) return true;
        }
        return false;
    } catch {
        return true;
    }
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

// Shown when the wallet cannot load and nothing is cached. Self-contained: no outside files. Looks like the "could not start" panel (css/splash.css).
// Repairing only removes service workers and cached app files; wallet data (localStorage / IndexedDB) is untouched.
// Baku (images/mascot/poses/support.webp at 224px), carried inside this file: when this page is shown the cache is
// empty and the network is down, so no picture could be fetched.
const offlineBaku = "data:image/webp;base64,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";

const offlinePageHtml = `<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#001529">
<title>DigiByte Wallet - cannot load</title>
<style>
html{min-height:100%}
body{margin:0;min-height:100vh;box-sizing:border-box;padding:calc(2rem + env(safe-area-inset-top,0px)) 1.25rem calc(2rem + env(safe-area-inset-bottom,0px))}
body {
    background: radial-gradient(120% 80% at 50% 0%, #012a52 0%, #001529 60%);
    color: rgba(255, 255, 255, .82);
    font: 15px/1.5 system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
}
body .br-card { max-width: 480px; margin: 0 auto; }
body .br-head { display: flex; align-items: center; gap: .75rem; }
body .br-baku { flex: 0 0 auto; width: 112px; height: 112px; object-fit: contain; filter: drop-shadow(0 8px 18px rgba(0, 102, 255, .35)); }
body .br-bubble {
    flex: 1 1 auto;
    padding: .9rem 1rem;
    border: 1px solid rgba(77, 148, 255, .28);
    border-radius: 1rem;
    background: rgba(0, 102, 204, .16);
}
body h2 { margin: 0 0 .25rem; font-size: 1.15rem; line-height: 1.3; font-weight: 700; color: #fff; }
body p { margin: 0; }
body .br-actions { display: flex; gap: .75rem; margin: 1.25rem 0; flex-wrap: wrap; }
body button {
    flex: 1 1 140px;
    min-height: 48px;
    padding: .75rem 1rem;
    border: 0;
    border-radius: .875rem;
    background: linear-gradient(180deg, #1a7dff, #0066cc);
    box-shadow: 0 6px 16px rgba(0, 102, 204, .35);
    color: #fff;
    font: inherit;
    font-weight: 700;
    cursor: pointer;
}
body button + button { background: rgba(255, 255, 255, .08); box-shadow: inset 0 0 0 1px rgba(255, 255, 255, .16); }
body button:focus-visible { outline: 2px solid #7fb4ff; outline-offset: 2px; }
body .br-title { margin: 0 0 .5rem; font-size: .75rem; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; color: rgba(255, 255, 255, .55); }
body ol { list-style: none; margin: 0 0 1.25rem; padding: 0; counter-reset: br-step; border: 1px solid rgba(255, 255, 255, .1); border-radius: 1rem; background: rgba(255, 255, 255, .04); }
body li { counter-increment: br-step; position: relative; padding: .75rem 1rem .75rem 3.25rem; }
body li + li { border-top: 1px solid rgba(255, 255, 255, .08); }
body li::before {
    content: counter(br-step);
    position: absolute;
    left: 1rem;
    top: .75rem;
    width: 1.5rem;
    height: 1.5rem;
    border-radius: 50%;
    background: rgba(77, 148, 255, .2);
    color: #9cc6ff;
    font-size: .75rem;
    font-weight: 700;
    line-height: 1.5rem;
    text-align: center;
}
body li b, body .br-warn b { color: #fff; }
body .br-warn { padding: .85rem 1rem; border: 1px solid rgba(239, 68, 68, .35); border-radius: 1rem; background: rgba(239, 68, 68, .12); font-size: .875rem; }
@media (max-width: 380px) {
    body .br-head { flex-direction: column; text-align: center; }
}
</style></head><body>
<main class="br-card">
<div class="br-head">
<img class="br-baku" id="b" src="${offlineBaku}" alt="" width="112" height="112">
<div class="br-bubble">
<h2>DigiByte Wallet could not load</h2>
<p>You appear to be offline, or the connection dropped while the app was loading. Your wallet and funds are safe.</p>
</div>
</div>
<div class="br-actions"><button id="r">Try again</button><button id="f">Repair app files</button></div>
<p class="br-title">What you can do</p>
<ol>
<li>Check your internet connection, turn off VPN or data saver, then tap Try again.</li>
<li>Tap <b>Repair app files</b>. This clears only downloaded app files and keeps your wallet.</li>
<li>Close other tabs and apps to free memory, then reopen dgbwallet.app.</li>
<li>Try another browser or the installed app.</li>
</ol>
<p class="br-warn"><b>Do not</b> clear site data / cookies for dgbwallet.app: that erases the wallet stored in this browser. Keep your 24-word recovery phrase safe.</p>
</main>
<script>
(function(){var b=document.getElementById('b');var off=false;try{off=localStorage.getItem('dgb-baku-mode')==='off'}catch(e){}
if(off){b.remove()}else{b.onerror=function(){b.remove()}}})();
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
