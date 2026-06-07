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
        await Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)));
        await clients.claim();
        // No forced reload — user gets the new version next time they open the app
    })());
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
