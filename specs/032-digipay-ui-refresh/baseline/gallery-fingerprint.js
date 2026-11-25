// Computed-style fingerprint of a page (quickstart Q0/Q1 on the wallet's /test-animations).
// 1. Save the same theme on both builds and reload (the gallery's toggle label depends on it):
//      window.themeManager.setMode('light'); location.reload();
// 2. Paste this, then: await window.__fp('light') and await window.__fp('dark'), at 375px and 1280px.
// Two builds match only if every element's computed style is identical.
window.__fp = async function (mode) {
    window.themeManager.setMode(mode);
    await document.fonts.ready;
    await new Promise(r => setTimeout(r, 400));
    // Finish one-shot animations (entrance effects); pause infinite ones (spinners) at 0.
    document.getAnimations().forEach(a => { try { a.finish(); } catch (e) { try { a.pause(); a.currentTime = 0; } catch (_) { } } });
    const els = [...document.querySelectorAll('body *')];
    const per = [];
    for (const e of els) {
        const kv = [];
        for (const pe of [null, '::before', '::after']) {
            const cs = getComputedStyle(e, pe);
            // Sorted, because custom-property order depends on which stylesheet defines them;
            // whitespace collapsed, because line endings inside values are not visual.
            for (let i = 0; i < cs.length; i++) { const p = cs[i]; kv.push((pe || '') + p + ':' + cs.getPropertyValue(p).replace(/\s+/g, ' ').trim()); }
        }
        kv.sort();
        const b = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(e.tagName + '|' + kv.join('|')));
        per.push([...new Uint8Array(b)].slice(0, 4).map(x => x.toString(16).padStart(2, '0')).join(''));
    }
    const all = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(per.join(',')));
    return [...new Uint8Array(all)].map(x => x.toString(16).padStart(2, '0')).join('');
};
