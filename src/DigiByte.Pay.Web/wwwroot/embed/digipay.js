/*!
 * DigiPay embed widget — drop this script on any page to open the DigiPay
 * hosted checkout inline as a modal, or render a ready-styled donation
 * button without ever leaving your site.
 *
 * Usage (declarative):
 *   <script src="https://pay.example.com/embed/digipay.js"></script>
 *   <button data-digipay-checkout data-session-id="ses_abc123">Pay</button>
 *
 * Usage (programmatic):
 *   DigiPay.checkout({ sessionId: 'ses_abc123' });
 *   DigiPay.checkout({ sessionId: 'ses_abc123', theme: 'light' });   // optional: 'light' | 'dark'
 *   DigiPay.checkout({
 *       sessionId: 'ses_abc123',
 *       onStatus: function (e) { console.log(e.sessionId, e.status); },   // every status change
 *       onSuccess: function (e) { location.href = '/thanks'; },          // once, on 'confirmed'
 *   });
 *   DigiPay.close();
 *   const el = DigiPay.button({ address: 'dgb1q…', amount: 5, name: 'Shop', theme: 'dark' });
 *
 * Theme (optional): pass `theme` ('light' or 'dark'), or add data-theme="light|dark" to an
 * auto-bound element, to match the checkout to your shop. Without it the checkout follows the
 * shopper's own choice or device, exactly as before. Any other value is ignored.
 *   document.body.appendChild(el);
 *
 * The widget auto-derives the Pay host from its own <script src>, so you
 * never hard-code the origin in merchant code. Version pin with ?v=0.1 for
 * predictable upgrades.
 */
(function (global) {
    'use strict';

    // Derive Pay.Web origin from our own <script src> — one less thing for merchants to configure.
    var scriptEl = document.currentScript || (function () {
        var scripts = document.getElementsByTagName('script');
        return scripts[scripts.length - 1];
    })();
    var origin = '';
    try { origin = new URL(scriptEl.src).origin; } catch (_) { /* inline script? let caller configure */ }

    var activeModal = null;

    // 'light' | 'dark' | null. Anything else is dropped silently (contract C3).
    function normaliseTheme(value) {
        var v = String(value || '').toLowerCase();
        return v === 'light' || v === 'dark' ? v : null;
    }

    function withTheme(url, theme) {
        var t = normaliseTheme(theme);
        if (!t) return url;
        return url + (url.indexOf('?') === -1 ? '?' : '&') + 'theme=' + t;
    }

    function prefersReducedMotion() {
        try { return !!(global.matchMedia && global.matchMedia('(prefers-reduced-motion: reduce)').matches); }
        catch (_) { return false; }
    }

    function isHttpUrl(url) {
        try {
            var u = new URL(String(url), global.location.href);
            return u.protocol === 'https:' || u.protocol === 'http:';
        } catch (_) { return false; }
    }

    function safeCall(fn, arg) {
        if (typeof fn !== 'function') return;
        try { fn(arg); } catch (err) { if (global.console && console.error) console.error('DigiPay callback failed', err); }
    }

    // opts: { onStatus, onSuccess, closeDelay } (see checkout()).
    function createModal(iframeSrc, theme, opts) {
        if (activeModal) closeModal();
        opts = opts || {};
        var light = normaliseTheme(theme) === 'light';
        var reduce = prefersReducedMotion();
        var opener = document.activeElement;

        var overlay = document.createElement('div');
        overlay.className = 'digipay-modal';
        overlay.style.cssText =
            'position:fixed;inset:0;background:rgba(0,0,0,0.6);z-index:2147483647;' +
            'display:flex;align-items:center;justify-content:center;padding:16px;' +
            (reduce ? '' : 'animation:digipayFadeIn 160ms ease-out;');
        overlay.addEventListener('click', function (e) { if (e.target === overlay) closeModal(); });

        var container = document.createElement('div');
        // Announced as a modal dialog so screen readers know the shop page behind it is inert.
        container.setAttribute('role', 'dialog');
        container.setAttribute('aria-modal', 'true');
        container.setAttribute('aria-label', 'DigiPay checkout');
        // Start tight: a sensible fallback so empty iframes don't collapse to
        // 150px. The real height arrives via postMessage once the checkout
        // page's body scrollHeight stabilises, at which point we shrink/grow.
        container.style.cssText =
            'position:relative;width:100%;max-width:460px;height:620px;max-height:90vh;' +
            'background:' + (light ? '#f8fafc' : '#001529') + ';border-radius:20px;overflow:hidden;' +
            'box-shadow:0 24px 48px rgba(0,0,0,0.4);' +
            'transform-origin:center;' +
            (reduce ? '' : 'animation:digipayScaleIn 200ms cubic-bezier(0.16,1,0.3,1);transition:height 180ms ease-out;');

        // 44px close target (WCAG 2.5.5).
        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.innerHTML = '&times;';
        closeBtn.setAttribute('aria-label', 'Close checkout');
        closeBtn.style.cssText =
            'position:absolute;top:6px;right:8px;width:44px;height:44px;' +
            'border:0;background:' + (light ? 'rgba(15,23,42,0.08)' : 'rgba(255,255,255,0.12)') + ';color:' + (light ? '#0f172a' : '#fff') + ';border-radius:9999px;' +
            'font:bold 24px/1 system-ui,sans-serif;cursor:pointer;z-index:1;' +
            'display:flex;align-items:center;justify-content:center;padding:0;';
        closeBtn.addEventListener('mouseover', function () { this.style.background = light ? 'rgba(15,23,42,0.14)' : 'rgba(255,255,255,0.22)'; });
        closeBtn.addEventListener('mouseout', function () { this.style.background = light ? 'rgba(15,23,42,0.08)' : 'rgba(255,255,255,0.12)'; });
        closeBtn.addEventListener('click', function () { closeModal(); });

        var iframe = document.createElement('iframe');
        iframe.src = iframeSrc;
        iframe.title = 'DigiPay checkout';
        iframe.style.cssText = 'width:100%;height:100%;border:0;display:block;';
        iframe.setAttribute('allow', 'clipboard-write');

        container.appendChild(closeBtn);
        container.appendChild(iframe);
        overlay.appendChild(container);

        if (!document.getElementById('__digipay_styles')) {
            var style = document.createElement('style');
            style.id = '__digipay_styles';
            style.textContent =
                '@keyframes digipayFadeIn{from{opacity:0}to{opacity:1}}' +
                '@keyframes digipayScaleIn{from{opacity:0;transform:scale(.94) translateY(8px)}to{opacity:1;transform:scale(1) translateY(0)}}' +
                '@media (prefers-reduced-motion: reduce){.digipay-modal,.digipay-modal *{animation:none!important;transition:none!important}}';
            document.head.appendChild(style);
        }

        document.body.appendChild(overlay);

        var onKey = function (e) { if (e.key === 'Escape') closeModal(); };
        document.addEventListener('keydown', onKey);

        var state = {
            overlay: overlay, onKey: onKey, onMessage: null, opener: opener,
            closeTimer: null, succeeded: false,
        };

        // Messages from our checkout iframe only (same window, same Pay origin).
        //   digipay-checkout-size   { height }                          → fit the modal to the card
        //   digipay-checkout-status { sessionId, status, returnUrl }    → onStatus; on "confirmed"
        //                                                                 onSuccess, then close
        //   digipay-checkout-return { sessionId, status, url }          → close, go to the shop's URL
        //   digipay-checkout-escape                                     → close (Escape in the frame)
        var onMessage = function (e) {
            if (e.source !== iframe.contentWindow) return;
            if (origin && e.origin && e.origin !== origin) return;
            var d = e.data;
            if (!d || typeof d !== 'object') return;

            if (d.type === 'digipay-checkout-size') {
                // Clamped to viewport so tall content scrolls inside the iframe rather than
                // the modal overflowing the screen.
                var h = Number(d.height);
                if (!isFinite(h) || h <= 0) return;
                var clamped = Math.min(h, Math.floor(global.innerHeight * 0.9));
                container.style.height = clamped + 'px';
            } else if (d.type === 'digipay-checkout-status') {
                var info = { sessionId: d.sessionId || null, status: String(d.status || '') };
                safeCall(opts.onStatus, info);
                if (info.status === 'confirmed' && !state.succeeded) {
                    state.succeeded = true;
                    safeCall(opts.onSuccess, info);
                    var delay = Number(opts.closeDelay);
                    if (!isFinite(delay) || delay < 0) delay = 2500;
                    // Short pause so the shopper sees "Payment confirmed" before the modal goes.
                    state.closeTimer = setTimeout(function () {
                        if (activeModal !== state) return;
                        closeModal();
                        // No onSuccess handler: behave like the standalone checkout and go to the
                        // shop's return URL, if it set one.
                        if (typeof opts.onSuccess !== 'function' && d.returnUrl && isHttpUrl(d.returnUrl)) navigate(d.returnUrl);
                    }, delay);
                }
            } else if (d.type === 'digipay-checkout-return') {
                closeModal();
                if (d.url && isHttpUrl(d.url)) navigate(d.url);
            } else if (d.type === 'digipay-checkout-escape') {
                closeModal();
            }
        };
        window.addEventListener('message', onMessage);
        state.onMessage = onMessage;

        activeModal = state;

        // Move focus into the checkout; it returns to the opener on close.
        setTimeout(function () {
            try { iframe.focus(); } catch (_) { try { closeBtn.focus(); } catch (__) { } }
        }, 0);
    }

    function navigate(url) {
        try { global.location.assign(url); }
        catch (_) { global.location.href = url; }
    }

    function closeModal() {
        if (!activeModal) return;
        var state = activeModal;
        activeModal = null;
        document.removeEventListener('keydown', state.onKey);
        if (state.onMessage) window.removeEventListener('message', state.onMessage);
        if (state.closeTimer) clearTimeout(state.closeTimer);
        state.overlay.remove();
        try { if (state.opener && state.opener.focus) state.opener.focus(); } catch (_) { }
    }

    function requireOrigin() {
        if (!origin) throw new Error('DigiPay: unable to derive host origin from the <script src>');
    }

    /**
     * Opens the hosted checkout in a modal.
     *
     * opts.sessionId   (required) the session to pay.
     * opts.theme       optional 'light' | 'dark'.
     * opts.onStatus    optional function({ sessionId, status }) called on every status change the
     *                  checkout reports: pending, seen, paid, confirmed, underpaid, expired,
     *                  refunded, voided.
     * opts.onSuccess   optional function({ sessionId, status }) called once when the payment is
     *                  confirmed. When you pass it, you decide what happens next (show a thank-you,
     *                  redirect…); without it the widget goes to the session's returnUrl, if any.
     *                  Always confirm the payment on your server (webhook or GET /v1/pay/sessions/{id})
     *                  before fulfilling: a browser callback is a hint, not proof.
     * opts.closeDelay  optional milliseconds to keep the modal open after "confirmed" (default 2500).
     */
    function checkout(opts) {
        if (!opts || !opts.sessionId) throw new Error('DigiPay.checkout requires { sessionId }');
        requireOrigin();
        createModal(withTheme(origin + '/pay/' + encodeURIComponent(opts.sessionId), opts.theme), opts.theme, opts);
    }

    /** Resolves a button logo: 'dgb' → DigiPay's DigiByte logo; otherwise only absolute http(s) URLs. */
    function resolveLogo(logo) {
        if (!logo) return null;
        logo = String(logo).trim();
        if (logo.toLowerCase() === 'dgb') return origin + '/digibyte-logo.svg';
        return /^https?:\/\//i.test(logo) ? logo : null;
    }

    /**
     * Builds a donation button (an <a> to the hosted tip page).
     *
     * opts.address  (required) DigiByte address to pay.
     * opts.amount / opts.name / opts.label / opts.theme  optional, passed to the tip page.
     * opts.text     optional button text (default 'Pay with DigiByte').
     * opts.logo     optional image shown before the text: 'dgb' for the DigiByte logo, or an http(s) URL.
     */
    function button(opts) {
        if (!opts || !opts.address) throw new Error('DigiPay.button requires { address }');
        requireOrigin();
        var url = origin + '/pay/button?to=' + encodeURIComponent(opts.address);
        if (opts.amount && Number(opts.amount) > 0) url += '&amount=' + encodeURIComponent(opts.amount);
        if (opts.name) url += '&name=' + encodeURIComponent(opts.name);
        if (opts.label) url += '&label=' + encodeURIComponent(opts.label);
        url = withTheme(url, opts.theme);

        var a = document.createElement('a');
        a.href = url;
        a.target = '_blank';
        a.rel = 'noopener';
        var logoUrl = resolveLogo(opts.logo);
        if (logoUrl) {
            var img = document.createElement('img');
            img.src = logoUrl;
            img.alt = '';
            img.width = 20;
            img.height = 20;
            img.style.cssText = 'width:20px;height:20px;object-fit:contain;border-radius:50%;flex-shrink:0;';
            a.appendChild(img);
        }
        a.appendChild(document.createTextNode(opts.text || 'Pay with DigiByte'));
        a.style.cssText =
            'display:inline-flex;align-items:center;gap:8px;padding:10px 18px;' +
            'background:#0066ff;color:#fff;border-radius:10px;text-decoration:none;' +
            'font:600 14px system-ui,-apple-system,Segoe UI,sans-serif;' +
            'transition:background 120ms;';
        a.addEventListener('mouseover', function () { this.style.background = '#0052cc'; });
        a.addEventListener('mouseout', function () { this.style.background = '#0066ff'; });
        return a;
    }

    // Auto-bind: any element with data-digipay-checkout becomes a click → checkout trigger.
    // Auto-mount: any element with data-digipay-button gets replaced with a rendered button.
    function bindAll(root) {
        var scope = root || document;
        scope.querySelectorAll('[data-digipay-checkout]').forEach(function (el) {
            if (el.__digipay_bound) return;
            el.__digipay_bound = true;
            el.addEventListener('click', function (e) {
                var sessionId = el.getAttribute('data-session-id') || el.getAttribute('data-digipay-session-id');
                if (!sessionId) return;
                e.preventDefault();
                checkout({ sessionId: sessionId, theme: el.getAttribute('data-theme') });
            });
        });
        scope.querySelectorAll('[data-digipay-button]').forEach(function (el) {
            if (el.__digipay_bound) return;
            el.__digipay_bound = true;
            var a = button({
                address: el.getAttribute('data-address') || el.getAttribute('data-to'),
                amount: el.getAttribute('data-amount'),
                name: el.getAttribute('data-name'),
                label: el.getAttribute('data-label'),
                theme: el.getAttribute('data-theme'),
                logo: el.getAttribute('data-logo'),
                text: (el.textContent || '').trim() || undefined,
            });
            el.replaceWith(a);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { bindAll(); });
    } else {
        bindAll();
    }

    global.DigiPay = {
        checkout: checkout,
        close: closeModal,
        button: button,
        bind: bindAll,
        version: '0.3.0',
    };
})(window);
