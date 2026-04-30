<?php
if (!defined('ABSPATH')) {
    exit;
}

/**
 * Minimal HTTP client for DigiPay's REST API.
 *
 * Only one endpoint is needed for the hosted-checkout flow — POST /v1/pay/sessions.
 * Uses wp_remote_post() so site-wide HTTP filters (proxies, custom CA bundles,
 * mocked transports in test) apply.
 *
 * Returns the parsed session as an associative array on success, or a WP_Error
 * the gateway surfaces back to checkout. The DigiPay error envelope shape comes
 * from src/DigiByte.Pay.Api — the typical 4xx body is { error, message } plus
 * an HTTP status. We preserve all three for the caller.
 */
final class DigiPay_Client
{
    private string $api_key;
    private string $base_url;

    public function __construct(string $api_key, string $base_url)
    {
        $this->api_key  = trim($api_key);
        $this->base_url = rtrim(trim($base_url), '/');
    }

    /**
     * @param array<string,mixed> $params  amount/fiatAmount/fiatCurrency/label/memo/expiresInSeconds/storeId
     * @param string $idempotency_key      passed straight through as the Idempotency-Key header
     * @return array<string,mixed>|WP_Error parsed session DTO or transport/api error
     */
    public function create_session(array $params, string $idempotency_key)
    {
        if ($this->api_key === '') {
            return new WP_Error('digipay_missing_key', __('DigiPay API key is not configured.', 'digipay-for-woocommerce'));
        }
        if ($this->base_url === '') {
            return new WP_Error('digipay_missing_url', __('DigiPay API base URL is not configured.', 'digipay-for-woocommerce'));
        }

        $url = $this->base_url . '/v1/pay/sessions';

        $headers = [
            'Authorization' => 'Bearer ' . $this->api_key,
            'Content-Type'  => 'application/json',
            'Accept'        => 'application/json',
            'User-Agent'    => 'DigiPay-WooCommerce/' . DIGIPAY_WC_VERSION . '; WP/' . get_bloginfo('version'),
        ];
        if ($idempotency_key !== '') {
            // Stripe-shaped: replay-safe within 24h, scoped per merchant.
            // Order id (e.g. wc_order_42) is the natural choice — a double-clicked
            // "Place order" can't mint two sessions.
            $headers['Idempotency-Key'] = $idempotency_key;
        }

        $response = wp_remote_post($url, [
            'headers' => $headers,
            'body'    => wp_json_encode($params),
            'timeout' => 10,
            // Don't redirect on a payment endpoint — anything other than 200/4xx
            // is the gateway's job to surface, not silently follow.
            'redirection' => 0,
        ]);

        if (is_wp_error($response)) {
            DigiPay_Logger::error('digipay request failed (transport)', [
                'url'   => $url,
                'error' => $response->get_error_message(),
            ]);
            return $response;
        }

        $status = (int) wp_remote_retrieve_response_code($response);
        $body   = (string) wp_remote_retrieve_body($response);
        $parsed = json_decode($body, true);

        if ($status >= 200 && $status < 300 && is_array($parsed)) {
            return $parsed;
        }

        $api_error   = is_array($parsed) && isset($parsed['error']) ? (string) $parsed['error'] : '';
        $api_message = is_array($parsed) && isset($parsed['message']) ? (string) $parsed['message'] : '';
        $detail = trim($api_message !== '' ? $api_message : $api_error);
        if ($detail === '') {
            $detail = sprintf(__('DigiPay returned HTTP %d.', 'digipay-for-woocommerce'), $status);
        }

        DigiPay_Logger::error('digipay session create rejected', [
            'status' => $status,
            'error'  => $api_error,
        ]);

        return new WP_Error('digipay_api_error', $detail, ['status' => $status]);
    }
}
