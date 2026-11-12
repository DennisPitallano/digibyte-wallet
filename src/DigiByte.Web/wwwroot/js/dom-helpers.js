// DOM helper functions called from Blazor via JS interop
// Replaces inline eval() calls that violate CSP
window.domHelpers = {
    focusElement: function (id) {
        document.getElementById(id)?.focus();
    },
    selectElement: function (id) {
        document.getElementById(id)?.select();
    },
    // Bring an element into view, for links that point at a section of the same page (for example /identity#recover-funds).
    scrollToElement: function (id) {
        document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
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
    // True when the page's scroll area is at the very top (pull-to-refresh only arms there).
    isScrolledToTop: function () {
        var main = document.querySelector('main');
        return !main || main.scrollTop <= 0;
    },
    // Short vibration where the device supports it (Android browsers; a no-op elsewhere).
    haptic: function (pattern) {
        try { if (navigator.vibrate) navigator.vibrate(pattern || 10); } catch (e) { }
    },
    // PIN boxes: move to the next box in the same tick as the keystroke (a round trip through .NET is too slow for
    // fast typing), and spread a pasted or auto-filled code across the boxes.
    initPinGroup: function (id) {
        var root = document.getElementById(id);
        if (!root || root.__pin) return;
        root.__pin = true;
        var boxes = function () { return Array.prototype.slice.call(root.querySelectorAll('input')); };
        var fill = function (start, text) {
            var list = boxes();
            var digits = (text || '').replace(/\D/g, '').split('');
            var i = start;
            digits.forEach(function (d) {
                if (i >= list.length) return;
                list[i].value = d;
                list[i].dispatchEvent(new Event('input', { bubbles: true }));
                i++;
            });
            var last = list[Math.min(i, list.length - 1)];
            if (last) last.focus();
        };
        root.addEventListener('paste', function (e) {
            var text = (e.clipboardData || window.clipboardData).getData('text');
            if (!text) return;
            e.preventDefault();
            var list = boxes();
            var at = list.indexOf(e.target);
            // A full code replaces everything; a partial one continues from the focused box.
            fill(text.replace(/\D/g, '').length >= list.length || at < 0 ? 0 : at, text);
        });
        // Several characters arriving at once (password managers, autofill, some keyboards): each box holds one digit,
        // so spread them out instead of letting the box keep only the first.
        root.addEventListener('beforeinput', function (e) {
            if (!e.data || e.data.length < 2) return;
            e.preventDefault();
            var at = boxes().indexOf(e.target);
            fill(at < 0 ? 0 : at, e.data);
        });
        root.addEventListener('input', function (e) {
            var list = boxes();
            var at = list.indexOf(e.target);
            if (at < 0 || !e.isTrusted) return;
            if (/^\d$/.test(e.target.value) && at < list.length - 1) list[at + 1].focus();
        });
    },
    // True when the user asked the OS for less motion; count-up animations are skipped.
    prefersReducedMotion: function () {
        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
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
