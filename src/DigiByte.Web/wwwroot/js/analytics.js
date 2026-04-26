// Google Analytics bootstrap. Pulled out of index.html so the page CSP
// can drop 'unsafe-inline' from script-src — every executable script
// shipped to the browser now lives at a verifiable URL on this origin.
//
// gtag.js itself is still loaded from googletagmanager.com (third-party
// CDN). It does NOT carry an SRI hash because Google rotates that file
// without notice; pinning a hash would silently break analytics on every
// rotation. Treating GA as an acceptable but documented supply-chain
// risk — see docs/walletscrutiny-self-eval.md §2.2.
window.dataLayer = window.dataLayer || [];
function gtag() { dataLayer.push(arguments); }
gtag('js', new Date());
gtag('config', 'G-NR6R1MNL9X');
