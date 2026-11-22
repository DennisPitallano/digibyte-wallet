// Boot-failure recovery. If the wallet never finishes starting (splash stuck, or the
// Blazor error banner appears before the app is up), repair the app-file cache once
// automatically, then show the user clear self-help steps.
//
// SAFE FOR WALLET DATA: this only unregisters service workers and deletes Cache Storage
// (downloaded app files). It never touches localStorage / IndexedDB, where the seed,
// PIN and wallet data live.
(function () {
    var TIMEOUT_MS = 25000;
    var FLAG = 'dgb-boot-repair-at';
    var booted = false;
    var shown = false;

    function appReady() {
        var app = document.getElementById('app');
        return !!app && !document.getElementById('splash');
    }

    async function repair() {
        try {
            if ('serviceWorker' in navigator) {
                var regs = await navigator.serviceWorker.getRegistrations();
                await Promise.all(regs.map(function (r) { return r.unregister(); }));
            }
        } catch (e) { /* ignore */ }
        try {
            if (window.caches) {
                var keys = await caches.keys();
                await Promise.all(keys.map(function (k) { return caches.delete(k); }));
            }
        } catch (e) { /* ignore */ }
    }

    function recentlyRepaired() {
        try {
            var t = parseInt(sessionStorage.getItem(FLAG) || '0', 10);
            return t && Date.now() - t < 10 * 60 * 1000;
        } catch (e) { return false; }
    }

    async function repairAndReload() {
        try { sessionStorage.setItem(FLAG, String(Date.now())); } catch (e) { /* ignore */ }
        await repair();
        location.reload();
    }

    // Baku is left out when the user turned the mascot off (Settings; same key the app reads).
    function showBaku() {
        try { return localStorage.getItem('dgb-baku-mode') !== 'off'; } catch (e) { return true; }
    }

    function showPanel() {
        if (shown || booted) return;
        shown = true;
        var el = document.createElement('div');
        el.id = 'boot-recovery';
        el.setAttribute('role', 'alert');
        el.innerHTML =
            '<div class="br-card">' +
            '<div class="br-head">' +
            (showBaku() ? '<img class="br-baku" id="br-baku" src="images/mascot/poses/support.webp" alt="" width="112" height="112">' : '') +
            '<div class="br-bubble">' +
            '<h2>The wallet could not start</h2>' +
            '<p>Your wallet and funds are safe. Nothing is stored in the app files that failed to load.</p>' +
            '</div>' +
            '</div>' +
            '<div class="br-actions">' +
            '<button type="button" id="br-reload">Reload</button>' +
            '<button type="button" id="br-repair">Repair app files</button>' +
            '</div>' +
            '<p class="br-title">If it keeps happening</p>' +
            '<ol>' +
            '<li>Check your internet connection, turn off VPN or data saver, then tap Reload.</li>' +
            '<li>Tap <b>Repair app files</b>. This clears only downloaded app files and keeps your wallet.</li>' +
            '<li>Close other browser tabs and apps to free memory, then reopen dgbwallet.app.</li>' +
            '<li>Try another browser (Chrome, Firefox, Safari) or the installed app.</li>' +
            '<li>Update your browser to the latest version.</li>' +
            '</ol>' +
            '<p class="br-warn"><b>Do not</b> use "Clear site data" or "Clear browsing data" with cookies and site data ticked: that erases the wallet stored in this browser. ' +
            'Make sure you have your 24-word recovery phrase before changing any browser settings.</p>' +
            '</div>';
        document.body.appendChild(el);
        // The picture may be one of the files that failed to load: without it the text stands alone.
        var baku = document.getElementById('br-baku');
        if (baku) baku.addEventListener('error', function () { baku.remove(); });
        document.getElementById('br-reload').addEventListener('click', function () { location.reload(); });
        document.getElementById('br-repair').addEventListener('click', repairAndReload);
    }

    function fail() {
        if (booted || appReady()) return;
        if (!recentlyRepaired()) {
            repairAndReload();
        } else {
            showPanel();
        }
    }

    // Error banner shown before the app finished starting.
    function watchErrorUi() {
        var ui = document.getElementById('blazor-error-ui');
        if (!ui || !window.MutationObserver) return;
        new MutationObserver(function () {
            if (!booted && !appReady() && getComputedStyle(ui).display !== 'none') fail();
        }).observe(ui, { attributes: true, attributeFilter: ['style', 'class'] });
    }

    // Splash replaced by the app => booted.
    function watchBoot() {
        var app = document.getElementById('app');
        if (!app || !window.MutationObserver) return;
        var obs = new MutationObserver(function () {
            if (appReady()) {
                booted = true;
                obs.disconnect();
                var p = document.getElementById('boot-recovery');
                if (p) p.remove();
                try { sessionStorage.removeItem(FLAG); } catch (e) { /* ignore */ }
            }
        });
        obs.observe(app, { childList: true });
    }

    watchBoot();
    watchErrorUi();
    setTimeout(fail, TIMEOUT_MS);
})();
