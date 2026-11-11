// Theme management: light, dark, or follow the device ("system"), kept in localStorage.
// No saved choice means dark, as before.
window.themeManager = {
    KEY: 'dgb-theme',
    COLORS: { dark: '#002352', light: '#f8fafc' },

    getMode() {
        var saved = null;
        try { saved = localStorage.getItem(this.KEY); } catch (e) { }
        return saved === 'light' || saved === 'system' ? saved : 'dark';
    },

    _systemDark() {
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    },

    // Applies the current mode to <html> and the browser chrome colour. Returns true when dark.
    _apply() {
        var mode = this.getMode();
        var dark = mode === 'system' ? this._systemDark() : mode === 'dark';
        document.documentElement.classList.toggle('dark', dark);
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', dark ? this.COLORS.dark : this.COLORS.light);
        return dark;
    },

    init() {
        return this._apply();
    },

    setMode(mode) {
        if (mode !== 'light' && mode !== 'system') mode = 'dark';
        try { localStorage.setItem(this.KEY, mode); } catch (e) { }
        return this._apply();
    },

    setDark(isDark) {
        this.setMode(isDark ? 'dark' : 'light');
    },

    isDark() {
        return document.documentElement.classList.contains('dark');
    }
};

// Initialize theme immediately to prevent flash
window.themeManager.init();

// Follow the device while in "system" mode.
if (window.matchMedia) {
    var themeQuery = window.matchMedia('(prefers-color-scheme: dark)');
    var onSystemThemeChange = function () {
        if (window.themeManager.getMode() === 'system') window.themeManager._apply();
    };
    if (themeQuery.addEventListener) themeQuery.addEventListener('change', onSystemThemeChange);
    else if (themeQuery.addListener) themeQuery.addListener(onSystemThemeChange);
}
