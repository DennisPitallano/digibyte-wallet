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
 *   DigiPay.close();
 *   const el = DigiPay.button({ address: 'dgb1q…', amount: 5, name: 'Shop' });
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

    function createModal(iframeSrc) {
        if (activeModal) closeModal();

        var overlay = document.createElement('div');
        overlay.className = 'digipay-modal';
        overlay.style.cssText =
            'position:fixed;inset:0;background:rgba(0,0,0,0.6);z-index:2147483647;' +
            'display:flex;align-items:center;justify-content:center;padding:16px;' +
            'animation:digipayFadeIn 150ms ease-out;';
        overlay.addEventListener('click', function (e) { if (e.target === overlay) closeModal(); });

        var container = document.createElement('div');
        container.style.cssText =
            'position:relative;width:100%;max-width:460px;height:min(720px,90vh);' +
            'background:#001529;border-radius:20px;overflow:hidden;' +
            'box-shadow:0 24px 48px rgba(0,0,0,0.4);';

        var closeBtn = document.createElement('button');
        closeBtn.innerHTML = '&times;';
        closeBtn.setAttribute('aria-label', 'Close');
        closeBtn.style.cssText =
            'position:absolute;top:10px;right:12px;width:32px;height:32px;' +
            'border:0;background:rgba(255,255,255,0.12);color:#fff;border-radius:9999px;' +
            'font:bold 22px/1 system-ui,sans-serif;cursor:pointer;z-index:1;' +
            'display:flex;align-items:center;justify-content:center;padding:0;';
        closeBtn.addEventListener('mouseover', function () { this.style.background = 'rgba(255,255,255,0.22)'; });
        closeBtn.addEventListener('mouseout', function () { this.style.background = 'rgba(255,255,255,0.12)'; });
        closeBtn.addEventListener('click', closeModal);

        var iframe = document.createElement('iframe');
        iframe.src = iframeSrc;
        iframe.style.cssText = 'width:100%;height:100%;border:0;display:block;';
        iframe.setAttribute('allow', 'clipboard-write');

        container.appendChild(closeBtn);
        container.appendChild(iframe);
        overlay.appendChild(container);

        if (!document.getElementById('__digipay_styles')) {
            var style = document.createElement('style');
            style.id = '__digipay_styles';
            style.textContent = '@keyframes digipayFadeIn{from{opacity:0}to{opacity:1}}';
            document.head.appendChild(style);
        }

        document.body.appendChild(overlay);

        var onKey = function (e) { if (e.key === 'Escape') closeModal(); };
        document.addEventListener('keydown', onKey);

        activeModal = { overlay: overlay, onKey: onKey };
    }

    function closeModal() {
        if (!activeModal) return;
        document.removeEventListener('keydown', activeModal.onKey);
        activeModal.overlay.remove();
        activeModal = null;
    }

    function requireOrigin() {
        if (!origin) throw new Error('DigiPay: unable to derive host origin from the <script src>');
    }

    function checkout(opts) {
        if (!opts || !opts.sessionId) throw new Error('DigiPay.checkout requires { sessionId }');
        requireOrigin();
        createModal(origin + '/pay/' + encodeURIComponent(opts.sessionId));
    }

    function button(opts) {
        if (!opts || !opts.address) throw new Error('DigiPay.button requires { address }');
        requireOrigin();
        var url = origin + '/pay/button?to=' + encodeURIComponent(opts.address);
        if (opts.amount && Number(opts.amount) > 0) url += '&amount=' + encodeURIComponent(opts.amount);
        if (opts.name) url += '&name=' + encodeURIComponent(opts.name);
        if (opts.label) url += '&label=' + encodeURIComponent(opts.label);

        var a = document.createElement('a');
        a.href = url;
        a.target = '_blank';
        a.rel = 'noopener';
        a.textContent = opts.text || 'Pay with DigiByte';
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
                checkout({ sessionId: sessionId });
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
                text: el.textContent || undefined,
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
        version: '0.1.0',
    };
})(window);
