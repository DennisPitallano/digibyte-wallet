/*!
 * DigiPay dialog focus helper for <ConfirmDialog> and sheets (spec 032, FR-030).
 * open(id, dotnetRef): remembers the element that had focus, moves focus into the dialog,
 * keeps Tab and Shift+Tab inside it, and calls dotnetRef.OnEscape() when Escape is pressed.
 * close(id): removes the handlers and returns focus to where it was.
 */
(function (global) {
    'use strict';

    var open = {};
    var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function focusables(el) {
        return Array.prototype.filter.call(el.querySelectorAll(FOCUSABLE), function (n) {
            return n.offsetParent !== null || n === document.activeElement;
        });
    }

    function openDialog(id, dotnetRef) {
        var el = document.getElementById(id);
        if (!el || open[id]) return;
        var state = { opener: document.activeElement, ref: dotnetRef };
        state.onKey = function (e) {
            if (e.key === 'Escape') {
                e.preventDefault();
                if (state.ref) state.ref.invokeMethodAsync('OnEscape');
                return;
            }
            if (e.key !== 'Tab') return;
            var items = focusables(el);
            if (!items.length) { e.preventDefault(); return; }
            var first = items[0], last = items[items.length - 1];
            if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
            else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
        };
        document.addEventListener('keydown', state.onKey, true);
        open[id] = state;
        var initial = el.querySelector('[data-autofocus]') || focusables(el)[0] || el;
        setTimeout(function () { try { initial.focus(); } catch (_) { } }, 0);
    }

    function closeDialog(id) {
        var state = open[id];
        if (!state) return;
        document.removeEventListener('keydown', state.onKey, true);
        delete open[id];
        try { if (state.opener && state.opener.focus) state.opener.focus(); } catch (_) { }
    }

    global.digipayDialog = { open: openDialog, close: closeDialog };
})(window);

/*!
 * Skip link (FR-029): every page renders its own <main>, so the shared "Skip to main content"
 * link focuses whichever <main> is on the page instead of relying on a fixed id.
 */
(function () {
    'use strict';
    document.addEventListener('click', function (e) {
        var link = e.target && e.target.closest && e.target.closest('[data-skip-to-main]');
        if (!link) return;
        var main = document.querySelector('main');
        if (!main) return;
        e.preventDefault();
        if (!main.hasAttribute('tabindex')) main.setAttribute('tabindex', '-1');
        main.focus();
        if (main.scrollIntoView) main.scrollIntoView({ block: 'start' });
    });

    // Dashboard store picker (DashboardShell): focus the active store when the menu opens, and move with the
    // arrow keys, Home and End between the menu's items.
    window.digipayStoreMenu = {
        focusActive(menuId) {
            const menu = document.getElementById(menuId);
            if (!menu) return;
            const item = menu.querySelector('[aria-checked="true"]') || menu.querySelector('[role^="menuitem"]');
            if (item) item.focus();
        },
        move(menuId, key) {
            const menu = document.getElementById(menuId);
            if (!menu) return;
            const items = Array.from(menu.querySelectorAll('[role^="menuitem"]'));
            if (!items.length) return;
            const i = items.indexOf(document.activeElement);
            const next = key === 'Home' ? 0 : key === 'End' ? items.length - 1
                : key === 'ArrowDown' ? (i + 1) % items.length : (i - 1 + items.length) % items.length;
            items[next].focus();
        },
    };
})();

/*!
 * Entrance animations after the first page (App.razor, tailwind-input.css). A page that arrived as full HTML
 * carries data-dp-prerendered on <html>, which turns its entrance animations off: the content is shown as it
 * is at the first paint, and not animated again when the live connection redraws it. The first click on a
 * link (or Back/Forward) clears the attribute so the next page animates in as before. What is on screen at
 * that moment is pinned to "no animation" first, so clearing the attribute doesn't replay it.
 */
(function () {
    'use strict';
    var root = document.documentElement;
    if (!root.hasAttribute('data-dp-prerendered')) return;
    var ANIMATED = '.page-enter, .fade-in, .fade-in-delay-1, .fade-in-delay-2, .fade-in-delay-3, .fade-in-delay-4, '
        + '.slide-up, .slide-up-delay-1, .slide-up-delay-2, .slide-up-delay-3, .slide-up-delay-4, .slide-up-delay-5, '
        + '.scale-in, .scale-in-delay-1, .scale-in-delay-2';

    function release() {
        document.removeEventListener('click', onClick, true);
        window.removeEventListener('popstate', release);
        var shown = document.querySelectorAll(ANIMATED);
        for (var i = 0; i < shown.length; i++) shown[i].style.animation = 'none';
        root.removeAttribute('data-dp-prerendered');
    }

    function onClick(e) {
        var link = e.target && e.target.closest && e.target.closest('a[href]');
        if (link && !link.hasAttribute('data-skip-to-main')) release();
    }

    document.addEventListener('click', onClick, true);
    window.addEventListener('popstate', release);
})();
