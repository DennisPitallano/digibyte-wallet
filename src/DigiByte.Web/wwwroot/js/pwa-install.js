// PWA Install prompt capture
// Stores the deferred prompt so Blazor can trigger it later. Several Blazor
// components may subscribe (the top banner and the Settings "Install app" row),
// so callbacks are kept in a map instead of a single slot.
let deferredPrompt = null;
let promptSeen = false;

// dotnetRef -> { promptUsed: boolean }. Only subscribers that ask for it receive
// 'OnPromptUsed', so a subscriber that does not implement it (the banner) is never called.
const subscribers = new Map();

function notify(method, onlyIfWanted) {
    subscribers.forEach((opts, ref) => {
        if (onlyIfWanted && !opts[onlyIfWanted]) return;
        try {
            ref.invokeMethodAsync(method).catch(() => subscribers.delete(ref));
        } catch {
            // Disposed reference: drop it, never let one subscriber stop the others.
            subscribers.delete(ref);
        }
    });
}

window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
    promptSeen = true;
    notify('OnInstallAvailable');
});

window.addEventListener('appinstalled', () => {
    deferredPrompt = null;
    notify('OnAppInstalled');
});

function isStandalone() {
    return window.matchMedia('(display-mode: standalone)').matches
        || window.navigator.standalone === true;
}

function isIos() {
    const ua = window.navigator.userAgent;
    return /iP(hone|od|ad)/.test(ua)
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}

function isAndroid() {
    return /Android/i.test(window.navigator.userAgent);
}

window.pwaInstall = {
    // opts.promptUsed: also receive 'OnPromptUsed' when the held prompt was used or dismissed.
    registerCallback: (dotnetRef, opts) => {
        subscribers.set(dotnetRef, { promptUsed: !!(opts && opts.promptUsed) });
        // If prompt was already captured before Blazor loaded
        if (deferredPrompt) {
            dotnetRef.invokeMethodAsync('OnInstallAvailable').catch(() => subscribers.delete(dotnetRef));
        }
        // On iOS Safari there's no beforeinstallprompt — notify Blazor for manual instructions
        if (isIos() && !isStandalone()) {
            dotnetRef.invokeMethodAsync('OnIosDetected').catch(() => subscribers.delete(dotnetRef));
        }
    },
    unsubscribe: (dotnetRef) => {
        subscribers.delete(dotnetRef);
    },
    prompt: async () => {
        if (!deferredPrompt) return false;
        const held = deferredPrompt;
        held.prompt();
        const result = await held.userChoice;
        deferredPrompt = null; // a captured prompt can be shown only once
        const accepted = result.outcome === 'accepted';
        if (!accepted) notify('OnPromptUsed', 'promptUsed');
        return accepted;
    },
    isStandalone,
    isIos,
    isAndroid,
    // Synchronous snapshot used to decide what the Settings install row shows.
    getState: () => {
        const ua = window.navigator.userAgent;
        const ios = isIos();
        return {
            standalone: isStandalone(),
            ios,
            android: isAndroid(),
            canPrompt: !!deferredPrompt,
            nonSafariIos: ios && (/CriOS|FxiOS|EdgiOS|OPiOS/.test(ua) || !/Safari/.test(ua)),
            promptSeen
        };
    }
};
