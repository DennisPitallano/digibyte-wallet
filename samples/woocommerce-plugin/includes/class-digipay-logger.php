<?php
if (!defined('ABSPATH')) {
    exit;
}

/**
 * Thin wrapper over wc_get_logger() so plugin code stays focused on flow.
 * Output lands in WooCommerce → Status → Logs under source "digipay".
 *
 * Gated on the gateway's debug_logging setting — log calls are no-ops when
 * disabled, so production sites don't pile up disk usage on the happy path.
 */
final class DigiPay_Logger
{
    private const SOURCE = 'digipay';

    private static ?bool $enabled = null;

    public static function info(string $message, array $context = []): void
    {
        self::log('info', $message, $context);
    }

    public static function warning(string $message, array $context = []): void
    {
        self::log('warning', $message, $context);
    }

    public static function error(string $message, array $context = []): void
    {
        // Errors always log, regardless of the debug toggle — a 401 on every
        // webhook in production should be visible without flipping a setting.
        self::write('error', $message, $context);
    }

    private static function log(string $level, string $message, array $context): void
    {
        if (!self::is_enabled()) {
            return;
        }
        self::write($level, $message, $context);
    }

    private static function write(string $level, string $message, array $context): void
    {
        if (!function_exists('wc_get_logger')) {
            return;
        }
        $line = $message;
        if (!empty($context)) {
            // Never log the webhook secret — defensive scrub even though no
            // caller is meant to pass it.
            unset($context['secret'], $context['webhook_secret'], $context['api_key']);
            $line .= ' ' . wp_json_encode($context);
        }
        wc_get_logger()->log($level, $line, ['source' => self::SOURCE]);
    }

    private static function is_enabled(): bool
    {
        if (self::$enabled !== null) {
            return self::$enabled;
        }
        $settings = get_option('woocommerce_digipay_settings');
        self::$enabled = is_array($settings)
            && isset($settings['debug_logging'])
            && $settings['debug_logging'] === 'yes';
        return self::$enabled;
    }
}
