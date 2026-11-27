// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

// Same as the published worker: take over when the page asks (pwa-bootstrap.js applies a waiting update this way).
// Without it, an update waiting after a rebuild was applied on every load, never took over, and the page's
// 4-second fallback reloaded it again and again.
self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
});

// A paid-request notification (spec 036) brings the wallet to the front, or opens it.
self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            if ('focus' in client) return client.focus();
        }
        if (self.clients.openWindow) return self.clients.openWindow('./payments');
    })());
});
