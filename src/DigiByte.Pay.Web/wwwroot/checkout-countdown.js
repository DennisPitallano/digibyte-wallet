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
                // A whole sentence reads better than "Price expires in expired": when the
                // countdown sits in a [data-digipay-countdown] line, replace the whole line.
                var line = el.closest && el.closest('[data-digipay-countdown]');
                if (line) line.textContent = line.getAttribute('data-digipay-countdown') || 'This price has expired.';
                else el.textContent = 'expired';
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

    // Inside the embed widget's iframe (DigiPay.checkout on a shop's page)?
    var inFrame = !!window.parent && window.parent !== window;

    function postToParent(msg) {
        try { window.parent.postMessage(msg, '*'); } catch (_) { /* parent gone: nothing to tell */ }
    }

    // The checkout card says which session it shows and its status (Checkout.razor).
    function checkoutCard() {
        return document.querySelector('[data-digipay-status]');
    }

    // ---------------------------------------------------------------------
    // Status to the embedding page.
    //
    // In the embed iframe, every status change is posted to the parent as
    //   { type: 'digipay-checkout-status', sessionId, status, returnUrl }
    // so the shop's page learns the outcome (digipay.js closes the modal on
    // "confirmed" and calls the merchant's onStatus / onSuccess callbacks).
    // ---------------------------------------------------------------------
    var lastPostedStatus = null;
    function tickStatus() {
        if (!inFrame) return;
        var card = checkoutCard();
        if (!card) return;
        var status = card.getAttribute('data-digipay-status');
        if (!status || status === lastPostedStatus) return;
        lastPostedStatus = status;
        postToParent({
            type: 'digipay-checkout-status',
            sessionId: card.getAttribute('data-digipay-session') || null,
            status: status,
            returnUrl: card.getAttribute('data-digipay-return-url') || null,
        });
    }
    setInterval(tickStatus, 500);

    // Going back to the shop. Standalone, the page navigates itself. In the embed iframe it must
    // not (the shop would load inside the modal): it asks the parent to go there instead with
    //   { type: 'digipay-checkout-return', sessionId, status, url }
    function returnToShop(url) {
        if (inFrame) {
            var card = checkoutCard();
            postToParent({
                type: 'digipay-checkout-return',
                sessionId: card ? card.getAttribute('data-digipay-session') : null,
                status: card ? card.getAttribute('data-digipay-status') : null,
                url: url,
            });
            return;
        }
        // location.assign so the browser back button still works on the
        // merchant's page (replace would lose the checkout page from history).
        try { window.location.assign(url); }
        catch (_) { window.location.href = url; }
    }

    // "Back to the shop" links (TerminalState, Receipt) carry data-digipay-return-link; in the
    // embed iframe a click becomes a request to the parent rather than an in-frame navigation.
    document.addEventListener('click', function (e) {
        if (!inFrame) return;
        var link = e.target && e.target.closest && e.target.closest('a[data-digipay-return-link]');
        if (!link) return;
        var url = link.getAttribute('href');
        if (!url) return;
        e.preventDefault();
        returnToShop(url);
    });

    // Escape inside the iframe never reaches the parent's key handler, so pass it on.
    document.addEventListener('keydown', function (e) {
        if (inFrame && e.key === 'Escape') postToParent({ type: 'digipay-checkout-escape' });
    });

    // ---------------------------------------------------------------------
    // Return-to-merchant countdown.
    //
    // Same idiom as the expiry countdown above: poll the DOM for a
    // self-describing element rather than rely on an inline <script>. An
    // inline script wouldn't fire when Blazor's InteractiveServer circuit
    // patches a "Payment confirmed" panel into the page — browsers don't
    // re-execute scripts injected via DOM diff. Polling sidesteps that.
    //
    // Element shape (rendered by TerminalState.razor when status=confirmed
    // and the session has a returnUrl):
    //   <div data-digipay-return-merchant
    //        data-digipay-return-url="https://shop.example/orders/42/thanks"
    //        data-digipay-return-seconds="8">
    //     ...
    //     <span data-digipay-return-countdown>8</span>
    //   </div>
    //
    // We arm the timer the first time we see an un-armed element, then leave
    // it alone — re-renders don't reset the countdown. While the shopper is
    // pointing at or focused on an element marked data-digipay-return-pause
    // (the "View receipt" link), the countdown holds so they can reach it.
    // ---------------------------------------------------------------------
    function returnPaused() {
        var active = document.activeElement;
        var pausers = document.querySelectorAll('[data-digipay-return-pause]');
        for (var i = 0; i < pausers.length; i++) {
            var el = pausers[i];
            if (el === active || (active && el.contains && el.contains(active))) return true;
            try { if (el.matches && el.matches(':hover')) return true; } catch (_) { }
        }
        return false;
    }

    function tickReturnMerchant() {
        var hosts = document.querySelectorAll('[data-digipay-return-merchant]:not([data-digipay-return-armed])');
        hosts.forEach(function (host) {
            var url = host.getAttribute('data-digipay-return-url');
            var seconds = parseInt(host.getAttribute('data-digipay-return-seconds') || '8', 10);
            if (!url || !(seconds > 0)) return;
            host.setAttribute('data-digipay-return-armed', '1');

            var counter = host.querySelector('[data-digipay-return-countdown]');
            var remaining = seconds;
            if (counter) counter.textContent = String(remaining);

            var t = setInterval(function () {
                if (host.isConnected === false) { clearInterval(t); return; }
                if (returnPaused()) {
                    host.setAttribute('data-digipay-return-paused', '1');
                    return;
                }
                host.removeAttribute('data-digipay-return-paused');
                remaining -= 1;
                if (counter) counter.textContent = String(Math.max(0, remaining));
                if (remaining <= 0) {
                    clearInterval(t);
                    returnToShop(url);
                }
            }, 1000);
        });
    }
    setInterval(tickReturnMerchant, 500);
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', tickReturnMerchant);
        document.addEventListener('DOMContentLoaded', tickStatus);
    } else {
        tickReturnMerchant();
        tickStatus();
    }

    // When the checkout runs inside the embed widget's iframe, post our
    // content height to the parent so it can tighten the modal — otherwise
    // the fixed 720px container leaves big dead zones above and below the
    // checkout card. No-op when loaded standalone (same-window reference).
    // Cross-frame pill updater for the embed demo's full-simulation section.
    // When Blazor's InteractiveServer circuit doesn't hydrate reliably inside
    // an iframe, SignalR pushes never reach our Checkout.razor. The demo's
    // advance button posts the new status here and we update the pill DOM
    // directly — pure additive, the production SignalR path is untouched.
    //
    // Announce readiness so the parent knows when it can reliably postMessage
    // us — covers the race where Advance is clicked before the iframe's JS
    // listener is installed. The parent can queue its last state update and
    // replay it on receipt of this ping.
    if (window.parent !== window) {
        try { window.parent.postMessage({ type: 'digipay-checkout-ready' }, '*'); } catch (_) { }
    }
    window.addEventListener('message', function (e) {
        if (!e.data || e.data.type !== 'digipay-demo-status') return;
        var status = String(e.data.status || '').toLowerCase();
        // The status badge rendered by StatusBadge.razor (spec 032). Swap its tone class and
        // text; the icon is hidden because it can't be swapped without the server.
        var badge = document.querySelector('.ui-checkout-header .ui-badge, .ui-badge');
        if (!badge) return;
        var states = {
            pending:   ['neutral', 'Waiting for payment'],
            seen:      ['info',    'Payment detected'],
            paid:      ['success', 'Payment received'],
            confirmed: ['success', 'Confirmed'],
            underpaid: ['danger',  'Underpaid'],
            expired:   ['neutral', 'Price expired'],
            refunded:  ['refund',  'Refunded'],
            voided:    ['neutral', 'Cancelled by the merchant'],
        };
        var st = states[status];
        if (!st) return;
        Array.from(badge.classList).forEach(function (c) {
            if (/^ui-badge-/.test(c)) badge.classList.remove(c);
        });
        badge.classList.add('ui-badge-' + st[0]);
        var svg = badge.querySelector('svg');
        if (svg) svg.style.display = 'none';
        var text = badge.querySelector('span');
        if (text) text.textContent = st[1];
        badge.setAttribute('data-status', status);
    });

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
