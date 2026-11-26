// Counts numbers up from zero when they come into view (home page stats). Each number is an element with
// data-count-to="1234.5" and optional data-decimals="0"; its text already holds the final value, which stays
// if JavaScript is off or the visitor prefers reduced motion.

const DURATION_MS = 1600;
const easeOutExpo = (t) => (t >= 1 ? 1 : 1 - Math.pow(2, -10 * t));

function animate(el) {
    if (el.dataset.counted) return;
    el.dataset.counted = '1';
    const to = Number(el.dataset.countTo);
    const decimals = Number(el.dataset.decimals || 0);
    if (!Number.isFinite(to) || to <= 0) return;
    const fmt = new Intl.NumberFormat('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    const final = el.textContent;
    const start = performance.now();
    el.classList.add('is-counting');
    const step = (now) => {
        const t = Math.min(1, (now - start) / DURATION_MS);
        el.textContent = fmt.format(to * easeOutExpo(t));
        if (t < 1) requestAnimationFrame(step);
        else {
            el.textContent = final; // exactly what the server wrote
            el.classList.remove('is-counting');
            el.classList.add('is-done');
        }
    };
    requestAnimationFrame(step);
}

/** Starts counting the numbers inside root as soon as they are on screen. */
export function countUp(root) {
    if (!root) return;
    const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const items = root.querySelectorAll('[data-count-to]');
    if (reduce || !('IntersectionObserver' in window)) return;
    const io = new IntersectionObserver((entries) => {
        for (const e of entries) {
            if (e.isIntersecting) { animate(e.target); io.unobserve(e.target); }
        }
    }, { threshold: 0.4 });
    items.forEach((el) => io.observe(el));
}
