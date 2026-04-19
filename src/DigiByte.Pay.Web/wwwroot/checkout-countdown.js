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
    // Cross-frame pill updater for the embed demo's full-simulation section.
    // When Blazor's InteractiveServer circuit doesn't hydrate reliably inside
    // an iframe, SignalR pushes never reach our Checkout.razor. The demo's
    // advance button posts the new status here and we update the pill DOM
    // directly — pure additive, the production SignalR path is untouched.
    window.addEventListener('message', function (e) {
        if (!e.data || e.data.type !== 'digipay-demo-status') return;
        var status = String(e.data.status || '').toLowerCase();
        // Pill: the one element rendered by CheckoutStatusPill.razor. Match by the
        // combination of inline-flex + rounded-full (stable even if colours change).
        var pill = document.querySelector('.inline-flex.rounded-full, .inline-flex[class*="rounded-full"]');
        if (!pill) return;
        var label = {
            seen: 'Detected', paid: 'Paid', confirmed: 'Confirmed',
            expired: 'Expired', underpaid: 'Underpaid', pending: 'Waiting',
        }[status] || status;
        // Replace the bg / colour / dot classes wholesale with the matching set
        // from CheckoutStatusPill.razor, keeping the wrapper's layout classes.
        var pillClasses = {
            seen:      ['bg-amber-100','dark:bg-amber-900/20','text-amber-700','dark:text-amber-300'],
            paid:      ['bg-emerald-100','dark:bg-emerald-900/20','text-emerald-700','dark:text-emerald-300'],
            confirmed: ['bg-emerald-100','dark:bg-emerald-900/20','text-emerald-700','dark:text-emerald-300'],
            underpaid: ['bg-red-100','dark:bg-red-900/20','text-red-700','dark:text-red-300'],
            expired:   ['bg-slate-100','dark:bg-dgb-800','text-slate-600','dark:text-slate-300'],
            pending:   ['bg-slate-100','dark:bg-dgb-800','text-slate-600','dark:text-slate-300'],
        }[status] || [];
        var dotClasses = {
            seen:'bg-amber-500', paid:'bg-emerald-500', confirmed:'bg-emerald-500',
            underpaid:'bg-red-500', expired:'bg-slate-400', pending:'bg-slate-400',
        }[status] || 'bg-slate-400';
        // Strip old status colour classes, add new. Leave layout classes alone.
        Array.from(pill.classList).forEach(function (c) {
            if (/^(bg|text)-|^dark:(bg|text)-/.test(c)) pill.classList.remove(c);
        });
        pillClasses.forEach(function (c) { pill.classList.add(c); });
        // Dot is the first child <span>, text is the remaining text node.
        var dot = pill.querySelector('span');
        if (dot) {
            Array.from(dot.classList).forEach(function (c) {
                if (/^bg-|^dark:bg-/.test(c)) dot.classList.remove(c);
            });
            dot.classList.add(dotClasses);
            // Pulse for "detected" / "paid" transitional states only.
            dot.classList.toggle('animate-dp-pulse', status === 'seen' || status === 'paid');
        }
        // Replace the label text. The pill markup is "<dot span>{label}" with
        // optional whitespace around it — find the last text node with content
        // and update just that one so we don't double-stamp into multiple
        // whitespace nodes.
        var labelNode = null;
        Array.from(pill.childNodes).forEach(function (n) {
            if (n.nodeType === 3 && n.textContent.trim().length > 0) labelNode = n;
        });
        if (labelNode) {
            labelNode.textContent = ' ' + label;
        } else {
            // No text node yet (e.g. first render) — append one after the dot.
            pill.appendChild(document.createTextNode(' ' + label));
        }
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
