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
    const PREFS_KEY = 'digipay.pos.prefs:v1';
    const RECENT_KEY = 'digipay.pos.recentCurrencies:v1';
    const MAX_RECENT = 4;

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
        // against the wallet's currency list (PosEntry.NormalizeCurrency).
        saveCurrency: function (code) {
            try { localStorage.setItem(CURRENCY_KEY, String(code)); } catch { }
        },
        loadCurrency: function () {
            try { return localStorage.getItem(CURRENCY_KEY); } catch { return null; }
        },

        // Display settings for this tablet (layout, keypad order and side, customer display, haptics,
        // "00" key, quick amounts). Stored as one JSON object; the Blazor side validates every field
        // (PosPrefs.Normalize), so anything that isn't a plain object reads back as null.
        savePrefs: function (prefs) {
            try { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs || {})); } catch { }
        },
        loadPrefs: function () {
            try {
                const raw = localStorage.getItem(PREFS_KEY);
                if (!raw) return null;
                const value = JSON.parse(raw);
                return value && typeof value === 'object' && !Array.isArray(value) ? value : null;
            } catch { return null; }
        },

        // The till currencies used most recently on this tablet (most recent first), shown as
        // "Recent" in the currency picker. Only strings are kept, upper-cased, at most four; the
        // Blazor side still checks each against the wallet's currency list.
        saveRecent: function (codes) {
            try { localStorage.setItem(RECENT_KEY, JSON.stringify(cleanRecent(codes))); } catch { }
        },
        loadRecent: function () {
            try {
                const raw = localStorage.getItem(RECENT_KEY);
                return raw ? cleanRecent(JSON.parse(raw)) : [];
            } catch { return []; }
        },

        // Moves focus into an element once the current render has settled (used for the currency
        // picker's search field, which dialog.js would otherwise skip for the close button).
        focus: function (selector) {
            setTimeout(function () {
                try {
                    const el = document.querySelector(selector);
                    if (el && el.focus) el.focus();
                } catch { }
            }, 30);
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

        // Hardware keyboard on the keypad: digits, "." / ",", Backspace, Delete (clear) and Enter (charge).
        // Ignored while a text field, select or dialog has focus, with modifier keys held, or
        // when Enter is pressed on a focused button (the button handles that itself). The .NET
        // side also checks that the keypad screen is showing.
        bindKeys: function (dotnetRef) {
            window.digipayPos.unbindKeys();
            keyHandler = function (e) {
                if (!dotnetRef || e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return;
                if (!isKeypadKey(e.key)) return;
                const active = document.activeElement;
                if (active) {
                    const tag = (active.tagName || '').toLowerCase();
                    if (tag === 'input' || tag === 'textarea' || tag === 'select' || active.isContentEditable) return;
                    if (active.closest && active.closest('[role="dialog"]')) return;
                    if (e.key === 'Enter' && (tag === 'button' || tag === 'a')) return;
                }
                if (document.querySelector('[role="dialog"][aria-modal="true"]')) return;
                if (e.key === 'Backspace' || e.key === 'Enter') e.preventDefault();
                dotnetRef.invokeMethodAsync('OnKeypadKey', e.key).catch(function () { });
            };
            document.addEventListener('keydown', keyHandler);

            // Haptic tick: a short vibration when a keypad key ([data-pos-haptic]) is pressed and
            // the merchant turned it on (data-haptics="on" on the POS root). Done here rather than
            // through the server so it lands with the touch.
            pointerHandler = function (e) {
                const target = e.target;
                const key = target && target.closest ? target.closest('[data-pos-haptic]') : null;
                if (!key || key.disabled) return;
                const root = key.closest('.dp-pos');
                if (!root || root.getAttribute('data-haptics') !== 'on') return;
                const nav = window.navigator;
                if (nav && typeof nav.vibrate === 'function') {
                    try { nav.vibrate(8); } catch { }
                }
            };
            document.addEventListener('pointerdown', pointerHandler);

            // Connection: tell the till when the browser goes offline or comes back.
            onlineHandler = function () { dotnetRef.invokeMethodAsync('OnNetworkChanged', true).catch(function () { }); };
            offlineHandler = function () { dotnetRef.invokeMethodAsync('OnNetworkChanged', false).catch(function () { }); };
            if (window.addEventListener) {
                window.addEventListener('online', onlineHandler);
                window.addEventListener('offline', offlineHandler);
            }
        },
        unbindKeys: function () {
            if (keyHandler) document.removeEventListener('keydown', keyHandler);
            if (pointerHandler) document.removeEventListener('pointerdown', pointerHandler);
            if (window.removeEventListener) {
                if (onlineHandler) window.removeEventListener('online', onlineHandler);
                if (offlineHandler) window.removeEventListener('offline', offlineHandler);
            }
            keyHandler = pointerHandler = onlineHandler = offlineHandler = null;
        },
    };

    let keyHandler = null;
    let pointerHandler = null;
    let onlineHandler = null;
    let offlineHandler = null;

    function cleanRecent(codes) {
        if (!Array.isArray(codes)) return [];
        const out = [];
        for (const c of codes) {
            if (typeof c !== 'string') continue;
            const code = c.trim().toUpperCase();
            if (!code || code.length > 5 || out.indexOf(code) >= 0) continue;
            out.push(code);
            if (out.length === MAX_RECENT) break;
        }
        return out;
    }

    function isKeypadKey(key) {
        return key === 'Enter' || key === 'Backspace' || key === 'Delete' || key === '.' || key === ','
            || (typeof key === 'string' && key.length === 1 && key >= '0' && key <= '9');
    }

    // ---------------------------------------------------------------------
    // New versions. A till is often left open for days, so after an update it would keep running the page it
    // loaded. Every few minutes the till asks the server which build it runs (/api/build); when that differs
    // from the build this page came from (<meta name="digipay-build">), it reloads, but only while the till is
    // idle (the POS root's data-idle="true": keypad, no amount, no dialog, no sale), so no sale is interrupted.
    // ---------------------------------------------------------------------
    const BUILD_CHECK_MS = 5 * 60 * 1000;
    let newBuildWaiting = false;

    async function checkBuild() {
        const meta = document.querySelector('meta[name="digipay-build"]');
        if (!meta || !meta.content) return;
        if (!newBuildWaiting) {
            try {
                const r = await fetch('/api/build', { cache: 'no-store' });
                if (!r.ok) return;
                const body = await r.json();
                newBuildWaiting = !!body.build && body.build !== meta.content;
            } catch (_) { return; /* offline: try again later */ }
        }
        if (newBuildWaiting) reloadWhenIdle();
    }

    function reloadWhenIdle() {
        const root = document.querySelector('.dp-pos');
        if (root && root.getAttribute('data-idle') === 'true') location.reload();
    }

    // Exposed for the tests (and so a page can ask for an immediate check).
    if (window.digipayPos) window.digipayPos.checkBuild = checkBuild;

    if (typeof location !== 'undefined' && (location.pathname === '/pos' || location.pathname === '/pos/')) {
        setInterval(checkBuild, BUILD_CHECK_MS);
        // Once a new build is known, reload as soon as the till goes idle (checked every few seconds).
        setInterval(() => { if (newBuildWaiting) reloadWhenIdle(); }, 5000);
    }
})();
