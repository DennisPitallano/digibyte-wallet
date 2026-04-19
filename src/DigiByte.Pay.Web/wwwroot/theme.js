/*!
 * DigiPay theme toggle: light / dark with OS default.
 * Sets <html data-theme="..."> before first paint so there's no FOUC.
 */
(function (global) {
    'use strict';

    var KEY = 'dp-theme';

    function resolve() {
        var stored = null;
        try { stored = localStorage.getItem(KEY); } catch (_) { }
        if (stored === 'light' || stored === 'dark') return stored;
        return (global.matchMedia && global.matchMedia('(prefers-color-scheme: dark)').matches) ? 'dark' : 'light';
    }

    function apply(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        document.querySelectorAll('[data-dp-theme-toggle]').forEach(function (btn) {
            btn.setAttribute('aria-pressed', theme === 'dark' ? 'true' : 'false');
            var label = btn.querySelector('[data-dp-theme-label]');
            if (label) label.textContent = theme === 'dark' ? '☀' : '☾';
        });
    }

    function toggle() {
        var cur = document.documentElement.getAttribute('data-theme') || resolve();
        var next = cur === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem(KEY, next); } catch (_) { }
        apply(next);
    }

    // Run synchronously at script load time so the initial render matches.
    apply(resolve());

    // Keep following OS changes when the user hasn't opted out.
    if (global.matchMedia) {
        global.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
            var stored = null;
            try { stored = localStorage.getItem(KEY); } catch (_) { }
            if (!stored) apply(resolve());
        });
    }

    global.DigiPayTheme = { toggle: toggle, apply: apply, resolve: resolve };
})(window);
