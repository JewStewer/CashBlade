// Blazor PWA service worker
self.importScripts('./service-worker-assets.js');

const CACHE = 'cashblade-' + self.assetsManifest.version;

self.addEventListener('install', event => {
    event.waitUntil((async () => {
        const cache = await caches.open(CACHE);
        // Cache each asset individually — one 404 won't block the whole install
        const urls = self.assetsManifest.assets.map(a => a.url);
        await Promise.all(urls.map(url => cache.add(url).catch(() => {})));
        self.skipWaiting(); // activate immediately after caching is done
    })());
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        // Remove old caches from previous versions
        const keys = await caches.keys();
        const oldKeys = keys.filter(k => k !== CACHE);
        const isUpdate = oldKeys.length > 0; // false on fresh install
        await Promise.all(oldKeys.map(k => caches.delete(k)));
        await clients.claim();
        // Only reload open windows when this is a real update (not a fresh install).
        // On fresh install there's nothing to reload — the page already has latest assets.
        if (isUpdate) {
            const all = await clients.matchAll({ type: 'window' });
            for (const c of all) {
                try { c.navigate(c.url); } catch {}
            }
        }
    })());
});

// ── Push notifications ──────────────────────────────────────────────────────
self.addEventListener('push', event => {
    let data = { title: 'Finance Blade', body: 'You have a bill due soon.' };
    try { if (event.data) data = event.data.json(); } catch {}
    event.waitUntil(
        self.registration.showNotification(data.title, {
            body: data.body,
            icon: './icons/icon-192.png',
            badge: './icons/icon-192.png',
            tag: 'bill-reminder',
            renotify: true,
            data: { url: self.location.origin + '/CashBlade/' }
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = event.notification.data?.url || self.location.origin;
    event.waitUntil(clients.matchAll({ type: 'window' }).then(list => {
        const existing = list.find(c => c.url.startsWith(url));
        if (existing) return existing.focus();
        return clients.openWindow(url);
    }));
});

self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') return;
    event.respondWith((async () => {
        const cache = await caches.open(CACHE);

        // For page navigation: serve cached index.html so deep links work offline
        if (event.request.mode === 'navigate') {
            const cached = await cache.match('index.html');
            if (cached) return cached;
            return fetch(event.request);
        }

        // For assets: cache first, fall back to network
        const cached = await cache.match(event.request);
        return cached ?? fetch(event.request);
    })());
});
