// ============================================================
// ExpenseTracker — Service Worker
//   Strategy:
//     • Static shell (CSS/JS/icons/lib) → cache-first
//     • HTML navigations → network-first with cache fallback to offline.html
//     • Everything else (e.g. POSTs, API calls) → straight to network
// ============================================================

// Bump this whenever the shell changes — old caches with a different version
// are dropped on activate.
const VERSION = 'v3-2026-09-04';
const SHELL_CACHE = `et-shell-${VERSION}`;
const PAGES_CACHE = `et-pages-${VERSION}`;

// Static assets that are safe to pre-cache. We avoid auth-gated pages here.
const SHELL_ASSETS = [
    '/',
    '/offline.html',
    '/css/site.css',
    '/js/site.js',
    '/js/events.js',
    '/manifest.webmanifest',
    '/icons/icon.svg',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
    '/lib/jquery/dist/jquery.min.js',
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(SHELL_CACHE)
            .then((cache) => Promise.all(
                SHELL_ASSETS.map((url) =>
                    cache.add(url).catch((e) => console.warn('SW pre-cache miss:', url, e))
                )
            ))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(
                keys
                    .filter((k) => k.startsWith('et-') && !k.endsWith(VERSION))
                    .map((k) => caches.delete(k))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', (event) => {
    const req = event.request;
    // Only handle GET — never cache POST/PUT/DELETE.
    if (req.method !== 'GET') return;

    const url = new URL(req.url);
    // Same-origin only (don't intercept Google Fonts, CDN icons, etc).
    if (url.origin !== self.location.origin) return;

    // HTML navigations → network-first, fall back to cached page, then offline.html.
    if (req.mode === 'navigate' || req.headers.get('accept')?.includes('text/html')) {
        event.respondWith(networkFirstHtml(req));
        return;
    }

    // Our own CSS/JS → stale-while-revalidate. Cache-first here meant a stylesheet
    // could outlive the markup it styles indefinitely (which is how an open dropdown
    // ended up rendering under the page heading: new HTML, months-old CSS).
    // Third-party libs under /lib/ are versioned by path, so they stay cache-first.
    if (/^\/(css|js)\//.test(url.pathname)) {
        event.respondWith(staleWhileRevalidate(req));
        return;
    }

    // Everything else static → cache-first.
    event.respondWith(cacheFirst(req));
});

async function networkFirstHtml(req) {
    try {
        const response = await fetch(req);
        // Stash a copy of successful HTML responses in case we go offline.
        if (response && response.status === 200) {
            const copy = response.clone();
            caches.open(PAGES_CACHE).then((cache) => cache.put(req, copy));
        }
        return response;
    } catch (e) {
        const cached = await caches.match(req);
        if (cached) return cached;
        // Final fallback — the offline page.
        const offline = await caches.match('/offline.html');
        return offline || new Response('Offline', { status: 503, statusText: 'Offline' });
    }
}

// Serve the cached copy immediately, but always re-fetch and replace it, so the
// next load is current. One stale render at most, never a permanently stale shell.
async function staleWhileRevalidate(req) {
    const cached = await caches.match(req);
    const network = fetch(req).then((response) => {
        if (response && response.status === 200) {
            const copy = response.clone();
            caches.open(SHELL_CACHE).then((cache) => cache.put(req, copy));
        }
        return response;
    }).catch(() => null);

    if (cached) return cached;
    const fresh = await network;
    return fresh || new Response('Asset unavailable offline', { status: 503 });
}

async function cacheFirst(req) {
    const cached = await caches.match(req);
    if (cached) return cached;
    try {
        const response = await fetch(req);
        if (response && response.status === 200) {
            const copy = response.clone();
            caches.open(SHELL_CACHE).then((cache) => cache.put(req, copy));
        }
        return response;
    } catch (e) {
        return new Response('Asset unavailable offline', { status: 503 });
    }
}
