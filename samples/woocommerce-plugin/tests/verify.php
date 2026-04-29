<?php
/**
 * Offline smoke test for DigiPay_Webhook::verify_signature.
 *
 * Runs without WordPress, WooCommerce, or any network — just exercises the
 * pure HMAC verification path against the captured fixtures in
 * webhook-fixtures/. CI-friendly: no external services, no DB.
 *
 * Usage:
 *   php tests/verify.php
 *
 * Exit code is 0 on success, 1 on any failure.
 */

declare(strict_types=1);

// The plugin source files guard with `if (!defined('ABSPATH')) exit;`. Define
// the constant up-front so we can include them outside WordPress.
if (!defined('ABSPATH')) {
    define('ABSPATH', __DIR__ . '/');
}

require_once __DIR__ . '/../includes/class-digipay-webhook.php';

$fixtures_dir = __DIR__ . '/webhook-fixtures';
$secret = trim((string) file_get_contents($fixtures_dir . '/secret.txt'));

$cases = [
    'session.paid',
    'session.confirmed',
    'session.expired',
];

$failed = 0;

foreach ($cases as $name) {
    $body = file_get_contents("$fixtures_dir/$name.json");
    $sig  = trim((string) file_get_contents("$fixtures_dir/$name.sig"));

    // 1) happy path
    assert_true(
        DigiPay_Webhook::verify_signature($body, $sig, $secret) === true,
        "$name: valid signature must verify"
    ) or $failed++;

    // 2) tampered body — flip one byte
    $tampered = $body . ' ';
    assert_true(
        DigiPay_Webhook::verify_signature($tampered, $sig, $secret) !== true,
        "$name: tampered body must NOT verify"
    ) or $failed++;

    // 3) tampered signature — flip one hex digit
    $bad_sig = preg_replace('/.$/', '0', $sig);
    if ($bad_sig === $sig) { $bad_sig = preg_replace('/.$/', '1', $sig); }
    assert_true(
        DigiPay_Webhook::verify_signature($body, (string) $bad_sig, $secret) !== true,
        "$name: tampered signature must NOT verify"
    ) or $failed++;

    // 4) wrong secret
    assert_true(
        DigiPay_Webhook::verify_signature($body, $sig, 'wrong-secret') !== true,
        "$name: wrong secret must NOT verify"
    ) or $failed++;

    // 5) missing header
    assert_true(
        DigiPay_Webhook::verify_signature($body, '', $secret) !== true,
        "$name: missing signature header must NOT verify"
    ) or $failed++;
}

// Edge cases on header parsing.
$body = file_get_contents("$fixtures_dir/session.paid.json");
$sig  = trim((string) file_get_contents("$fixtures_dir/session.paid.sig"));

assert_true(
    DigiPay_Webhook::verify_signature($body, str_replace('sha256=', '', $sig), $secret) === true,
    'bare-hex signature (no scheme prefix) must still verify'
) or $failed++;

assert_true(
    DigiPay_Webhook::verify_signature($body, 'md5=' . str_replace('sha256=', '', $sig), $secret) !== true,
    'unsupported scheme prefix must NOT verify'
) or $failed++;

assert_true(
    DigiPay_Webhook::verify_signature($body, 'sha256=ZZZZ', $secret) !== true,
    'malformed hex must NOT verify'
) or $failed++;

assert_true(
    DigiPay_Webhook::verify_signature($body, $sig, '') !== true,
    'empty secret must NOT verify (no-config guard)'
) or $failed++;

if ($failed > 0) {
    fwrite(STDERR, "FAIL: $failed assertion(s) failed.\n");
    exit(1);
}

echo "ok — all verification cases passed.\n";
exit(0);

function assert_true(bool $cond, string $label): bool
{
    if ($cond) {
        echo "  ✓ $label\n";
        return true;
    }
    fwrite(STDERR, "  ✗ $label\n");
    return false;
}
