// Browser helpers for the dashboard and the receipt, loaded on demand as a module (LocalClock.cs).

/** The browser's IANA time zone, e.g. "Europe/Amsterdam", or null when unknown. */
export function timeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch (_) {
        return null;
    }
}

const RAIL_KEY = 'digipay.dash.railCollapsed:v1';

/** Whether the merchant collapsed the dashboard menu in this browser. */
export function railCollapsed() {
    try { return localStorage.getItem(RAIL_KEY) === '1'; } catch (_) { return false; }
}

/** Remembers the menu's collapsed state for this browser. */
export function setRailCollapsed(collapsed) {
    try {
        if (collapsed) localStorage.setItem(RAIL_KEY, '1');
        else localStorage.removeItem(RAIL_KEY);
    } catch (_) { /* private mode: it just isn't remembered */ }
}

/**
 * Tooltips for the collapsed dashboard menu. The rail scrolls, which clips anything drawn outside it, so one
 * fixed-position tip is moved next to whichever [data-label] item is hovered or focused, only while the shell
 * has .is-rail-collapsed. Bound once per rail element.
 */
export function bindRailTips(rail) {
    if (!rail || rail.dataset.tipsBound) return;
    rail.dataset.tipsBound = '1';
    // One tip for the page, reused when the shell is drawn again on the next dashboard page.
    let tip = document.getElementById('dp-rail-tip');
    if (!tip) {
        tip = document.createElement('div');
        tip.id = 'dp-rail-tip';
        tip.className = 'dp-rail-tip';
        tip.setAttribute('aria-hidden', 'true'); // the item already has its accessible name
        document.body.appendChild(tip);
    }
    tip.classList.remove('is-shown');

    const show = (el) => {
        const shell = rail.closest('.dp-shell');
        if (!shell || !shell.classList.contains('is-rail-collapsed') || !el) { hide(); return; }
        const r = el.getBoundingClientRect();
        tip.textContent = el.getAttribute('data-label');
        tip.style.left = `${r.right + 12}px`;
        tip.style.top = `${r.top + r.height / 2}px`;
        tip.classList.add('is-shown');
    };
    const hide = () => tip.classList.remove('is-shown');
    const target = (e) => e.target instanceof Element ? e.target.closest('[data-label]') : null;

    rail.addEventListener('mouseover', (e) => show(target(e)));
    rail.addEventListener('mouseleave', hide);
    rail.addEventListener('focusin', (e) => show(target(e)));
    rail.addEventListener('focusout', hide);
    rail.addEventListener('scroll', hide, { passive: true });
    rail.addEventListener('click', hide);
}

const GROUPS_KEY = 'digipay.dash.foldedGroups:v1';

/** Menu groups the merchant folded in this browser (e.g. ["Developers"]). */
export function foldedGroups() {
    try {
        const v = JSON.parse(localStorage.getItem(GROUPS_KEY) || '[]');
        return Array.isArray(v) ? v.filter((x) => typeof x === 'string') : [];
    } catch (_) { return []; }
}

/** Remembers which menu groups are folded. */
export function setFoldedGroups(groups) {
    try {
        if (groups && groups.length) localStorage.setItem(GROUPS_KEY, JSON.stringify(groups));
        else localStorage.removeItem(GROUPS_KEY);
    } catch (_) { /* not remembered */ }
}
