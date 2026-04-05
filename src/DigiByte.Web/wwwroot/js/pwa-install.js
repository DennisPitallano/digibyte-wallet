// PWA Install prompt capture
// Stores the deferred prompt so Blazor can trigger it later.
let deferredPrompt = null;

window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
    // Notify Blazor the prompt is available
    if (window._pwaInstallCallback) {
        window._pwaInstallCallback.invokeMethodAsync('OnInstallAvailable');
    }
});

window.addEventListener('appinstalled', () => {
    deferredPrompt = null;
    if (window._pwaInstallCallback) {
        window._pwaInstallCallback.invokeMethodAsync('OnAppInstalled');
    }
});

window.pwaInstall = {
    registerCallback: (dotnetRef) => {
        window._pwaInstallCallback = dotnetRef;
        // If prompt was already captured before Blazor loaded
        if (deferredPrompt) {
            dotnetRef.invokeMethodAsync('OnInstallAvailable');
        }
        // On iOS Safari there's no beforeinstallprompt — notify Blazor for manual instructions
        if (window.pwaInstall.isIos() && !window.pwaInstall.isStandalone()) {
            dotnetRef.invokeMethodAsync('OnIosDetected');
        }
    },
    prompt: async () => {
        if (!deferredPrompt) return false;
        deferredPrompt.prompt();
        const result = await deferredPrompt.userChoice;
        deferredPrompt = null;
        return result.outcome === 'accepted';
    },
    isStandalone: () => {
        return window.matchMedia('(display-mode: standalone)').matches
            || window.navigator.standalone === true;
    },
    isIos: () => {
        const ua = window.navigator.userAgent;
        return /iP(hone|od|ad)/.test(ua)
            || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    }
};
