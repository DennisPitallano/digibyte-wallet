// Smoke tests for the webhook signature verifier. Pure-Node test runner
// (no Jest/Vitest dependency) — keeps the SDK install footprint zero.
//
// Run after `npm run build`:
//     node --test tests/webhook.test.js

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { verifyWebhook, DigiPayError } from '../dist/esm/index.js';

const SECRET = 'test-secret-123';
const PAYLOAD = JSON.stringify({
    event: 'session.paid',
    timestamp: '2026-04-21T00:00:00Z',
    session: { id: 'ses_abc', amount: 5 },
});

function sign(body, secret = SECRET) {
    return 'sha256=' + createHmac('sha256', secret).update(body).digest('hex');
}

test('verifies a correctly signed payload', () => {
    const event = verifyWebhook({
        rawBody: PAYLOAD,
        signature: sign(PAYLOAD),
        secret: SECRET,
    });
    assert.equal(event.event, 'session.paid');
    assert.equal(event.session.id, 'ses_abc');
});

test('accepts Buffer rawBody as well as string', () => {
    const event = verifyWebhook({
        rawBody: Buffer.from(PAYLOAD, 'utf8'),
        signature: sign(PAYLOAD),
        secret: SECRET,
    });
    assert.equal(event.event, 'session.paid');
});

test('tolerates a missing sha256= prefix', () => {
    const sig = sign(PAYLOAD).replace('sha256=', '');
    const event = verifyWebhook({ rawBody: PAYLOAD, signature: sig, secret: SECRET });
    assert.equal(event.event, 'session.paid');
});

test('rejects a wrong signature', () => {
    assert.throws(
        () => verifyWebhook({ rawBody: PAYLOAD, signature: sign(PAYLOAD, 'wrong-secret'), secret: SECRET }),
        (err) => err instanceof DigiPayError && err.status === 401,
    );
});

test('rejects a missing signature header', () => {
    assert.throws(
        () => verifyWebhook({ rawBody: PAYLOAD, signature: undefined, secret: SECRET }),
        (err) => err instanceof DigiPayError && err.status === 401,
    );
});

test('rejects a tampered body even with a valid-looking signature', () => {
    const tampered = PAYLOAD.replace('5', '50000');
    assert.throws(
        () => verifyWebhook({ rawBody: tampered, signature: sign(PAYLOAD), secret: SECRET }),
        (err) => err instanceof DigiPayError && err.status === 401,
    );
});

test('rejects junk JSON after a valid signature', () => {
    const bad = '{not json';
    assert.throws(
        () => verifyWebhook({ rawBody: bad, signature: sign(bad), secret: SECRET }),
        (err) => err instanceof DigiPayError && err.status === 400,
    );
});
