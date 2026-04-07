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
