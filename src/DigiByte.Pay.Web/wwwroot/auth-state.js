/*!
 * DigiPay auth-state boot: reads the session token from localStorage and
 * sets <html data-digipay-auth="yes|no"> before first paint, so SSR pages
 * can toggle sign-in vs dashboard links via CSS without needing an
 * interactive render.
 *
 * Mirrors theme.js's pattern — same reason (FOUC-free, works across
 * Blazor enhanced navigation which strips custom <html> attributes on
 * server-side renders).
 */
(function (global) {
    'use strict';

    var KEY = 'digipay-token';

    function resolve() {
        try {
            var tok = localStorage.getItem(KEY);
            return tok && tok.length > 0 ? 'yes' : 'no';
        } catch (_) {
            return 'no';
        }
    }

    function apply(state) {
        document.documentElement.setAttribute('data-digipay-auth', state);
    }

    // Synchronous: runs in <head> before body paint, so the first render
    // already has the right attribute — no "Sign in" → "Dashboard" flash.
    apply(resolve());

    // Cross-tab: signing out in another tab flips this tab without reload.
    global.addEventListener('storage', function (e) {
        if (e.key === KEY) apply(resolve());
    });

    // Blazor enhanced navigation replaces <html> content; its server-rendered
    // output has no data-digipay-auth (server can't read localStorage). Watch
    // for attribute clears and re-apply, same trick as theme.js.
    try {
        var observer = new MutationObserver(function () {
            if (!document.documentElement.hasAttribute('data-digipay-auth')) apply(resolve());
        });
        observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-digipay-auth'] });
    } catch (_) { /* no MutationObserver → lose cross-nav update, non-fatal */ }

    // Expose so dashboard sign-in / sign-out can force-refresh without reload.
    global.DigiPayAuth = { refresh: function () { apply(resolve()); } };
})(window);
