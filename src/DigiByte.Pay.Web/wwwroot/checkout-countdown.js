/*!
 * DigiPay checkout countdown — pure client-side, no Blazor circuit needed.
 *
 * Any element carrying `data-digipay-expires="<ISO-8601 UTC>"` has its
 * textContent rewritten every second to "MM:SS" (or "expired" when
 * the window closes). Written as a self-installing polling loop rather
 * than a MutationObserver so it keeps working after InteractiveServer
 * re-renders without needing explicit re-wiring from C#.
 *
 * The checkout runs inside an iframe when mounted via the embed widget —
 * that context doesn't always hydrate Blazor's server circuit, which
 * broke the previous server-timer approach.
 */
(function () {
    'use strict';

    function update() {
        var now = Date.now();
        document.querySelectorAll('[data-digipay-expires]').forEach(function (el) {
            var iso = el.getAttribute('data-digipay-expires');
            if (!iso) return;
            var expMs = Date.parse(iso);
            if (isNaN(expMs)) return;
            var remSec = Math.max(0, Math.floor((expMs - now) / 1000));
            if (remSec <= 0) {
                el.textContent = 'expired';
                return;
            }
            var m = Math.floor(remSec / 60);
            var s = remSec % 60;
            el.textContent = m + ':' + (s < 10 ? '0' + s : s);
        });
    }

    // First tick synchronous so the user never sees a placeholder.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', update);
    } else {
        update();
    }
    setInterval(update, 1000);

    // When the checkout runs inside the embed widget's iframe, post our
    // content height to the parent so it can tighten the modal — otherwise
    // the fixed 720px container leaves big dead zones above and below the
    // checkout card. No-op when loaded standalone (same-window reference).
    if (window.parent !== window) {
        function postSize() {
            try {
                // Measure the card itself (data-digipay-measure) rather than body —
                // the checkout page uses min-h-dvh on <main> which would clamp the
                // body height to the iframe's current viewport, defeating the purpose.
                var card = document.querySelector('[data-digipay-measure]');
                if (!card) return;
                // +32 for the outer page padding (p-4) so the card isn't flush against
                // the iframe edge. Use offsetHeight — includes border, excludes margin.
                var h = card.offsetHeight + 32;
                window.parent.postMessage({ type: 'digipay-checkout-size', height: h }, '*');
            } catch (_) { /* cross-origin can fail silently; modal falls back to default height */ }
        }
        // A few pokes after load: the QR image streams in, fonts settle, etc.
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', postSize);
        } else {
            postSize();
        }
        window.addEventListener('load', postSize);
        window.addEventListener('resize', postSize);
        // Catch late layout shifts (e.g. Blazor hydration, QR image decode). Observe
        // the card specifically so body's min-h-dvh doesn't mask real changes.
        try {
            var ro = new ResizeObserver(postSize);
            var observeCard = function () {
                var card = document.querySelector('[data-digipay-measure]');
                if (card) ro.observe(card);
            };
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', observeCard);
            } else {
                observeCard();
            }
        } catch (_) { /* older browsers: fall back to the setTimeouts below */ }
        setTimeout(postSize, 300);
        setTimeout(postSize, 1000);
    }
})();
