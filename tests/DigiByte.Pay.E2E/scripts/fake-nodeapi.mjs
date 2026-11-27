// A stand-in for NodeApi's DigiDollar routes and the price feeds, for the end-to-end run (specs/033 T070).
// Pay.Api is pointed at it with --DdNode:BaseUrl and --DigiPay:Prices:*, so the journeys never need a real
// DigiDollar node or the internet. Every DD address it hands out is new; no DD ever arrives.
//
//   node scripts/fake-nodeapi.mjs <port>
import { createServer } from 'node:http';

const port = Number(process.argv[2] || process.env.E2E_NODE_PORT || 5262);
let counter = 0;

const json = (res, status, body) => {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
};

createServer((req, res) => {
    const url = new URL(req.url, `http://localhost:${port}`);
    const path = url.pathname;

    if (path === '/health') return json(res, 200, { ok: true });

    // NodeApi /api/digidollar/*
    if (path === '/api/digidollar/health')
        return json(res, 200, { enabled: true, deployment_active: true, dgb_fee_reserve_dgb: 500, reserve_healthy: true });
    if (path === '/api/digidollar/address/new' && req.method === 'POST') {
        counter += 1;
        return json(res, 200, { address: `TDe2efake${String(counter).padStart(6, '0')}` });
    }
    if (path.startsWith('/api/digidollar/balance')) return json(res, 200, { confirmed: 0, unconfirmed: 0, total: 0 });
    if (path === '/api/digidollar/transactions') return json(res, 200, []);
    if (path === '/api/digidollar/sends') return json(res, 200, { items: [] });
    if (path.startsWith('/api/digidollar/tx/')) return json(res, 200, { status: 'unknown', confirmations: 0 });

    // Price feeds: CoinGecko's simple price (1 DGB = $0.01) and the USD exchange-rate table.
    if (path === '/coingecko') {
        const cur = (url.searchParams.get('vs_currencies') || 'usd').toLowerCase();
        const perUsd = { usd: 1, eur: 0.92, php: 58.2 }[cur] ?? 1;
        return json(res, 200, { digibyte: { [cur]: 0.01 * perUsd } });
    }
    if (path === '/fx') return json(res, 200, { result: 'success', base_code: 'USD', rates: { USD: 1, EUR: 0.92, PHP: 58.2 } });

    json(res, 404, { error: 'not found' });
}).listen(port, () => console.log(`fake NodeApi on ${port}`));
