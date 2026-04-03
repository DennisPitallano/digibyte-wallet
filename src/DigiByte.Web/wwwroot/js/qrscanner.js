// QR Scanner using browser camera API
// Lightweight implementation without external dependencies
window.qrScanner = {
    video: null,
    canvas: null,
    ctx: null,
    scanning: false,
    stream: null,

    async start(dotnetHelper) {
        try {
            // Request camera
            this.stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: 'environment', width: { ideal: 640 }, height: { ideal: 480 } }
            });

            this.video = document.getElementById('qr-video');
            if (!this.video) return false;

            this.video.srcObject = this.stream;
            await this.video.play();

            this.canvas = document.createElement('canvas');
            this.ctx = this.canvas.getContext('2d', { willReadFrequently: true });
            this.scanning = true;

            // Load the jsQR library dynamically
            if (!window.jsQR) {
                await new Promise((resolve, reject) => {
                    const script = document.createElement('script');
                    script.src = 'https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.min.js';
                    script.onload = resolve;
                    script.onerror = reject;
                    document.head.appendChild(script);
                });
            }

            this._scan(dotnetHelper);
            return true;
        } catch (err) {
            console.error('QR Scanner error:', err);
            dotnetHelper.invokeMethodAsync('OnScanError', err.message || 'Camera access denied');
            return false;
        }
    },

    _scan(dotnetHelper) {
        if (!this.scanning || !this.video) return;

        if (this.video.readyState === this.video.HAVE_ENOUGH_DATA) {
            this.canvas.width = this.video.videoWidth;
            this.canvas.height = this.video.videoHeight;
            this.ctx.drawImage(this.video, 0, 0, this.canvas.width, this.canvas.height);

            const imageData = this.ctx.getImageData(0, 0, this.canvas.width, this.canvas.height);
            const code = jsQR(imageData.data, imageData.width, imageData.height, {
                inversionAttempts: 'dontInvert'
            });

            if (code && code.data) {
                this.stop();
                dotnetHelper.invokeMethodAsync('OnScanResult', code.data);
                return;
            }
        }

        requestAnimationFrame(() => this._scan(dotnetHelper));
    },

    stop() {
        this.scanning = false;
        if (this.stream) {
            this.stream.getTracks().forEach(t => t.stop());
            this.stream = null;
        }
        if (this.video) {
            this.video.srcObject = null;
        }
    },

    isSupported() {
        return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    }
};
