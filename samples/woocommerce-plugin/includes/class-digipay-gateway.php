<?php
if (!defined('ABSPATH')) {
    exit;
}

/**
 * DigiPay payment gateway — hosted-checkout MVP.
 *
 * Flow:
 *   1. Customer clicks "Place order" with DigiPay selected.
 *   2. process_payment() POSTs /v1/pay/sessions with an idempotency key.
 *   3. Order meta records _digipay_session_id; customer is redirected to
 *      session.checkoutUrl (DigiPay's hosted checkout page).
 *   4. DigiPay webhook (see DigiPay_Webhook) flips the order forward.
 */
final class DigiPay_Gateway extends WC_Payment_Gateway
{
    public const ID = 'digipay';

    public function __construct()
    {
        $this->id                 = self::ID;
        $this->method_title       = __('DigiPay (DigiByte)', 'digipay-for-woocommerce');
        $this->method_description = __(
            'Accept DigiByte (DGB) via DigiPay hosted checkout. Non-custodial — payments land directly in your wallet. Configure the API key and webhook secret from your DigiPay dashboard.',
            'digipay-for-woocommerce'
        );
        $this->has_fields = false;
        $this->supports   = ['products'];

        $this->icon = apply_filters('digipay_gateway_icon', digipay_icon_url());

        $this->init_form_fields();
        $this->init_settings();

        $this->title       = (string) $this->get_option('title', __('DigiByte (DGB)', 'digipay-for-woocommerce'));
        $this->description = (string) $this->get_option('description', __('Pay in DigiByte. You\'ll be redirected to a secure DigiPay checkout to complete payment.', 'digipay-for-woocommerce'));
        $this->enabled     = (string) $this->get_option('enabled', 'no');

        add_action('woocommerce_update_options_payment_gateways_' . $this->id, [$this, 'process_admin_options']);
    }

    /**
     * Hide the gateway at checkout when essential config is missing — surfacing
     * a half-configured DigiPay gateway means the buyer only finds out it's
     * broken after Place Order, which is the worst possible UX. is_available()
     * is what WooCommerce calls per-checkout-render; the Blocks integration's
     * is_active() also picks this up via the same option lookup.
     */
    public function is_available(): bool
    {
        if (!parent::is_available()) {
            return false;
        }
        $api_key = trim((string) $this->get_option('api_key', ''));
        $secret  = trim((string) $this->get_option('webhook_secret', ''));
        if ($api_key === '' || $secret === '') {
            return false;
        }
        // For currency_mode='fiat' the live price has to come back from
        // CoinGecko in one of the supported denominations. Hiding the
        // gateway for unsupported store currencies is far less surprising
        // than failing at process_payment().
        if ($this->get_option('currency_mode', 'fiat') === 'fiat') {
            $supported = ['USD', 'EUR', 'GBP', 'PHP', 'JPY'];
            if (function_exists('get_woocommerce_currency')) {
                $store = strtoupper((string) get_woocommerce_currency());
                if (!in_array($store, $supported, true)) {
                    return false;
                }
            }
        }
        return true;
    }

    public function init_form_fields(): void
    {
        $this->form_fields = [
            'enabled' => [
                'title'   => __('Enable / disable', 'digipay-for-woocommerce'),
                'type'    => 'checkbox',
                'label'   => __('Enable DigiPay (DigiByte)', 'digipay-for-woocommerce'),
                'default' => 'no',
            ],
            'title' => [
                'title'       => __('Title', 'digipay-for-woocommerce'),
                'type'        => 'text',
                'description' => __('Shown to customers at checkout.', 'digipay-for-woocommerce'),
                'default'     => __('DigiByte (DGB)', 'digipay-for-woocommerce'),
                'desc_tip'    => true,
            ],
            'description' => [
                'title'       => __('Description', 'digipay-for-woocommerce'),
                'type'        => 'textarea',
                'description' => __('Shown to customers at checkout under the title.', 'digipay-for-woocommerce'),
                'default'     => __('Pay in DigiByte. You\'ll be redirected to a secure DigiPay checkout to complete payment.', 'digipay-for-woocommerce'),
            ],
            'api_base_url' => [
                'title'       => __('API base URL', 'digipay-for-woocommerce'),
                'type'        => 'text',
                'description' => __('DigiPay API base URL. Use the default unless you\'re self-hosting or testing on regtest.', 'digipay-for-woocommerce'),
                'default'     => 'https://pay.dgbwallet.app',
                'desc_tip'    => true,
            ],
            'api_key' => [
                'title'       => __('API key', 'digipay-for-woocommerce'),
                'type'        => 'password',
                'description' => __('From DigiPay dashboard → API keys (starts with dgp_).', 'digipay-for-woocommerce'),
                'default'     => '',
                'desc_tip'    => true,
            ],
            'webhook_secret' => [
                'title'       => __('Webhook secret', 'digipay-for-woocommerce'),
                'type'        => 'password',
                'description' => sprintf(
                    /* translators: %s: webhook URL the merchant must register on the DigiPay dashboard */
                    __('From DigiPay dashboard → Webhook. Set the webhook URL on that page to: %s', 'digipay-for-woocommerce'),
                    '<code>' . esc_html(self::webhook_url()) . '</code>'
                ),
                'default'     => '',
            ],
            'currency_mode' => [
                'title'       => __('Currency mode', 'digipay-for-woocommerce'),
                'type'        => 'select',
                'description' => __('How to price the session. "Fiat" sends the WooCommerce order total + currency to DigiPay so the DGB amount is pinned at checkout time. "DGB" assumes the WC store currency is DGB.', 'digipay-for-woocommerce'),
                'default'     => 'fiat',
                'options'     => [
                    'fiat' => __('Fiat (recommended; WC store is priced in USD/EUR/etc.)', 'digipay-for-woocommerce'),
                    'dgb'  => __('DGB (the WC store currency is DigiByte)', 'digipay-for-woocommerce'),
                ],
                'desc_tip'    => true,
            ],
            'expires_in_seconds' => [
                'title'       => __('Session expiry (seconds)', 'digipay-for-woocommerce'),
                'type'        => 'number',
                'description' => __('Price-lock window. Leave blank for the DigiPay default (1800s / 30 min, matches BTCPay).', 'digipay-for-woocommerce'),
                'default'     => '',
                'desc_tip'    => true,
                'custom_attributes' => ['min' => '60', 'max' => '7200', 'step' => '60'],
            ],
            'debug_logging' => [
                'title'   => __('Debug logging', 'digipay-for-woocommerce'),
                'type'    => 'checkbox',
                'label'   => __('Log session creation and webhook events to WooCommerce → Status → Logs (source: digipay)', 'digipay-for-woocommerce'),
                'default' => 'no',
            ],
        ];
    }

    public function process_payment($order_id)
    {
        $order = wc_get_order($order_id);
        if (!$order) {
            wc_add_notice(__('Order not found.', 'digipay-for-woocommerce'), 'error');
            return ['result' => 'failure'];
        }

        $api_key  = (string) $this->get_option('api_key');
        $base_url = (string) $this->get_option('api_base_url', 'https://pay.dgbwallet.app');

        if ($api_key === '') {
            wc_add_notice(__('DigiPay is not configured. Please contact the store owner.', 'digipay-for-woocommerce'), 'error');
            DigiPay_Logger::error('process_payment aborted: API key not set', ['order_id' => $order_id]);
            return ['result' => 'failure'];
        }

        $client = new DigiPay_Client($api_key, $base_url);

        $params = $this->build_session_params($order);
        if (is_wp_error($params)) {
            // Most common cause: fiat-mode and CoinGecko unreachable, or the
            // store currency isn't in the supported set. Surface the message
            // verbatim — DigiPay_Price returns user-friendly strings.
            wc_add_notice($params->get_error_message(), 'error');
            return ['result' => 'failure'];
        }

        // Idempotency key derived from the order id — re-submitting the same
        // order (a double-clicked Place Order, or a customer hitting "Pay")
        // returns the original session, never a duplicate. Keep the key
        // stable for the lifetime of the order, not the request.
        $idempotency_key = 'wc_order_' . $order->get_id();

        $session = $client->create_session($params, $idempotency_key);

        if (is_wp_error($session)) {
            $message = $session->get_error_message();
            wc_add_notice(
                sprintf(
                    /* translators: %s: error returned by the DigiPay API */
                    __('Could not start a DigiPay payment: %s', 'digipay-for-woocommerce'),
                    $message
                ),
                'error'
            );
            return ['result' => 'failure'];
        }

        $session_id   = isset($session['id']) ? (string) $session['id'] : '';
        $checkout_url = isset($session['checkoutUrl']) ? (string) $session['checkoutUrl'] : '';
        if ($session_id === '' || $checkout_url === '') {
            wc_add_notice(__('DigiPay returned an unexpected response. Please try again.', 'digipay-for-woocommerce'), 'error');
            DigiPay_Logger::error('session create returned without id/checkoutUrl', [
                'order_id' => $order_id,
                'session'  => array_intersect_key($session, array_flip(['id', 'checkoutUrl', 'status'])),
            ]);
            return ['result' => 'failure'];
        }

        $order->update_meta_data('_digipay_session_id', $session_id);
        $order->update_meta_data('_digipay_checkout_url', $checkout_url);
        $order->add_order_note(sprintf(
            /* translators: %s: DigiPay session id */
            __('DigiPay session created: %s', 'digipay-for-woocommerce'),
            $session_id
        ));
        $order->save();

        // Empty the cart only on success — a failed session create should leave
        // the customer where they are with their cart intact.
        WC()->cart->empty_cart();

        DigiPay_Logger::info('session created', [
            'order_id'   => $order_id,
            'session_id' => $session_id,
        ]);

        return [
            'result'   => 'success',
            'redirect' => $checkout_url,
        ];
    }

    /**
     * Public so it can be reused on the settings page (the webhook URL hint).
     */
    public static function webhook_url(): string
    {
        // wc-api endpoint — WooCommerce routes /?wc-api=digipay_webhook to the
        // 'woocommerce_api_digipay_webhook' action registered in the bootstrap.
        return add_query_arg('wc-api', 'digipay_webhook', home_url('/'));
    }

    /**
     * @return array<string,mixed>|WP_Error
     */
    private function build_session_params(WC_Order $order)
    {
        $mode = (string) $this->get_option('currency_mode', 'fiat');
        $total = (float) $order->get_total();

        $params = [
            'label'    => sprintf(
                /* translators: 1: order number, 2: site name */
                __('Order #%1$s — %2$s', 'digipay-for-woocommerce'),
                $order->get_order_number(),
                get_bloginfo('name')
            ),
            'memo'     => 'wc_order_id=' . $order->get_id(),
            'metadata' => [
                // The PaySession.Source comment in the API code already
                // anticipates "woocommerce" here — used for dashboard filtering.
                'source'      => 'woocommerce',
                'wc_order_id' => (string) $order->get_id(),
                'wc_site'     => home_url('/'),
            ],
        ];

        if ($mode === 'dgb') {
            // The WC store currency itself is DGB — order total is already DGB.
            $params['amount'] = $total;
        } else {
            // Fiat-priced: convert WC's order total to DGB at the live
            // CoinGecko rate (transient-cached 60s) and send all four
            // fields. Pay.Api requires `amount` (DGB) regardless; the fiat
            // metadata drives the dashboard view + volatility warning on
            // the hosted checkout.
            $currency = strtoupper((string) $order->get_currency());
            require_once DIGIPAY_WC_PLUGIN_DIR . 'includes/class-digipay-price.php';
            $conv = DigiPay_Price::fiat_to_dgb($total, $currency);
            if (is_wp_error($conv)) {
                return $conv;
            }
            $params['amount']             = $conv['amount_dgb'];
            $params['fiatAmount']         = $total;
            $params['fiatCurrency']       = $currency;
            $params['dgbPriceAtCreation'] = $conv['dgb_price'];
        }

        $expiry = (string) $this->get_option('expires_in_seconds', '');
        if ($expiry !== '' && ctype_digit($expiry)) {
            $params['expiresInSeconds'] = (int) $expiry;
        }

        // Bounce the customer back to the WC thank-you page once they pay.
        $return_url = $this->get_return_url($order);
        if (is_string($return_url) && $return_url !== '') {
            $params['returnUrl'] = $return_url;
        }

        return $params;
    }
}
