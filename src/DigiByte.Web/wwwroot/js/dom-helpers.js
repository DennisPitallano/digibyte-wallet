// DOM helper functions called from Blazor via JS interop
// Replaces inline eval() calls that violate CSP
window.domHelpers = {
    focusElement: function (id) {
        document.getElementById(id)?.focus();
    },
    selectElement: function (id) {
        document.getElementById(id)?.select();
    },
    autoSizeTxList: function () {
        var main = document.querySelector('main');
        if (main) main.style.overflow = 'hidden';

        var el = document.getElementById('tx-list');
        if (!el) return;
        var rect = el.getBoundingClientRect();
        var navHeight = 64;
        var padding = 16;
        var available = window.innerHeight - rect.top - navHeight - padding;
        if (available > 100) el.style.maxHeight = available + 'px';
    },
    restoreScroll: function () {
        var m = document.querySelector('main');
        if (m) m.style.overflow = '';
    }
};
