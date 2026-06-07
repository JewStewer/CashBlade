// Blazor PWA service worker — no integrity checks (compatible with Netlify CDN)
self.importScripts('./service-worker-assets.js');

const CACHE = 'finblade-' + self.assetsManifest.version;

self.addEventListener('install', event => {
    event.waitUntil((async () => {
        self.skipWaiting();
        const cache = await caches.open(CACHE);
        // Cache all assets without integrity checks so Netlify CDN doesn't break them
        const urls = self.assetsManifest.assets.map(a => a.url);
        await cache.addAll(urls);
    })());
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        // Delete old caches
        const keys = await caches.keys();
        await Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)));
        await clients.claim();
        // Tell all open pages to reload so they immediately get the new version
        const allClients = await clients.matchAll({ type: 'window' });
        allClients.forEach(c => c.postMessage({ type: 'SW_UPDATED' }));
    })());
});

self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') return;
    event.respondWith((async () => {
        const cache = await caches.open(CACHE);

        // For page navigation always serve index.html from cache (enables offline + home screen)
        if (event.request.mode === 'navigate') {
            const cached = await cache.match('index.html');
            if (cached) return cached;
            // Not cached yet — fetch from network
            return fetch('index.html');
        }

        // For all other assets: cache first, fall back to network
        const cached = await cache.match(event.request);
        return cached ?? fetch(event.request);
    })());
});
