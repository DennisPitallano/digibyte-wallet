<?php
if (!defined('ABSPATH')) {
    exit;
}

/**
 * DGB price fetcher for fiat-priced sessions.
 *
 * Pay.Api's POST /v1/pay/sessions requires `amount` (DGB) regardless of
 * whether `fiatAmount` + `fiatCurrency` are supplied — the fiat fields are
 * captured for the dashboard / volatility warning, not used to compute the
 * DGB amount server-side. So when the WC store is priced in fiat we have to
 * convert here before posting.
 *
 * Source: CoinGecko's simple/price endpoint. Free, no API key, ~50/min limit
 * on the free tier. Sibling DigiByte.Web wallet uses the same source via a
 * server-side proxy with a 60s cache; we mirror that here as a WP transient
 * so multiple sessions per minute don't hammer the upstream.
 */
final class DigiPay_Price
{
    private const TRANSIENT_PREFIX = 'digipay_dgb_price_';
    private const CACHE_TTL_SECONDS = 60;
    private const COINGECKO_URL = 'https://api.coingecko.com/api/v3/simple/price';

    // Mirrors the currency set DigiByte.Web supports. Anything outside this is
    // returned as a WP_Error so the caller surfaces a useful message at checkout.
    private const SUPPORTED = ['USD', 'EUR', 'GBP', 'PHP', 'JPY'];

    /**
     * Returns the DGB price denominated in $currency (e.g. how many USD per
     * 1 DGB), or a WP_Error on failure / unsupported currency.
     *
     * @return float|WP_Error
     */
    public static function get_dgb_price(string $currency)
    {
        $currency = strtoupper(trim($currency));
        if (!in_array($currency, self::SUPPORTED, true)) {
            return new WP_Error(
                'digipay_unsupported_currency',
                sprintf(
                    /* translators: %s: ISO currency code */
                    __('DigiPay does not have a DGB price for %s. Supported: USD, EUR, GBP, PHP, JPY.', 'digipay-for-woocommerce'),
                    $currency
                )
            );
        }

        $cached = get_transient(self::TRANSIENT_PREFIX . $currency);
        if (is_array($cached) && isset($cached['price'])) {
            return (float) $cached['price'];
        }

        $url = add_query_arg(
            [
                'ids'           => 'digibyte',
                'vs_currencies' => strtolower($currency),
            ],
            self::COINGECKO_URL
        );

        $response = wp_remote_get($url, [
            'timeout' => 8,
            'headers' => [
                'Accept'     => 'application/json',
                'User-Agent' => 'DigiPay-WooCommerce/' . DIGIPAY_WC_VERSION . '; WP/' . get_bloginfo('version'),
            ],
        ]);

        if (is_wp_error($response)) {
            DigiPay_Logger::error('DGB price fetch failed (transport)', [
                'currency' => $currency,
                'error'    => $response->get_error_message(),
            ]);
            return $response;
        }

        $status = (int) wp_remote_retrieve_response_code($response);
        $body   = (string) wp_remote_retrieve_body($response);
        $parsed = json_decode($body, true);

        $price = null;
        if ($status === 200 && is_array($parsed) && isset($parsed['digibyte'][strtolower($currency)])) {
            $price = (float) $parsed['digibyte'][strtolower($currency)];
        }

        if (!$price || $price <= 0) {
            DigiPay_Logger::error('DGB price unavailable from upstream', [
                'currency' => $currency,
                'status'   => $status,
            ]);
            return new WP_Error(
                'digipay_price_unavailable',
                __('Could not fetch the current DGB price. Try again in a moment.', 'digipay-for-woocommerce')
            );
        }

        set_transient(
            self::TRANSIENT_PREFIX . $currency,
            ['price' => $price, 'fetched_at' => time()],
            self::CACHE_TTL_SECONDS
        );

        return $price;
    }

    /**
     * Convert a fiat amount to DGB at the live rate. Rounded to 8 decimals
     * (1 satoshi precision; matches what Pay.Api stores internally).
     *
     * @return array{amount_dgb: float, dgb_price: float}|WP_Error
     */
    public static function fiat_to_dgb(float $fiat_amount, string $currency)
    {
        $price = self::get_dgb_price($currency);
        if (is_wp_error($price)) {
            return $price;
        }
        $amount_dgb = round($fiat_amount / $price, 8);
        if ($amount_dgb <= 0) {
            return new WP_Error(
                'digipay_amount_zero',
                __('Computed DGB amount rounded to zero. Increase the order total.', 'digipay-for-woocommerce')
            );
        }
        return [
            'amount_dgb' => $amount_dgb,
            'dgb_price'  => $price,
        ];
    }
}
