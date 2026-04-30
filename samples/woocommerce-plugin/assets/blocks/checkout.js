/**
 * DigiPay — WooCommerce Blocks payment method registration.
 *
 * Renders the gateway label + description on the block-based checkout radio.
 * For a hosted-redirect gateway there's no on-page card form to draw, so
 * `Content` is just the description text — the actual checkout UI lives on
 * DigiPay's site after process_payment() returns its redirect URL.
 *
 * Vanilla JS so the plugin ships without a build step. Uses globals exposed
 * by WC Blocks: window.wc.wcBlocksRegistry + window.wc.wcSettings.
 */
(function () {
    'use strict';

    if (!window.wc || !window.wc.wcBlocksRegistry || !window.wc.wcSettings || !window.wp || !window.wp.element) {
        // Block runtime missing — bail silently. The legacy shortcode-checkout
        // path still works for sites that haven't switched to blocks.
        return;
    }

    var registerPaymentMethod = window.wc.wcBlocksRegistry.registerPaymentMethod;
    var getSetting = window.wc.wcSettings.getSetting;
    var createElement = window.wp.element.createElement;

    var settings = getSetting('digipay_data', {});
    var label = settings.title || 'DigiByte (DGB)';
    var description = settings.description || '';
    var iconUrl = settings.iconUrl || '';

    function Label() {
        var children = [createElement('span', { key: 'text' }, label)];
        if (iconUrl) {
            children.unshift(createElement('img', {
                key: 'icon',
                src: iconUrl,
                alt: '',
                style: { width: 24, height: 24, marginRight: 8, verticalAlign: 'middle' },
            }));
        }
        return createElement('span', { className: 'digipay-blocks-label' }, children);
    }

    function Content() {
        // Plain description; sanitised on the PHP side.
        return createElement('div', { className: 'digipay-blocks-description' }, description);
    }

    registerPaymentMethod({
        name: 'digipay',
        label: createElement(Label, null),
        content: createElement(Content, null),
        // Editor preview in the Site Editor — same as the live render.
        edit: createElement(Content, null),
        // Hosted-redirect gateway: always available once configured. The PHP
        // is_active() guard already gates by the merchant's enabled toggle.
        canMakePayment: function () { return true; },
        ariaLabel: label,
        supports: {
            features: settings.supports || ['products'],
        },
    });
})();
