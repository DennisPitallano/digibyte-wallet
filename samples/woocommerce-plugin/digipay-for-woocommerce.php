<?php
/**
 * Plugin Name: DigiPay for WooCommerce
 * Plugin URI: https://github.com/DennisPitallano/digibyte-wallet/tree/main/samples/woocommerce-plugin
 * Description: Accept DigiByte (DGB) payments in WooCommerce via DigiPay's hosted checkout. Non-custodial, HMAC-signed webhooks, idempotent session creation.
 * Version: 0.1.0
 * Requires at least: 6.0
 * Requires PHP: 7.4
 * Author: DigiPay contributors
 * Author URI: https://pay.dgbwallet.app
 * License: GPL-2.0-or-later
 * License URI: https://www.gnu.org/licenses/gpl-2.0.html
 * Text Domain: digipay-for-woocommerce
 * WC requires at least: 7.0
 * WC tested up to: 9.4
 * Update URI: https://github.com/DennisPitallano/digibyte-wallet/releases
 */

if (!defined('ABSPATH')) {
    exit;
}

define('DIGIPAY_WC_PLUGIN_FILE', __FILE__);
define('DIGIPAY_WC_PLUGIN_DIR', plugin_dir_path(__FILE__));
define('DIGIPAY_WC_VERSION', '0.1.0');

// Bootstrap only when WooCommerce is loaded — guards against fatals on
// activate-without-Woo. Hooked at plugins_loaded:10 so Woo (which loads at 0)
// is already in scope.
add_action('plugins_loaded', static function (): void {
    if (!class_exists('WooCommerce')) {
        add_action('admin_notices', static function (): void {
            echo '<div class="notice notice-error"><p>'
                . esc_html__('DigiPay for WooCommerce requires WooCommerce to be installed and active.', 'digipay-for-woocommerce')
                . '</p></div>';
        });
        return;
    }

    require_once DIGIPAY_WC_PLUGIN_DIR . 'includes/class-digipay-logger.php';
    require_once DIGIPAY_WC_PLUGIN_DIR . 'includes/class-digipay-client.php';
    require_once DIGIPAY_WC_PLUGIN_DIR . 'includes/class-digipay-webhook.php';
    require_once DIGIPAY_WC_PLUGIN_DIR . 'includes/class-digipay-gateway.php';

    // Register the gateway so it appears in WooCommerce → Settings → Payments.
    add_filter('woocommerce_payment_gateways', static function (array $gateways): array {
        $gateways[] = 'DigiPay_Gateway';
        return $gateways;
    });

    // wc-api endpoint: POST {site}/?wc-api=digipay_webhook
    add_action('woocommerce_api_digipay_webhook', [DigiPay_Webhook::class, 'handle']);

    // WooCommerce Blocks (the default checkout since WC 8.3) doesn't show
    // legacy gateways unless they register a block payment-method type. The
    // class is loaded lazily because AbstractPaymentMethodType only exists
    // when WC Blocks is active.
    add_action('woocommerce_blocks_loaded', static function (): void {
        if (!class_exists('Automattic\\WooCommerce\\Blocks\\Payments\\Integrations\\AbstractPaymentMethodType')) {
            return;
        }
        require_once DIGIPAY_WC_PLUGIN_DIR . 'includes/class-digipay-blocks-integration.php';
        add_action(
            'woocommerce_blocks_payment_method_type_registration',
            static function ($registry): void {
                $registry->register(new DigiPay_Blocks_Integration());
            }
        );
    });

    // Declare HPOS (custom-order-tables) compatibility — the plugin only uses
    // $order->get_meta / update_meta_data, both of which are HPOS-safe.
    add_action('before_woocommerce_init', static function (): void {
        if (class_exists('\\Automattic\\WooCommerce\\Utilities\\FeaturesUtil')) {
            \Automattic\WooCommerce\Utilities\FeaturesUtil::declare_compatibility(
                'custom_order_tables',
                DIGIPAY_WC_PLUGIN_FILE,
                true
            );
        }
    });
}, 10);
