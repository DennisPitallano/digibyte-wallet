import { DigiPay } from '@dgbwallet/digipay';

// Run with:  DIGIPAY_KEY=dgp_… npx tsx examples/create-session.ts

const dp = new DigiPay({ apiKey: process.env.DIGIPAY_KEY! });

const session = await dp.sessions.create({
    amount: 5,
    label: 'Order #1234',
    memo: 'Customer: alice@example.com',
});

console.log('Session ID:    ', session.id);
console.log('Amount:        ', session.amount, 'DGB');
console.log('Address:       ', session.address);
console.log('Expires:       ', session.expiresAt);
console.log('BIP21 URI:     ', session.uri);
console.log('Hosted page:   ', session.checkoutUrl);
