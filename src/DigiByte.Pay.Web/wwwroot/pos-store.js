// Tablet-side state for the DigiPay POS kiosk (/pos).
//
// Scope: localStorage persistence for the paired device (API key + store) plus
// a couple of "kiosk nicety" helpers (confirmation beep, placeholder for
// future fullscreen / wake-lock hooks). Intentionally tiny and standalone —
// the Razor page calls into this via IJSRuntime, so the kiosk keeps working
// even across a Blazor circuit reconnect.
//
// Storage key is versioned (:v1) so we can evolve the shape later without
// stranding already-paired devices on a broken payload.
(function () {
    const KEY = 'digipay.pos.device:v1';
    const PIN_KEY = 'digipay.pos.pin:v1';
    const CURRENCY_KEY = 'digipay.pos.currency:v1';
    const SHIFT_KEY = 'digipay.pos.shiftStartedAt:v1';

    // Manager PIN helpers. PIN is never stored raw — we hash it with SHA-256
    // over a per-device random salt so a leaked localStorage dump doesn't
    // immediately reveal the digits. 4–6 digits is the realistic threat model
    // here (shoulder-surfing a cashier), not offline brute force.
    async function sha256Hex(input) {
        const bytes = new TextEncoder().encode(input);
        const digest = await crypto.subtle.digest('SHA-256', bytes);
        return Array.from(new Uint8Array(digest))
            .map(b => b.toString(16).padStart(2, '0')).join('');
    }
    function randomSalt() {
        const bytes = new Uint8Array(16);
        crypto.getRandomValues(bytes);
        return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
    }

    window.digipayPos = {
        save: function (device) {
            try { localStorage.setItem(KEY, JSON.stringify(device)); } catch { }
        },
        load: function () {
            try {
                const raw = localStorage.getItem(KEY);
                return raw ? JSON.parse(raw) : null;
            } catch { return null; }
        },
        clear: function () {
            // Only the paired-device record is cleared here — a manager PIN
            // intentionally persists across re-pairings so staff can't sidestep
            // the lock by unpairing and re-pairing.
            try { localStorage.removeItem(KEY); } catch { }
        },

        // Is a PIN configured on this device?
        hasPin: function () {
            try { return !!localStorage.getItem(PIN_KEY); } catch { return false; }
        },
        // Store / overwrite the PIN. Caller is responsible for confirming the
        // value twice in the UI (we don't do a second-field check here).
        setPin: async function (pin) {
            if (!pin || String(pin).length < 4) return false;
            const salt = randomSalt();
            const hash = await sha256Hex(salt + ':' + pin);
            try {
                localStorage.setItem(PIN_KEY, JSON.stringify({ salt, hash, v: 1 }));
                return true;
            } catch { return false; }
        },
        verifyPin: async function (pin) {
            try {
                const raw = localStorage.getItem(PIN_KEY);
                if (!raw) return true; // no PIN set == allow
                const { salt, hash } = JSON.parse(raw);
                return (await sha256Hex(salt + ':' + pin)) === hash;
            } catch { return false; }
        },
        clearPin: function () {
            try { localStorage.removeItem(PIN_KEY); } catch { }
        },

        // Remember the last-selected currency on this tablet so a JPY till
        // stays on JPY after a reload. Stored as a raw string code ("DGB",
        // "EUR", etc.) — no validation here; the Blazor side checks it
        // against its SupportedCurrencies list.
        saveCurrency: function (code) {
            try { localStorage.setItem(CURRENCY_KEY, String(code)); } catch { }
        },
        loadCurrency: function () {
            try { return localStorage.getItem(CURRENCY_KEY); } catch { return null; }
        },

        // "Shift started at" marker — set when the cashier hits "Start new
        // shift" from the close-shift modal. Used by the shift-report fetch
        // so the numbers reflect this till's current shift, not the whole
        // UTC day. Stored as an ISO-8601 string; null → report falls back
        // to start-of-UTC-day server-side.
        saveShiftStart: function (iso) {
            try { localStorage.setItem(SHIFT_KEY, String(iso)); } catch { }
        },
        loadShiftStart: function () {
            try { return localStorage.getItem(SHIFT_KEY); } catch { return null; }
        },

        // Short confirmation chirp on successful payment. WebAudio so we don't
        // ship an audio file; gracefully no-ops if the browser blocks it
        // (e.g. autoplay policy — but at this point the cashier has interacted
        // with the page so it should go through).
        beep: function () {
            try {
                const ctx = new (window.AudioContext || window.webkitAudioContext)();
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(880, ctx.currentTime);
                osc.frequency.setValueAtTime(1320, ctx.currentTime + 0.12);
                gain.gain.setValueAtTime(0.0001, ctx.currentTime);
                gain.gain.exponentialRampToValueAtTime(0.2, ctx.currentTime + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.35);
                osc.connect(gain).connect(ctx.destination);
                osc.start();
                osc.stop(ctx.currentTime + 0.36);
                osc.onended = () => ctx.close();
            } catch { /* ignore — audio unavailable */ }
        },
    };
})();
