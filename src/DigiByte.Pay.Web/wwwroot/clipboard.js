/*!
 * DigiPay clipboard helper for <CopyButton> (spec 032, FR-008).
 * copy(text) resolves true when the text reached the clipboard. If the browser refuses
 * (no permission, insecure context, an iframe without clipboard-write), select(id) highlights
 * the value so the person can copy it by hand.
 */
(function (global) {
    'use strict';

    async function copy(text) {
        try {
            if (global.navigator && global.navigator.clipboard && global.isSecureContext !== false) {
                await global.navigator.clipboard.writeText(text);
                return true;
            }
        } catch (_) { }
        // Older browsers and some embedded windows: the execCommand fallback.
        try {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'fixed';
            ta.style.top = '-1000px';
            document.body.appendChild(ta);
            ta.select();
            var ok = document.execCommand && document.execCommand('copy');
            document.body.removeChild(ta);
            return !!ok;
        } catch (_) {
            return false;
        }
    }

    function select(id) {
        var el = id && document.getElementById(id);
        if (!el) return false;
        try {
            var range = document.createRange();
            range.selectNodeContents(el);
            var sel = global.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            return true;
        } catch (_) {
            return false;
        }
    }

    // Copies the text of an element (code samples), as the reader sees it.
    function copyFrom(id) {
        var el = id && document.getElementById(id);
        if (!el) return Promise.resolve(false);
        return copy(el.innerText || el.textContent || '');
    }

    global.digipayClipboard = { copy: copy, select: select, copyFrom: copyFrom };
})(window);
