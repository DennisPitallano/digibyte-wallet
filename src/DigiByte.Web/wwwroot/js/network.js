// Network status monitoring with Blazor interop
window.networkStatus = {
    _dotNetRef: null,

    initialize(dotNetRef) {
        this._dotNetRef = dotNetRef;
        window.addEventListener('online', () => this._notify(true));
        window.addEventListener('offline', () => this._notify(false));
    },

    isOnline() {
        return navigator.onLine;
    },

    _notify(isOnline) {
        if (this._dotNetRef) {
            this._dotNetRef.invokeMethodAsync('OnNetworkStatusChanged', isOnline);
        }
    },

    dispose() {
        this._dotNetRef = null;
    }
};

// Offline data cache in localStorage (small, fast)
window.offlineCache = {
    save(key, data) {
        try {
            localStorage.setItem('dgb-cache-' + key, JSON.stringify({
                data: data,
                ts: Date.now()
            }));
        } catch { }
    },

    load(key, maxAgeMs) {
        try {
            const raw = localStorage.getItem('dgb-cache-' + key);
            if (!raw) return null;
            const entry = JSON.parse(raw);
            if (maxAgeMs && (Date.now() - entry.ts) > maxAgeMs) return null;
            return entry.data;
        } catch { return null; }
    },

    getAge(key) {
        try {
            const raw = localStorage.getItem('dgb-cache-' + key);
            if (!raw) return -1;
            const entry = JSON.parse(raw);
            return Date.now() - entry.ts;
        } catch { return -1; }
    }
};
