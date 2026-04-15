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

    // Do NOT call self.skipWaiting() here — let the page show the update banner
    // and let the user decide when to activate the new version.
    // The PwaUpdateBanner component will post SKIP_WAITING when the user clicks "Update".

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Claim all open clients so they switch to this SW without a reload
    await clients.claim();

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

    return cachedResponse || fetch(request);
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
