// DOM helper functions called from Blazor via JS interop
// Replaces inline eval() calls that violate CSP
window.domHelpers = {
    focusElement: function (id) {
        document.getElementById(id)?.focus();
    },
    selectElement: function (id) {
        document.getElementById(id)?.select();
    },
    autoSizeTxList: function () {
        var main = document.querySelector('main');
        if (main) main.style.overflow = 'hidden';

        var el = document.getElementById('tx-list');
        if (!el) return;
        var rect = el.getBoundingClientRect();
        var navHeight = 64;
        var padding = 16;
        var available = window.innerHeight - rect.top - navHeight - padding;
        if (available > 100) el.style.maxHeight = available + 'px';
    },
    restoreScroll: function () {
        var m = document.querySelector('main');
        if (m) m.style.overflow = '';
    },
    getScrollPage: function (el) {
        if (!el || !el.scrollWidth) return 0;
        var halfWidth = el.scrollWidth / 2;
        return el.scrollLeft >= halfWidth * 0.5 ? 1 : 0;
    },
    // Keep Tab / Shift+Tab inside a modal dialog (accessibility: focus trap). Returns nothing; call releaseFocus on close.
    trapFocus: function (id) {
        var root = document.getElementById(id);
        if (!root) return;
        var selector = 'a[href], button:not([disabled]), textarea, input, select, [tabindex]:not([tabindex="-1"])';
        var handler = function (e) {
            if (e.key !== 'Tab') return;
            var items = Array.prototype.slice.call(root.querySelectorAll(selector)).filter(function (el) { return el.offsetParent !== null; });
            if (items.length === 0) { e.preventDefault(); return; }
            var first = items[0], last = items[items.length - 1];
            if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
            else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
            else if (!root.contains(document.activeElement)) { e.preventDefault(); first.focus(); }
        };
        root.__trap = handler;
        document.addEventListener('keydown', handler, true);
        var autofocus = root.querySelector('[data-autofocus]');
        if (autofocus) autofocus.focus();
    },
    releaseFocus: function (id) {
        var root = document.getElementById(id);
        if (root && root.__trap) {
            document.removeEventListener('keydown', root.__trap, true);
            root.__trap = null;
        }
    },
    // True when the tab is backgrounded (minimized, switched away, screen off).
    // Used by Home page poller to pause network refreshes while hidden.
    isHidden: function () {
        return typeof document !== 'undefined' && document.hidden === true;
    },
    // Returns true if history.back() would stay within this app.
    // Used by pages like /help that can be deep-linked: if the user arrived
    // directly (no same-origin referrer and short history), fall back to the home page.
    canGoBackInApp: function () {
        try {
            if (window.history.length <= 1) return false;
            var ref = document.referrer || '';
            if (!ref) return false;
            var refOrigin = new URL(ref).origin;
            return refOrigin === window.location.origin;
        } catch (e) {
            return false;
        }
    }
};
