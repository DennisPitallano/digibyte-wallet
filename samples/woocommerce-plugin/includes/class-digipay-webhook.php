<?php
if (!defined('ABSPATH')) {
    exit;
}

/**
 * DigiPay webhook receiver.
 *
 * Wire-up:
 *   POST {site}/?wc-api=digipay_webhook
 *
 * Header contract (set by src/DigiByte.Pay.Api/Services/WebhookDispatcher.cs):
 *   X-DigiPay-Signature: sha256=<hex(HMAC_SHA256(secret, raw_body))>
 *   X-DigiPay-Event:     session.paid | session.confirmed | session.expired | …
 *   X-DigiPay-Delivery:  wdel_…  (per-attempt id, useful for retry de-dupe)
 *
 * Critical: HMAC covers the *raw* request bytes. Anything that re-serialises
 * JSON (e.g. WP_REST middleware) breaks verification by normalising whitespace
 * — read php://input directly. The Express sample (samples/express-store/server.ts)
 * has the equivalent warning.
 */
final class DigiPay_Webhook
{
    public static function handle(): void
    {
        $raw = file_get_contents('php://input');
        if ($raw === false) {
            $raw = '';
        }

        $signature_header = isset($_SERVER['HTTP_X_DIGIPAY_SIGNATURE'])
            ? (string) $_SERVER['HTTP_X_DIGIPAY_SIGNATURE']
            : '';
        $event_header = isset($_SERVER['HTTP_X_DIGIPAY_EVENT'])
            ? (string) $_SERVER['HTTP_X_DIGIPAY_EVENT']
            : '';
        $delivery_header = isset($_SERVER['HTTP_X_DIGIPAY_DELIVERY'])
            ? (string) $_SERVER['HTTP_X_DIGIPAY_DELIVERY']
            : '';

        $settings = get_option('woocommerce_digipay_settings');
        $secret   = is_array($settings) && isset($settings['webhook_secret'])
            ? (string) $settings['webhook_secret']
            : '';

        $verify = self::verify_signature($raw, $signature_header, $secret);
        if ($verify !== true) {
            DigiPay_Logger::error('webhook rejected: ' . $verify, [
                'delivery' => $delivery_header,
                'event'    => $event_header,
            ]);
            self::respond(401);
            return;
        }

        $event = json_decode($raw, true);
        if (!is_array($event)) {
            DigiPay_Logger::error('webhook rejected: malformed JSON', [
                'delivery' => $delivery_header,
            ]);
            self::respond(400);
            return;
        }

        $session = isset($event['session']) && is_array($event['session']) ? $event['session'] : [];
        $session_id = isset($session['id']) ? (string) $session['id'] : '';
        $event_name = isset($event['event']) ? (string) $event['event'] : $event_header;

        if ($session_id === '') {
            DigiPay_Logger::warning('webhook missing session.id', [
                'delivery' => $delivery_header,
                'event'    => $event_name,
            ]);
            self::respond(200); // ack — nothing to act on, don't trigger retries
            return;
        }

        $order = self::find_order_by_session_id($session_id);
        if (!$order) {
            // Webhook for a session this WP site doesn't know — most likely a
            // shared store between multiple sites, or a stale order. Ack to
            // stop retries; log for investigation.
            DigiPay_Logger::warning('webhook for unknown session', [
                'session_id' => $session_id,
                'event'      => $event_name,
                'delivery'   => $delivery_header,
            ]);
            self::respond(200);
            return;
        }

        // Idempotent: re-firing the same delivery should be a no-op. Track the
        // last-seen delivery id on the order so a manual replay (or a DigiPay
        // retry that arrived after we already responded) doesn't double-act.
        $last_delivery = (string) $order->get_meta('_digipay_last_delivery_id', true);
        if ($delivery_header !== '' && $delivery_header === $last_delivery) {
            DigiPay_Logger::info('webhook duplicate delivery ignored', [
                'order_id' => $order->get_id(),
                'delivery' => $delivery_header,
            ]);
            self::respond(200);
            return;
        }

        self::apply_event($order, $event_name, $session);

        if ($delivery_header !== '') {
            $order->update_meta_data('_digipay_last_delivery_id', $delivery_header);
        }
        $order->update_meta_data('_digipay_last_event', $event_name);
        $order->save();

        DigiPay_Logger::info('webhook applied', [
            'order_id'   => $order->get_id(),
            'session_id' => $session_id,
            'event'      => $event_name,
        ]);

        self::respond(200);
    }

    /**
     * Constant-time signature verification. Returns true on match, or a short
     * reason string on failure (logged, never surfaced to the caller).
     *
     * @return true|string
     */
    public static function verify_signature(string $raw_body, string $header, string $secret)
    {
        if ($secret === '') {
            return 'webhook secret not configured';
        }
        if ($header === '') {
            return 'missing X-DigiPay-Signature header';
        }
        // Header format: "sha256=<hex>". Tolerate the prefix being absent for
        // forward-compat with future schemes, but require sha256 if present.
        $hex = $header;
        if (strncmp($header, 'sha256=', 7) === 0) {
            $hex = substr($header, 7);
        } elseif (strpos($header, '=') !== false) {
            return 'unsupported signature scheme';
        }
        $hex = trim($hex);
        if ($hex === '' || !ctype_xdigit($hex) || strlen($hex) !== 64) {
            return 'malformed signature hex';
        }

        $expected = hash_hmac('sha256', $raw_body, $secret);
        return hash_equals($expected, strtolower($hex)) ? true : 'signature mismatch';
    }

    /**
     * Map a DigiPay event onto WooCommerce order state. Unknown events are
     * silently acked so DigiPay doesn't retry forward-compatible events forever.
     *
     * @param array<string,mixed> $session
     */
    private static function apply_event(WC_Order $order, string $event_name, array $session): void
    {
        $txid = isset($session['paidTxid']) ? (string) $session['paidTxid'] : '';

        switch ($event_name) {
            case 'session.paid':
                if (!$order->is_paid()) {
                    // payment_complete advances the order to processing/completed
                    // depending on whether any line item needs shipping.
                    $order->payment_complete($txid !== '' ? $txid : '');
                    $order->add_order_note(sprintf(
                        /* translators: %s: DigiByte transaction id */
                        __('DigiPay reported payment seen on-chain. Tx: %s', 'digipay-for-woocommerce'),
                        $txid !== '' ? $txid : 'n/a'
                    ));
                }
                break;

            case 'session.confirmed':
                // 6+ confirmations on DigiByte — definitive. Make sure the
                // order is at least processing; payment_complete() is idempotent.
                if (!$order->is_paid()) {
                    $order->payment_complete($txid !== '' ? $txid : '');
                }
                $order->add_order_note(sprintf(
                    /* translators: %s: DigiByte transaction id */
                    __('DigiPay confirmed payment (6+ confirmations). Tx: %s', 'digipay-for-woocommerce'),
                    $txid !== '' ? $txid : 'n/a'
                ));
                break;

            case 'session.expired':
                if (!in_array($order->get_status(), ['failed', 'cancelled', 'completed'], true)) {
                    $order->update_status(
                        'failed',
                        __('DigiPay session expired before payment was received.', 'digipay-for-woocommerce')
                    );
                }
                break;

            case 'session.underpaid':
                if (!in_array($order->get_status(), ['on-hold', 'completed', 'failed'], true)) {
                    $received = isset($session['receivedSatoshis'])
                        ? number_format(((int) $session['receivedSatoshis']) / 100_000_000, 8, '.', '')
                        : '?';
                    $expected = isset($session['amount']) ? (string) $session['amount'] : '?';
                    $order->update_status(
                        'on-hold',
                        sprintf(
                            /* translators: 1: received DGB, 2: expected DGB */
                            __('DigiPay reported underpayment: received %1$s DGB of %2$s expected. Manual review required.', 'digipay-for-woocommerce'),
                            $received,
                            $expected
                        )
                    );
                }
                break;

            case 'session.refunded':
            case 'session.voided':
                // Non-custodial: refunds/voids are merchant-initiated outside this
                // plugin (admin sends DGB, stamps txid). Annotate the order; do
                // not auto-flip status — the WC admin already controls that.
                $note = isset($session['refundNote']) ? (string) $session['refundNote'] : '';
                $rtxid = isset($session['refundTxid']) ? (string) $session['refundTxid'] : '';
                $order->add_order_note(sprintf(
                    /* translators: 1: event name, 2: refund tx id, 3: refund note */
                    __('DigiPay %1$s recorded. Refund tx: %2$s. Note: %3$s', 'digipay-for-woocommerce'),
                    $event_name,
                    $rtxid !== '' ? $rtxid : 'n/a',
                    $note !== '' ? $note : 'n/a'
                ));
                break;

            default:
                // Forward-compatibility: log and ack. Don't 4xx — DigiPay would
                // retry forever on a future event we haven't taught the plugin
                // about yet.
                DigiPay_Logger::info('webhook unknown event acked', [
                    'order_id' => $order->get_id(),
                    'event'    => $event_name,
                ]);
                break;
        }
    }

    private static function find_order_by_session_id(string $session_id): ?WC_Order
    {
        $orders = wc_get_orders([
            'limit'      => 1,
            'meta_key'   => '_digipay_session_id',
            'meta_value' => $session_id,
            // Don't filter on status — a webhook for an "expired" order should
            // still find its way home.
            'status'     => array_keys(wc_get_order_statuses()),
            'return'     => 'objects',
        ]);
        if (empty($orders)) {
            return null;
        }
        $order = $orders[0];
        return $order instanceof WC_Order ? $order : null;
    }

    private static function respond(int $status): void
    {
        // Don't echo error reasons — keeps an attacker from using the endpoint
        // as a signature oracle.
        status_header($status);
        nocache_headers();
        header('Content-Type: text/plain; charset=utf-8');
        echo $status === 200 ? 'ok' : '';
        // wc-api action handlers must not let WP keep rendering page chrome.
        exit;
    }
}
