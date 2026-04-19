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
})();
