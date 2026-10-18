function downloadDataUrl(dataUrl, filename) {
    const qrImg = new Image();
    qrImg.onload = function () {
        const size = qrImg.width;
        const canvas = document.createElement('canvas');
        canvas.width = size;
        canvas.height = size;
        const ctx = canvas.getContext('2d');

        // Draw QR code
        ctx.drawImage(qrImg, 0, 0);

        // Draw white circle + DGB logo in center
        const logoSize = Math.round(size * 0.18);
        const padding = Math.round(logoSize * 0.15);
        const circleRadius = (logoSize + padding * 2) / 2;
        const cx = size / 2;
        const cy = size / 2;

        ctx.beginPath();
        ctx.arc(cx, cy, circleRadius, 0, Math.PI * 2);
        ctx.fillStyle = '#ffffff';
        ctx.fill();

        // Draw the DGB logo SVG
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 479.8 479.8" width="${logoSize}" height="${logoSize}">` +
            `<circle cx="239.9" cy="239.9" r="219.5" fill="#002352" stroke="#06c" stroke-miterlimit="10" stroke-width="40.8"/>` +
            `<path fill="#fff" d="M116.9 182.9h155s53.5-.5 16.5 68.5c0 0-27 54-94 53l36.1-92.2a7.3 7.3 0 0 0-6.6-10l-47.5-.8-60 146s20 2 31 1l-6 15h28.3a3.8 3.8 0 0 0 3.6-2.5l5.1-13.5 12-1-7 17H211a5 5 0 0 0 4.7-3.2l7.7-19.8s84-14 122-82c0 0 51-79-9-107a116 116 0 0 0-37-10l7-17.3a3.4 3.4 0 0 0-3.1-4.7h-25.9l-8 21h-11l6.2-16.3a3.5 3.5 0 0 0-3.2-4.7h-26l-8 21h-80.8a12 12 0 0 0-10.6 6.3z"/>` +
            `</svg>`;
        const blob = new Blob([svg], { type: 'image/svg+xml;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const logoImg = new Image();
        logoImg.onload = function () {
            ctx.drawImage(logoImg, cx - logoSize / 2, cy - logoSize / 2, logoSize, logoSize);
            URL.revokeObjectURL(url);

            // Trigger download
            const a = document.createElement('a');
            a.href = canvas.toDataURL('image/png');
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        };
        logoImg.src = url;
    };
    qrImg.src = dataUrl;
}

function downloadTextFile(content, filename) {
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

// ---- Branded QR card (DigiDollar receive): QR + product logo + title + address, as a shareable PNG ----

function loadImage(src) {
    return new Promise(function (resolve, reject) {
        const img = new Image();
        img.onload = function () { resolve(img); };
        img.onerror = reject;
        img.src = src;
    });
}

function drawCircleImage(ctx, img, x, y, size) {
    ctx.save();
    ctx.beginPath();
    ctx.arc(x + size / 2, y + size / 2, size / 2, 0, Math.PI * 2);
    ctx.clip();
    ctx.drawImage(img, x, y, size, size);
    ctx.restore();
}

// Renders the card to a PNG Blob. qrDataUrl is the plain QR; caption is the address, title the product name.
async function renderQrCard(qrDataUrl, logoUrl, title, subtitle, caption) {
    const qr = await loadImage(qrDataUrl);
    let logo = null;
    try { logo = await loadImage(logoUrl); } catch (e) { /* the card is still useful without the mark */ }

    const size = qr.width;
    const pad = Math.round(size * 0.09);
    const headerH = Math.round(size * 0.16);
    const lineH = Math.round(size * 0.065);
    const half = Math.ceil(caption.length / 2);
    const lines = caption.length > 30 ? [caption.slice(0, half), caption.slice(half)] : [caption];
    const footerH = Math.round(size * 0.07) + lines.length * lineH + Math.round(size * 0.04);

    const w = size + pad * 2;
    const h = headerH + size + pad + footerH;
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');

    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, w, h);

    // Header: logo + title (+ network)
    const logoSize = Math.round(headerH * 0.62);
    const hy = Math.round(headerH / 2);
    let tx = pad;
    if (logo) {
        drawCircleImage(ctx, logo, pad, hy - logoSize / 2, logoSize);
        tx += logoSize + Math.round(pad * 0.4);
    }
    ctx.fillStyle = '#002352';
    ctx.textBaseline = 'middle';
    ctx.font = '700 ' + Math.round(headerH * 0.36) + 'px Inter, system-ui, sans-serif';
    ctx.fillText(title, tx, hy - (subtitle ? Math.round(headerH * 0.1) : 0));
    if (subtitle) {
        ctx.fillStyle = '#64748b';
        ctx.font = '500 ' + Math.round(headerH * 0.2) + 'px Inter, system-ui, sans-serif';
        ctx.fillText(subtitle, tx, hy + Math.round(headerH * 0.24));
    }

    // QR with a rounded border
    const qy = headerH;
    ctx.strokeStyle = '#e2e8f0';
    ctx.lineWidth = Math.max(2, Math.round(size * 0.006));
    ctx.strokeRect(pad - 2, qy - 2, size + 4, size + 4);
    ctx.drawImage(qr, pad, qy, size, size);

    // Product mark in the centre of the QR (the code uses error correction level H)
    if (logo) {
        const mark = Math.round(size * 0.18);
        const ring = Math.round(mark * 0.62);
        const cx = pad + size / 2, cy = qy + size / 2;
        ctx.beginPath();
        ctx.arc(cx, cy, ring, 0, Math.PI * 2);
        ctx.fillStyle = '#ffffff';
        ctx.fill();
        drawCircleImage(ctx, logo, cx - mark / 2, cy - mark / 2, mark);
    }

    // Address, monospaced, centred
    ctx.fillStyle = '#334155';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.font = '500 ' + Math.round(lineH * 0.78) + 'px ui-monospace, Menlo, Consolas, monospace';
    let y = qy + size + Math.round(size * 0.07);
    lines.forEach(function (l) { ctx.fillText(l, w / 2, y); y += lineH; });

    return new Promise(function (resolve) { canvas.toBlob(resolve, 'image/png'); });
}

async function downloadQrCard(qrDataUrl, filename, logoUrl, title, subtitle, caption) {
    const blob = await renderQrCard(qrDataUrl, logoUrl, title, subtitle, caption);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
}

// Shares the card as an image when the platform can share files, otherwise the address as text.
// Returns "shared", "cancelled" or "unsupported" (the caller then falls back to copying the address).
async function shareQrCard(qrDataUrl, filename, logoUrl, title, subtitle, caption, shareTitle, shareText) {
    if (!navigator.share) return 'unsupported';
    try {
        const blob = await renderQrCard(qrDataUrl, logoUrl, title, subtitle, caption);
        const file = new File([blob], filename, { type: 'image/png' });
        if (navigator.canShare && navigator.canShare({ files: [file] })) {
            await navigator.share({ files: [file], title: shareTitle, text: shareText });
        } else {
            await navigator.share({ title: shareTitle, text: shareText });
        }
        return 'shared';
    } catch (e) {
        return e && e.name === 'AbortError' ? 'cancelled' : 'unsupported';
    }
}
