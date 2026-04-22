// Version badges for /sdks — one-shot fetch of the currently-published
// version from npm / PyPI / NuGet. Tabs and copy buttons are handled by
// Blazor (@rendermode InteractiveServer) in Sdks.razor.

(function () {
    const REGISTRY_ENDPOINTS = {
        npm: 'https://registry.npmjs.org/@dgbwallet/digipay/latest',
        pypi: 'https://pypi.org/pypi/digipay/json',
        nuget: 'https://api.nuget.org/v3-flatcontainer/digipay/index.json',
    };

    async function fetchVersion(registry) {
        try {
            const res = await fetch(REGISTRY_ENDPOINTS[registry], { cache: 'no-cache' });
            if (!res.ok) return null;
            const body = await res.json();
            if (registry === 'npm') return body.version;
            if (registry === 'pypi') return body.info?.version;
            if (registry === 'nuget') {
                const versions = body.versions || [];
                return versions.length ? versions[versions.length - 1] : null;
            }
        } catch {
            return null;
        }
    }

    async function initVersionBadges() {
        const badges = document.querySelectorAll('[data-version-badge]');
        await Promise.all(Array.from(badges).map(async (el) => {
            const registry = el.getAttribute('data-version-badge');
            const version = await fetchVersion(registry);
            if (version) {
                el.textContent = 'v' + version;
                el.classList.remove('opacity-0');
            }
        }));
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initVersionBadges);
    } else {
        initVersionBadges();
    }
})();
// Progressive-enhancement helpers for /sdks. Runs on the static SSR
// markup — no Blazor interactivity required, which keeps the page
// fully cacheable.
//
// Three responsibilities:
//   1. Language tabs for the "Create a session" / "Verify a webhook"
//      sections. Click a tab → show the matching code pane, hide the
//      others. Keyboard-accessible (arrow keys + home/end).
//   2. Copy-to-clipboard on install commands and any [data-copy] element.
//      Visual "Copied ✓" feedback for 1.5 s.
//   3. Live version badges — fetch the currently-published version
//      from npm / PyPI / NuGet and stamp it into the install cards.
//      Gracefully degrades if offline or a registry is down.

(function () {
    function initTabs(root) {
        const tablist = root.querySelector('[role="tablist"]');
        if (!tablist) return;
        const tabs = Array.from(tablist.querySelectorAll('[role="tab"]'));
        const panelsById = new Map(
            Array.from(root.querySelectorAll('[role="tabpanel"]')).map(p => [p.id, p])
        );

        function activate(tab) {
            tabs.forEach(t => {
                const active = t === tab;
                t.setAttribute('aria-selected', active ? 'true' : 'false');
                t.tabIndex = active ? 0 : -1;
                t.classList.toggle('bg-dgb-500', active);
                t.classList.toggle('text-white', active);
                t.classList.toggle('text-slate-600', !active);
                t.classList.toggle('dark:text-slate-300', !active);
                t.classList.toggle('hover:bg-slate-100', !active);
                t.classList.toggle('dark:hover:bg-dgb-800', !active);
                const panel = panelsById.get(t.getAttribute('aria-controls'));
                if (panel) panel.hidden = !active;
            });
        }

        tabs.forEach((tab, i) => {
            tab.addEventListener('click', () => activate(tab));
            tab.addEventListener('keydown', (ev) => {
                let target = null;
                if (ev.key === 'ArrowRight') target = tabs[(i + 1) % tabs.length];
                else if (ev.key === 'ArrowLeft') target = tabs[(i - 1 + tabs.length) % tabs.length];
                else if (ev.key === 'Home') target = tabs[0];
                else if (ev.key === 'End') target = tabs[tabs.length - 1];
                if (target) { target.focus(); activate(target); ev.preventDefault(); }
            });
        });
    }

    function initCopyButtons() {
        document.querySelectorAll('[data-copy]').forEach(btn => {
            btn.addEventListener('click', async (ev) => {
                ev.preventDefault();
                const text = btn.getAttribute('data-copy');
                try {
                    await navigator.clipboard.writeText(text);
                } catch {
                    // Very old browser fallback — synthesise a selection.
                    const ta = document.createElement('textarea');
                    ta.value = text; document.body.appendChild(ta);
                    ta.select(); document.execCommand('copy');
                    document.body.removeChild(ta);
                }
                const original = btn.getAttribute('data-label') || btn.textContent;
                btn.textContent = 'Copied ✓';
                btn.classList.add('text-emerald-500');
                setTimeout(() => {
                    btn.textContent = original;
                    btn.classList.remove('text-emerald-500');
                }, 1500);
            });
        });
    }

    // Each registry has a tiny JSON endpoint that reveals the current
    // version. All three use CORS-open endpoints so a public site can
    // hit them from the browser without a proxy.
    const REGISTRY_ENDPOINTS = {
        npm: 'https://registry.npmjs.org/@dgbwallet/digipay/latest',
        pypi: 'https://pypi.org/pypi/digipay/json',
        nuget: 'https://api.nuget.org/v3-flatcontainer/digipay/index.json',
    };

    async function fetchVersion(registry) {
        try {
            const res = await fetch(REGISTRY_ENDPOINTS[registry], { cache: 'no-cache' });
            if (!res.ok) return null;
            const body = await res.json();
            if (registry === 'npm') return body.version;
            if (registry === 'pypi') return body.info?.version;
            if (registry === 'nuget') {
                // flat-container returns { versions: ["0.1.0", ...] }
                const versions = body.versions || [];
                return versions.length ? versions[versions.length - 1] : null;
            }
        } catch {
            return null;
        }
    }

    async function initVersionBadges() {
        const badges = document.querySelectorAll('[data-version-badge]');
        await Promise.all(Array.from(badges).map(async (el) => {
            const registry = el.getAttribute('data-version-badge');
            const version = await fetchVersion(registry);
            if (version) {
                el.textContent = 'v' + version;
                el.classList.remove('opacity-0');
            }
        }));
    }

    function init() {
        document.querySelectorAll('[data-tabs]').forEach(initTabs);
        initCopyButtons();
        initVersionBadges();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
