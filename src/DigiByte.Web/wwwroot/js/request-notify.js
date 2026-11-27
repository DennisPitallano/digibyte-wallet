// System notifications for paid payment requests (spec 036, FR-013, FR-014b). Shown only while the wallet is open in a
// tab or app: there is no push subscription and no server (FR-014). Does nothing where notifications are unsupported.
window.requestNotify = {
    permission: function () {
        if (typeof Notification === 'undefined') return 'unsupported';
        return Notification.permission;
    },

    // Called once, after the person makes their first request (FR-013), never on page load.
    ask: async function () {
        if (typeof Notification === 'undefined') return 'unsupported';
        if (Notification.permission !== 'default') return Notification.permission;
        try { return await Notification.requestPermission(); } catch { return 'denied'; }
    },

    show: async function (title, body) {
        if (typeof Notification === 'undefined' || Notification.permission !== 'granted') return false;
        const options = { body: body, tag: 'dgb-request-paid', renotify: true, icon: 'icon-192.png', badge: 'icon-192.png' };
        try {
            if ('serviceWorker' in navigator) {
                const registration = await navigator.serviceWorker.getRegistration();
                if (registration && registration.active) {
                    await registration.showNotification(title, options);
                    return true;
                }
            }
            new Notification(title, options);
            return true;
        } catch {
            return false;
        }
    }
};
