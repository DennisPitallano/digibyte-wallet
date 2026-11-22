// Service-worker registration + PWA update plumbing. Lifted verbatim out
// of index.html so the page CSP can drop 'unsafe-inline' from script-src.
//
// How an update reaches the user:
//   • The browser looks for a new service-worker.js when the app opens, every 60 minutes while it
//     stays open, and when the app comes back to the foreground (at most every 10 minutes).
//   • A new worker downloads every file, then WAITS (see service-worker.published.js). It does not
//     take over by itself, because taking over reloads the page and locks the wallet.
//   • One that was already waiting when the app opens is applied at once, before the app starts.
//   • When one is waiting, the Razor app is told (pwaUpdate.registerCallback). It shows "Update
//     available", or applies it straight away while the wallet is locked.
//   • Applying = asking the waiting worker to take over; controllerchange then reloads the page once.
const UPDATE_CHECK_MS = 60 * 60 * 1000;
const FOREGROUND_CHECK_MS = 10 * 60 * 1000;
const APPLIED_AT_KEY = 'pwa-update-applied-at';
let lastUpdateCheck = Date.now();

function appliedRecently() {
    try { return Date.now() - Number(sessionStorage.getItem(APPLIED_AT_KEY) || 0) < 60000; } catch (e) { return false; }
}

function notifyUpdateReady() {
    if (window._pwaUpdateCallback) {
        window._pwaUpdateCallback.invokeMethodAsync('OnUpdateAvailable');
    }
}

navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }).then(reg => {
    const check = () => { lastUpdateCheck = Date.now(); reg.update().catch(() => { }); };
    setInterval(check, UPDATE_CHECK_MS);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && Date.now() - lastUpdateCheck > FOREGROUND_CHECK_MS) check();
    });

    function onNewSW(sw) {
        // "installed" with a worker already in control means a finished update that is waiting.
        if (sw.state === 'installed' && navigator.serviceWorker.controller) notifyUpdateReady();
    }

    // An update that was already waiting when the app opened (downloaded last time and not applied) goes in
    // now: the page has only just loaded, so nothing is unlocked or half-entered yet. It also means a version
    // that cannot start is replaced without needing its own code to run. At most once a minute, so a worker
    // that never takes over cannot cause a reload loop.
    if (reg.waiting && navigator.serviceWorker.controller && !window._pwaUpdateCallback && !appliedRecently()) {
        try { sessionStorage.setItem(APPLIED_AT_KEY, String(Date.now())); } catch (e) { }
        reg.waiting.postMessage({ type: 'SKIP_WAITING' });
    } else if (reg.waiting) {
        onNewSW(reg.waiting);
    }

    reg.addEventListener('updatefound', () => {
        const newSW = reg.installing;
        if (!newSW) return;
        newSW.addEventListener('statechange', () => onNewSW(newSW));
    });
});

let refreshing = false;
// A first-time visitor has no controller yet: the new worker claiming the page is not an
// update, and reloading would cut the boot short mid-download. Only reload for real updates.
const hadController = !!navigator.serviceWorker.controller;
navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (!hadController) return;
    if (!refreshing) { refreshing = true; location.reload(); }
});

// The worker that has finished downloading and is waiting to take over, or null.
async function waitingWorker() {
    try {
        const reg = await navigator.serviceWorker.getRegistration();
        return (reg && reg.waiting) || null;
    } catch (e) { return null; }
}

// Asks the waiting worker to take over. Returns false, and does nothing, when no update is waiting:
// a reload without an update would only lock the wallet for no reason.
async function applyWaitingUpdate() {
    const sw = await waitingWorker();
    if (!sw) return false;
    sw.postMessage({ type: 'SKIP_WAITING' });
    // If controllerchange has not reloaded the page after a few seconds, reload anyway.
    setTimeout(() => { if (!refreshing) { refreshing = true; location.reload(); } }, 4000);
    return true;
}

window.pwaUpdate = {
    registerCallback: async (dotnetRef) => {
        window._pwaUpdateCallback = dotnetRef;
        if (await waitingWorker()) notifyUpdateReady();
    },
    // True when an update has been downloaded and is waiting.
    isReady: async () => !!(await waitingWorker()),
    // Applies the waiting update; false when there is none.
    apply: applyWaitingUpdate,
};

// "Check for Updates" in Settings. Asks the browser to look for a new version now and says what it found:
//   'none'         the installed version is the newest
//   'ready'        an update is downloaded and waiting
//   'downloading'  an update was found and is still downloading (it will be offered when it is ready)
//   'failed'       the check or the download did not succeed
window.pwaCheckForUpdate = async () => {
    let reg;
    try { reg = await navigator.serviceWorker.getRegistration(); } catch (e) { return 'failed'; }
    if (!reg) return 'none';
    if (reg.waiting) return 'ready';
    lastUpdateCheck = Date.now();
    try { await reg.update(); } catch (e) { return 'failed'; }
    if (reg.waiting) return 'ready';
    const sw = reg.installing;
    if (!sw) return 'none';
    // A new version exists and is downloading. Wait for it to finish, but not forever.
    return await new Promise(resolve => {
        const timer = setTimeout(() => resolve('downloading'), 60000);
        const done = (result) => { clearTimeout(timer); resolve(result); };
        sw.addEventListener('statechange', () => {
            if (sw.state === 'installed') done(navigator.serviceWorker.controller ? 'ready' : 'none');
            else if (sw.state === 'activating' || sw.state === 'activated') done('none');
            else if (sw.state === 'redundant') done('failed');
        });
    });
};

// Applies a waiting update (the page reloads once it has taken over). Returns false when none is waiting.
window.pwaApplyUpdate = applyWaitingUpdate;
