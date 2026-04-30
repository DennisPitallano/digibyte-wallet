<?php
if (!defined('ABSPATH')) {
    exit;
}

use Automattic\WooCommerce\Blocks\Payments\Integrations\AbstractPaymentMethodType;

/**
 * WooCommerce Blocks payment method registration.
 *
 * The "Cart & Checkout Blocks" UI (default since WC 8.3) filters out legacy
 * WC_Payment_Gateway implementations unless they also register a block
 * payment-method type. Without this class, DigiPay shows up under WC →
 * Settings → Payments but never appears at the customer-facing checkout —
 * which is the bug the v1 README missed.
 *
 * For a hosted-redirect gateway (no on-page card fields), the JS just needs
 * to render the gateway label + description on the radio. PHP side hands
 * those strings to the JS via wcSettings, avoiding any build step.
 */
final class DigiPay_Blocks_Integration extends AbstractPaymentMethodType
{
    protected $name = 'digipay';

    public function initialize(): void
    {
        $this->settings = (array) get_option('woocommerce_digipay_settings', []);
    }

    public function is_active(): bool
    {
        return !empty($this->settings['enabled']) && $this->settings['enabled'] === 'yes';
    }

    public function get_payment_method_script_handles(): array
    {
        $handle = 'digipay-blocks-integration';

        wp_register_script(
            $handle,
            plugins_url('assets/blocks/checkout.js', DIGIPAY_WC_PLUGIN_FILE),
            // Dependencies pulled from the WP/WC block runtime; loaded by Blocks itself.
            ['wc-blocks-registry', 'wp-element', 'wc-settings'],
            DIGIPAY_WC_VERSION,
            true
        );

        return [$handle];
    }

    public function get_payment_method_data(): array
    {
        // ?ver=<plugin-version> doubles as a cache-buster — the same convention
        // wp_register_script uses. Without it, swapping the SVG mid-release
        // doesn't reach browsers that cached the old bytes by URL.
        $icon = add_query_arg(
            'ver',
            DIGIPAY_WC_VERSION,
            plugins_url('assets/icon.svg', DIGIPAY_WC_PLUGIN_FILE)
        );

        return [
            'title'       => $this->settings['title'] ?? __('DigiByte (DGB)', 'digipay-for-woocommerce'),
            'description' => $this->settings['description'] ?? __('Pay in DigiByte. You\'ll be redirected to a secure DigiPay checkout to complete payment.', 'digipay-for-woocommerce'),
            'supports'    => ['products'],
            'iconUrl'     => $icon,
        ];
    }
}
