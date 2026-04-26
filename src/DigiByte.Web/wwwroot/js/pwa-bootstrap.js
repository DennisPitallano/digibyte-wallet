// Service-worker registration + PWA update plumbing. Lifted verbatim out
// of index.html so the page CSP can drop 'unsafe-inline' from script-src.
//
// What this does:
//   • Registers /service-worker.js with updateViaCache: 'none' (the SW
//     itself is always fetched fresh; cache-control is Workbox's job).
//   • Polls for updates every 60 minutes.
//   • When a new SW finishes installing, exposes a `pwaUpdate` API the
//     Razor app calls to surface a "new version available" banner instead
//     of auto-skipWaiting (which would yank a signed-in session under the
//     user mid-flow).
//   • controllerchange triggers exactly one reload after skipWaiting so
//     the user lands on the new build.
navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }).then(reg => {
    setInterval(() => reg.update(), 60 * 60 * 1000);

    function onNewSW(sw) {
        if (sw.state === 'installed' && navigator.serviceWorker.controller) {
            window._swWaiting = sw;
            if (window._pwaUpdateCallback) {
                window._pwaUpdateCallback.invokeMethodAsync('OnUpdateAvailable');
            }
        }
    }

    if (reg.waiting) { onNewSW(reg.waiting); }

    reg.addEventListener('updatefound', () => {
        const newSW = reg.installing;
        if (!newSW) return;
        newSW.addEventListener('statechange', () => onNewSW(newSW));
    });
});

let refreshing = false;
navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!refreshing) { refreshing = true; location.reload(); }
});

window.pwaUpdate = {
    registerCallback: (dotnetRef) => {
        window._pwaUpdateCallback = dotnetRef;
        if (window._swWaiting) {
            dotnetRef.invokeMethodAsync('OnUpdateAvailable');
        }
    },
    skipWaiting: () => {
        if (window._swWaiting) {
            window._swWaiting.postMessage({ type: 'SKIP_WAITING' });
            window._swWaiting = null;
        }
        // Fallback: if controllerchange doesn't fire within 2s, force reload.
        setTimeout(() => { if (!refreshing) { refreshing = true; location.reload(); } }, 2000);
    },
};

// Force-check for a new service worker version and return true if one is waiting.
window.pwaCheckForUpdate = async () => {
    const reg = await navigator.serviceWorker.getRegistration('/');
    if (!reg) return false;
    await reg.update();
    // Give the browser a moment to fetch and compare service-worker.js.
    await new Promise(r => setTimeout(r, 1500));
    return !!(reg.waiting || reg.installing);
};

// Skip waiting on any waiting SW and let the controllerchange listener reload.
window.pwaApplyUpdate = () => {
    navigator.serviceWorker.getRegistration('/').then(reg => {
        const sw = reg?.waiting || reg?.installing;
        if (sw) {
            sw.postMessage({ type: 'SKIP_WAITING' });
        } else {
            location.reload();
        }
    });
};
