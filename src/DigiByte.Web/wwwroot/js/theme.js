// Theme management - light/dark mode with localStorage persistence
window.themeManager = {
    init() {
        const saved = localStorage.getItem('dgb-theme');
        if (saved === 'dark') {
            document.documentElement.classList.add('dark');
            return true; // isDark
        }
        document.documentElement.classList.remove('dark');
        return false;
    },

    setDark(isDark) {
        if (isDark) {
            document.documentElement.classList.add('dark');
            localStorage.setItem('dgb-theme', 'dark');
        } else {
            document.documentElement.classList.remove('dark');
            localStorage.setItem('dgb-theme', 'light');
        }
    },

    isDark() {
        return document.documentElement.classList.contains('dark');
    }
};

// Initialize theme immediately to prevent flash
window.themeManager.init();
