// Sponsors form helper: shrink the picked picture in the browser before it is sent.
// Nothing here touches wallet data. The picture is resized to at most 256x256 and re-encoded as WebP; browsers that cannot encode
// WebP (notably Safari on iPhone) hand back PNG for that request, so the helper then falls back to JPEG, which every browser encodes.
// The server checks the type and size again; see specs/006-sponsors-page/research.md (R9).
window.sponsorsImage = (function () {
    var MAX_SIDE = 256;
    var MAX_BYTES = 200 * 1024;

    function toBlob(canvas, type, quality) {
        return new Promise(function (resolve) {
            try { canvas.toBlob(function (b) { resolve(b); }, type, quality); }
            catch (e) { resolve(null); }
        });
    }

    function draw(bitmap, w, h, whiteBackground) {
        var canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        var ctx = canvas.getContext('2d');
        if (whiteBackground) { ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, w, h); }
        ctx.drawImage(bitmap, 0, 0, w, h);
        return canvas;
    }

    function toBase64(bytes) {
        var chunk = 0x8000, binary = '';
        for (var i = 0; i < bytes.length; i += chunk)
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        return btoa(binary);
    }

    return {
        // Reads the first file of an <input type="file"> and returns { base64, contentType, previewUrl } or { error: 'type' | 'size' | 'read' }.
        prepare: async function (input) {
            var file = input && input.files && input.files[0];
            if (!file) return null;
            if (!/^image\/(png|jpeg|webp)$/.test(file.type)) return { error: 'type' };
            try {
                var bitmap = await createImageBitmap(file);
                var scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
                var w = Math.max(1, Math.round(bitmap.width * scale));
                var h = Math.max(1, Math.round(bitmap.height * scale));

                var blob = await toBlob(draw(bitmap, w, h, false), 'image/webp', 0.85);
                if (!blob || blob.type !== 'image/webp')
                    blob = await toBlob(draw(bitmap, w, h, true), 'image/jpeg', 0.85);
                if (bitmap.close) bitmap.close();
                if (!blob || blob.size === 0 || blob.size > MAX_BYTES) return { error: 'size' };

                var bytes = new Uint8Array(await blob.arrayBuffer());
                return { base64: toBase64(bytes), contentType: blob.type, previewUrl: URL.createObjectURL(blob) };
            } catch (e) {
                return { error: 'read' };
            }
        },
        revoke: function (url) {
            try { if (url) URL.revokeObjectURL(url); } catch (e) { }
        }
    };
})();
