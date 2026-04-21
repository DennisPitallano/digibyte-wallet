// Small browser-side download helper. Blazor can hand us a base64-encoded
// blob (cheap, no JS-interop streaming needed) and we turn it into a real
// file download via Blob + ObjectURL — the standard idiom that works in
// every modern browser without prompting for a save dialog from the user.
window.digipayDownloadBlob = function (filename, mimeType, base64) {
    const bin = atob(base64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);

    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    // Defer cleanup so the browser has a tick to start the download.
    setTimeout(function () {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }, 0);
};
