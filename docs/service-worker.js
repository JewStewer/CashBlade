// Blazor PWA service worker
self.importScripts('./service-worker-assets.js');

const CACHE = 'cashblade-' + self.assetsManifest.version;

// iOS sometimes leaves an in-flight fetch hanging forever after the app is
// backgrounded and resumed, which would otherwise stall the boot on the
// loading screen. Bound network fetches so we always fall back to cache (or
// a rejection the page-level watchdog can react to) instead of hanging.
function fetchWithTimeout(request, ms) {
    return Promise.race([
        fetch(request),
        new Promise((_, reject) => setTimeout(() => reject(new Error('fetch timeout')), ms))
    ]);
}

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
        // No forced client.navigate() — navigation is network-first so the
        // browser always fetches fresh index.html anyway. Removing the navigate
        // eliminates the 2-3 reload problem on iOS.
    })());
});

// ── Push notifications ──────────────────────────────────────────────────────
self.addEventListener('push', event => {
    let data = { title: 'Evergrove', body: 'You have a bill due soon.' };
    try { if (event.data) data = event.data.json(); } catch {}
    event.waitUntil(
        self.registration.showNotification(data.title, {
            body: data.body,
            icon: './icons/icon-192.png',
            badge: './icons/icon-192.png',
            tag: 'bill-reminder',
            renotify: true,
            data: { url: new URL('./', self.registration.scope).href }
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

        // For page navigation: NETWORK FIRST so the latest index.html is always served.
        // This means code updates are visible immediately on next app open without
        // any forced reload tricks. Falls back to cache when offline.
        if (event.request.mode === 'navigate') {
            try {
                const response = await fetchWithTimeout(event.request, 5000);
                if (response.ok) return response;
                return (await cache.match('index.html')) ?? (await cache.match('./index.html')) ?? response;
            } catch (err) {
                const cached = (await cache.match('index.html')) ?? (await cache.match('./index.html'));
                if (cached) return cached;
                // No cache and the network failed — let the request fail normally.
                // Responding with Response.error() makes the browser surface
                // "Response served by service worker is an error" instead of a
                // normal failed navigation the page-level watchdog can recover from.
                throw err;
            }
        }

        // Framework files change together. Prefer the network so iOS does not mix
        // a fresh boot manifest with stale/corrupt cached WASM after an update.
        if (new URL(event.request.url).pathname.includes('/_framework/')) {
            try {
                const response = await fetchWithTimeout(event.request, 10000);
                if (response.ok) {
                    cache.put(event.request, response.clone()).catch(() => {});
                    return response;
                }
            } catch {}
        }

        // For other assets: cache first, fall back to network. If both miss, let
        // the fetch rejection propagate instead of Response.error() (see above).
        return (await cache.match(event.request)) ?? (await fetchWithTimeout(event.request, 10000));
    })());
});
/* Manifest version: DLQ5PcGK */
