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
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// API domains whose responses should be cached for offline use
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

    // Cache assets individually — don't let one failed fetch kill the entire install.
    // cache.addAll() is all-or-nothing; this approach caches what it can.
    const cache = await caches.open(cacheName);
    const assets = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));

    await Promise.all(assets.map(async asset => {
        try {
            const request = new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' });
            const response = await fetch(request);
            if (response.ok) await cache.put(request, response);
        } catch {
            console.warn('SW install: failed to cache', asset.url);
        }
    }));

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
        // Offline and not in cache — return a basic offline response for navigations
        if (request.mode === 'navigate') {
            const offlineCache = await caches.open(cacheName);
            const fallback = await offlineCache.match('index.html');
            if (fallback) return fallback;
        }
        return new Response('Offline', { status: 503, statusText: 'Service Unavailable' });
    }
}

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
