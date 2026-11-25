/*!
 * DigiPay theme: light / dark / system, the same three choices as the wallet.
 * Runs synchronously in <head> and sets the `dark` class on <html> before first paint,
 * so the page never flashes the wrong theme. The shared style layer
 * (_content/DigiByte.UI/css/ui.css) keys every themed value off `.dark`.
 *
 * Resolution, highest first (specs/032-digipay-ui-refresh/data-model.md §1–§2):
 *   1. On /pay/* pages, ?theme=light|dark set by the shop. This page view only, never saved.
 *   2. On /pos, the POS key `digipay.pos.theme:v1` (missing = dark). Set by the merchant
 *      behind the manager PIN.
 *   3. The saved choice `dp-theme` (light | dark | system; missing or invalid = system).
 *   4. The device setting (prefers-color-scheme) when the choice is `system`.
 */
(function (global) {
    'use strict';

    var KEY = 'dp-theme';
    var POS_KEY = 'digipay.pos.theme:v1';
    var COLORS = { dark: '#002352', light: '#f8fafc' };
    var MODES = { light: 1, dark: 1, system: 1 };

    function read(key) {
        try { return global.localStorage.getItem(key); } catch (_) { return null; }
    }

    function write(key, value) {
        try { global.localStorage.setItem(key, value); } catch (_) { }
    }

    function path() {
        return (global.location && global.location.pathname || '/').toLowerCase();
    }

    function isPayPage(p) { return p === '/pay' || p.indexOf('/pay/') === 0; }
    function isPosPage(p) { return p === '/pos' || p.indexOf('/pos/') === 0; }

    // The shop's ?theme= on /pay/* pages, or null when absent or invalid.
    function shopOverride() {
        if (!isPayPage(path())) return null;
        var search = (global.location && global.location.search) || '';
        var m = /[?&]theme=([^&#]*)/i.exec(search);
        if (!m) return null;
        var v = decodeURIComponent(m[1] || '').toLowerCase();
        return v === 'light' || v === 'dark' ? v : null;
    }

    function systemDark() {
        return !!(global.matchMedia && global.matchMedia('(prefers-color-scheme: dark)').matches);
    }

    // The saved choice for the current page: light | dark | system.
    function getMode() {
        if (isPosPage(path())) {
            var pos = read(POS_KEY);
            return MODES[pos] ? pos : 'dark';
        }
        var saved = read(KEY);
        return MODES[saved] ? saved : 'system';
    }

    // The theme to show right now: light | dark.
    function resolve() {
        var shop = shopOverride();
        if (shop) return shop;
        var mode = getMode();
        if (mode === 'system') return systemDark() ? 'dark' : 'light';
        return mode;
    }

    function apply(theme) {
        theme = theme === 'dark' || theme === 'light' ? theme : resolve();
        var root = global.document.documentElement;
        if (root.classList.contains('dark') !== (theme === 'dark')) {
            root.classList.toggle('dark', theme === 'dark');
        }
        root.style.colorScheme = theme;
        var meta = global.document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', COLORS[theme]);
        return theme;
    }

    // Saves the choice for this page type (website/dashboard/checkout, or the POS) and applies it.
    function setMode(mode) {
        if (!MODES[mode]) mode = 'system';
        write(isPosPage(path()) ? POS_KEY : KEY, mode);
        return apply();
    }

    apply();

    // Blazor's enhanced navigation can replace <html> attributes with the server's copy,
    // which has no theme class. Put it back whenever it drifts.
    try {
        var observer = new global.MutationObserver(function () {
            var want = resolve() === 'dark';
            if (global.document.documentElement.classList.contains('dark') !== want) apply();
        });
        observer.observe(global.document.documentElement, { attributes: true, attributeFilter: ['class'] });
    } catch (_) { }

    ['enhancedload', 'blazor-enhanced-load'].forEach(function (n) {
        global.document.addEventListener(n, function () { apply(); });
    });

    // Follow the device live while the choice is `system` (and no shop override applies).
    if (global.matchMedia) {
        var q = global.matchMedia('(prefers-color-scheme: dark)');
        var onChange = function () { if (!shopOverride() && getMode() === 'system') apply(); };
        if (q.addEventListener) q.addEventListener('change', onChange);
        else if (q.addListener) q.addListener(onChange);
    }

    // Flip between light and dark (used by older two-state toggles).
    function toggle() { return setMode(resolve() === 'dark' ? 'light' : 'dark'); }

    global.DigiPayTheme = { getMode: getMode, setMode: setMode, resolve: resolve, apply: apply, toggle: toggle };
})(window);
