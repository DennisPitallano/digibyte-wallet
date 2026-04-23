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
