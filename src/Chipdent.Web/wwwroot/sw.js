// Chipdent Service Worker
// Strategia:
//  - Asset statici (CSS/JS/img/manifest): cache-first con network fallback
//  - Pagine HTML: network-first con fallback alla cache (e infine /offline.html)
//  - SignalR e API JSON: sempre rete (MAI cacciare risposte autenticate scadute)

const VERSION = "chipdent-v3";
const STATIC_CACHE = "chipdent-static-" + VERSION;
const RUNTIME_CACHE = "chipdent-runtime-" + VERSION;

const PRECACHE_URLS = [
    "/offline.html",
    "/css/site.css",
    "/css/tour.css",
    "/js/site.js",
    "/js/cmdk.js",
    "/js/notifications.js",
    "/js/tour.js",
    "/img/logo.png",
    "/site.webmanifest"
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(STATIC_CACHE).then((cache) => cache.addAll(PRECACHE_URLS).catch(() => {}))
    );
    self.skipWaiting();
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(
                keys
                    .filter((k) => !k.endsWith(VERSION))
                    .map((k) => caches.delete(k))
            )
        ).then(() => self.clients.claim())
    );
});

function isStatic(url) {
    return /\.(css|js|png|jpg|jpeg|svg|webp|ico|woff2?|ttf|webmanifest)$/i.test(url.pathname)
        || url.pathname.startsWith("/img/")
        || url.pathname.startsWith("/css/")
        || url.pathname.startsWith("/js/");
}

function isHtmlNavigation(request) {
    return request.mode === "navigate"
        || (request.headers.get("accept") || "").includes("text/html");
}

function isUncacheable(url, request) {
    // SignalR, API auth, file upload — non cacciare mai
    return url.pathname.startsWith("/hubs/")
        || url.pathname.startsWith("/api/")
        || url.pathname.startsWith("/account/login")
        || url.pathname.startsWith("/account/logout")
        || url.pathname.includes("/uploads/")
        || request.method !== "GET";
}

self.addEventListener("fetch", (event) => {
    const request = event.request;
    const url = new URL(request.url);

    if (url.origin !== self.location.origin) return;
    if (isUncacheable(url, request)) return;

    if (isStatic(url)) {
        // Cache-first
        event.respondWith(
            caches.match(request).then((cached) =>
                cached
                || fetch(request).then((res) => {
                    if (res.ok) {
                        const copy = res.clone();
                        caches.open(STATIC_CACHE).then((c) => c.put(request, copy));
                    }
                    return res;
                }).catch(() => caches.match("/offline.html"))
            )
        );
        return;
    }

    if (isHtmlNavigation(request)) {
        // Network-first con fallback
        event.respondWith(
            fetch(request).then((res) => {
                if (res.ok) {
                    const copy = res.clone();
                    caches.open(RUNTIME_CACHE).then((c) => c.put(request, copy));
                }
                return res;
            }).catch(() =>
                caches.match(request).then((cached) => cached || caches.match("/offline.html"))
            )
        );
    }
});
