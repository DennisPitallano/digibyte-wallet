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

            // Load the jsQR library dynamically (self-hosted)
            if (!window.jsQR) {
                await new Promise((resolve, reject) => {
                    const script = document.createElement('script');
                    script.src = 'js/jsqr.min.js';
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
    },

    // ----- Paste panel (specs/030-desktop-paste-scan) -----
    // Nothing here sends anything anywhere, stores anything, or logs a pasted or decoded value.

    // What the browser says about this device, read without any permission prompt (contracts/scanner-js.md).
    async deviceFacts() {
        const finePointer = !!(window.matchMedia && window.matchMedia('(pointer: fine)').matches);
        let hasCamera = null;
        try {
            if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
                const devices = await navigator.mediaDevices.enumerateDevices();
                // Before camera permission some browsers list nothing at all; "none listed" is then "unknown".
                hasCamera = devices.some(d => d.kind === 'videoinput') ? true : (devices.length > 0 ? false : null);
            }
        } catch (e) {
            hasCamera = null;
        }
        const clip = navigator.clipboard;
        return {
            finePointer,
            hasCamera,
            canCaptureScreen: !!(navigator.mediaDevices && navigator.mediaDevices.getDisplayMedia),
            canReadClipboardText: !!(clip && clip.readText),
            canReadClipboardImages: !!(clip && clip.read),
            isMac: /Mac|iPhone|iPad/.test((navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '')
        };
    },

    // Synchronous pointer check, so the menu's label is right on its first render.
    finePointer() {
        return !!(window.matchMedia && window.matchMedia('(pointer: fine)').matches);
    },

    // Reads the clipboard after the person pressed "Paste from clipboard". A picture is read for a QR code here
    // and only the decoded text is returned; picture bytes never leave this file.
    async readClipboard() {
        const clip = navigator.clipboard;
        if (!clip) return { kind: 'refused' };
        try {
            if (clip.read) {
                const items = await clip.read();
                for (const item of items) {
                    const imageType = item.types.find(t => t.startsWith('image/'));
                    if (imageType) {
                        const text = await this.decodeBlob(await item.getType(imageType));
                        if (text === 'notimage' || text === 'toolarge') return { kind: text };
                        return text ? { kind: 'code', text } : { kind: 'nocode' };
                    }
                }
                for (const item of items) {
                    if (item.types.includes('text/plain')) {
                        const text = await (await item.getType('text/plain')).text();
                        return text && text.trim() ? { kind: 'text', text } : { kind: 'empty' };
                    }
                }
                return { kind: 'empty' };
            }
            if (clip.readText) {
                const text = await clip.readText();
                return text && text.trim() ? { kind: 'text', text } : { kind: 'empty' };
            }
            return { kind: 'refused' };
        } catch (e) {
            // Permission refused, prompt dismissed, or not allowed here: the keyboard paste still works.
            return { kind: 'refused' };
        }
    },

    // Wires the paste panel: Ctrl+V / ⌘V anywhere in it (text or a picture), Enter in the field, drag and drop.
    // Text and decoded codes go to .NET; picture bytes stay here. Call detachPanel on close.
    attachPanel(panelEl, dotnetRef) {
        if (!panelEl || panelEl.__qrPanel) return;
        // The panel is the whole dialog, so the field may come and go as the view changes: look it up each time.
        const field = panelEl.querySelector('textarea');
        const send =(method, kind, text) => dotnetRef.invokeMethodAsync(method, kind, text == null ? null : String(text));
        const handlers = {
            opener: document.activeElement,
            paste: async (e) => {
                const data = e.clipboardData;
                if (!data) return;
                const image = Array.prototype.find.call(data.items || [], i => i.kind === 'file' && i.type.startsWith('image/'));
                if (image) {
                    e.preventDefault();
                    const file = image.getAsFile();
                    send('OnPanelPaste', 'decoding', null);
                    const result = await this.decodeBlob(file);
                    if (result === 'notimage' || result === 'toolarge') send('OnPanelPaste', result, null);
                    else send('OnPanelPaste', result ? 'code' : 'nocode', result);
                    return;
                }
                const text = data.getData('text/plain');
                // A pasted text is handled at once; it never sits in the field first (FR-008, FR-027).
                e.preventDefault();
                send('OnPanelPaste', text && text.trim() ? 'text' : 'empty', text);
            },
            keydown: (e) => {
                if (e.target && e.target.tagName === 'TEXTAREA' && e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
                    e.preventDefault();
                    send('OnPanelPaste', 'typed', e.target.value);
                } else if (e.key === 'Escape') {
                    e.preventDefault();
                    dotnetRef.invokeMethodAsync('OnEscape');
                }
            },
            dragenter: (e) => { if (this._hasFiles(e)) { e.preventDefault(); dotnetRef.invokeMethodAsync('OnDragState', true); } },
            dragover: (e) => { if (this._hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } },
            dragleave: (e) => { if (!panelEl.contains(e.relatedTarget)) dotnetRef.invokeMethodAsync('OnDragState', false); },
            drop: async (e) => {
                if (!this._hasFiles(e)) return;
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnDragState', false);
                const file = e.dataTransfer.files[0];
                send('OnPanelDrop', 'decoding', null);
                const result = await this.decodeBlob(file);
                if (result === 'notimage' || result === 'toolarge') send('OnPanelDrop', result, null);
                else send('OnPanelDrop', result ? 'code' : 'nocode', result);
            }
        };
        panelEl.__qrPanel = handlers;
        panelEl.addEventListener('paste', handlers.paste);
        panelEl.addEventListener('keydown', handlers.keydown);
        for (const name of ['dragenter', 'dragover', 'dragleave', 'drop']) panelEl.addEventListener(name, handlers[name]);
        if (field) field.focus();
    },

    detachPanel(panelEl) {
        const handlers = panelEl && panelEl.__qrPanel;
        if (!handlers) return;
        panelEl.removeEventListener('paste', handlers.paste);
        panelEl.removeEventListener('keydown', handlers.keydown);
        for (const name of ['dragenter', 'dragover', 'dragleave', 'drop']) panelEl.removeEventListener(name, handlers[name]);
        panelEl.__qrPanel = null;
        // Focus goes back to what opened the panel, if it is still there (FR-029).
        try { if (handlers.opener && document.contains(handlers.opener)) handlers.opener.focus(); } catch (e) { }
    },

    // Empties the field in the page itself, so a cleared secret is gone from the DOM at once.
    clearField(panelEl) {
        const field = panelEl && panelEl.querySelector('textarea');
        if (field) field.value = '';
    },

    // Says something to screen readers from a live region that outlives the panel, which closes at once (FR-029).
    announce(text) {
        let region = document.getElementById('qr-announcer');
        if (!region) {
            region = document.createElement('div');
            region.id = 'qr-announcer';
            region.className = 'sr-only';
            region.setAttribute('role', 'status');
            region.setAttribute('aria-live', 'polite');
            document.body.appendChild(region);
        }
        region.textContent = '';
        setTimeout(() => { region.textContent = text; }, 50);
    },

    focusField(panelEl) {
        const field = panelEl && panelEl.querySelector('textarea');
        if (field) field.focus();
    },

    _hasFiles(e) {
        return !!(e.dataTransfer && Array.prototype.includes.call(e.dataTransfer.types || [], 'Files'));
    },

    // ----- Scan from screen (FR-015 to FR-018) -----
    // The person picks a tab or window in the browser's own prompt. Frames are read here, a few times a second,
    // and never kept or sent anywhere. Sharing stops as soon as a code is found, on Stop or close, when the browser's
    // own "Stop sharing" is used, or after a minute.

    screen: null,

    // True once limitMs has passed since startMs. Pure; covered by qrscanner.test.mjs.
    _giveUpClock(startMs, nowMs, limitMs = 60000) {
        return nowMs - startMs >= limitMs;
    },

    // Bumped by every stop, so a share the person grants after the panel was closed or stopped is ended at once.
    _screenGen: 0,

    async startScreen(dotnetRef) {
        const media = navigator.mediaDevices;
        if (!media || !media.getDisplayMedia) return 'unsupported';
        this.stopScreen();
        const gen = this._screenGen;

        let stream;
        try {
            stream = await media.getDisplayMedia({ video: { frameRate: 5 }, audio: false });
        } catch (e) {
            // Cancelled or refused in the browser's prompt: not an error for the person.
            return 'cancelled';
        }
        const stale = () => {
            if (gen === this._screenGen) return false;
            stream.getTracks().forEach(track => track.stop());
            return true;
        };
        if (stale()) return 'cancelled';

        await this._ensureJsQr();
        if (stale()) return 'cancelled';

        const video = document.createElement('video');
        video.muted = true;
        video.playsInline = true;
        video.srcObject = stream;
        // Not awaited: a screen that sends no new frame would hold this forever. The sampler waits for frames.
        const playing = video.play();
        if (playing && playing.catch) playing.catch(() => { });

        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        const state = { stream, video, timer: null, done: false };
        this.screen = state;
        const started = Date.now();

        const finish = (method, arg) => {
            if (state.done) return;
            this.stopScreen();
            dotnetRef.invokeMethodAsync(method, arg);
        };

        stream.getVideoTracks().forEach(track => track.addEventListener('ended', () => finish('OnScreenEnded', 'ended')));

        state.timer = setInterval(() => {
            if (state.done) return;
            if (this._giveUpClock(started, Date.now())) { finish('OnScreenEnded', 'timeout'); return; }
            if (video.readyState < 2 || !video.videoWidth) return;
            const size = this._fitWithin(video.videoWidth, video.videoHeight, 1920);
            canvas.width = size.w;
            canvas.height = size.h;
            ctx.drawImage(video, 0, 0, size.w, size.h);
            const frame = ctx.getImageData(0, 0, size.w, size.h);
            const code = jsQR(frame.data, frame.width, frame.height, { inversionAttempts: 'attemptBoth' });
            if (code && code.data) finish('OnScreenResult', code.data);
        }, 250);

        return 'started';
    },

    // Idempotent: leaves no live screen track behind.
    stopScreen() {
        this._screenGen++;
        const state = this.screen;
        if (!state) return;
        state.done = true;
        this.screen = null;
        if (state.timer) clearInterval(state.timer);
        try { state.stream.getTracks().forEach(track => track.stop()); } catch (e) { }
        try { state.video.srcObject = null; } catch (e) { }
    },

    // Largest picture accepted for reading, the same limit Upload image has always had.
    MAX_IMAGE_BYTES: 10 * 1024 * 1024,

    // Scales (w, h) down so the longer side is at most max, keeping the shape. Pure; covered by qrscanner.test.mjs.
    _fitWithin(w, h, max) {
        if (!(w > 0) || !(h > 0) || !(max > 0)) return { w: 0, h: 0 };
        const longer = Math.max(w, h);
        if (longer <= max) return { w, h };
        const scale = max / longer;
        return { w: Math.round(w * scale), h: Math.round(h * scale) };
    },

    async _ensureJsQr() {
        if (window.jsQR) return;
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'js/jsqr.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    },

    // Reads a QR code from a picture (pasted, dropped or chosen). Returns the text, null when there is no code,
    // or 'notimage' / 'toolarge' when the file is refused before reading.
    async decodeBlob(blob) {
        if (!blob || !blob.type || !blob.type.startsWith('image/')) return 'notimage';
        if (blob.size > this.MAX_IMAGE_BYTES) return 'toolarge';
        await this._ensureJsQr();
        let bitmap;
        try {
            bitmap = await createImageBitmap(blob);
        } catch (e) {
            return null;
        }
        const size = this._fitWithin(bitmap.width, bitmap.height, 2048);
        const canvas = document.createElement('canvas');
        canvas.width = size.w;
        canvas.height = size.h;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(bitmap, 0, 0, size.w, size.h);
        if (bitmap.close) bitmap.close();
        const imageData = ctx.getImageData(0, 0, size.w, size.h);
        const code = jsQR(imageData.data, imageData.width, imageData.height, { inversionAttempts: 'attemptBoth' });
        return code && code.data ? code.data : null;
    },

    // Reads the file chosen in an <input type=file> without sending its bytes through .NET.
    async decodeInputFile(inputEl) {
        const file = inputEl && inputEl.files && inputEl.files[0];
        if (!file) return null;
        try {
            return await this.decodeBlob(file);
        } finally {
            inputEl.value = '';   // choosing the same file again fires a change again
        }
    }

};
