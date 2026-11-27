// A stand-in for the public Esplora explorer's DigiDollar data and the price feeds, for the end-to-end run
// (specs/033 T070, specs/035 T033). Pay.Api is pointed at it with --DigiPay:DigiDollar:Esplora*Url and
// --DigiPay:Prices:*, so the journeys never need the real explorer or the internet. DigiDollar is reported active
// and no DD ever arrives: every address has no transactions.
//
//   node scripts/fake-esplora.mjs <port>
import { createServer } from 'node:http';

const port = Number(process.argv[2] || process.env.E2E_NODE_PORT || 5262);

const json = (res, status, body) => {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
};
const text = (res, status, body) => {
    res.writeHead(status, { 'Content-Type': 'text/plain' });
    res.end(body);
};

createServer((req, res) => {
    const url = new URL(req.url, `http://localhost:${port}`);
    const path = url.pathname;

    if (path === '/health') return json(res, 200, { ok: true });

    // Esplora, mainnet at /api and testnet at /testnet/api (the paths EsploraDigiDollarApi calls).
    const esplora = path.match(/^(?:\/testnet)?\/api(\/.*)$/);
    if (esplora) {
        const rest = esplora[1];
        if (rest === '/blocks/tip/height') return text(res, 200, '30000000'); // past the mainnet DD activation height
        if (/^\/address\/[^/]+\/txs/.test(rest)) return json(res, 200, []);
        if (/^\/address\/[^/]+\/utxo$/.test(rest)) return json(res, 200, []);
        if (/^\/tx\//.test(rest)) return json(res, 404, { error: 'not found' });
    }

    // Price feeds: CoinGecko's simple price (1 DGB = $0.01) and the USD exchange-rate table.
    if (path === '/coingecko') {
        const cur = (url.searchParams.get('vs_currencies') || 'usd').toLowerCase();
        const perUsd = { usd: 1, eur: 0.92, php: 58.2 }[cur] ?? 1;
        return json(res, 200, { digibyte: { [cur]: 0.01 * perUsd } });
    }
    if (path === '/fx') return json(res, 200, { result: 'success', base_code: 'USD', rates: { USD: 1, EUR: 0.92, PHP: 58.2 } });

    json(res, 404, { error: 'not found' });
}).listen(port, () => console.log(`fake Esplora on ${port}`));
