/* WriteQuest service worker – generated at build time (see vite.config.ts). */
const VERSION = "mvryqugi";
const CACHE = `writequest-${VERSION}`;
const PRECACHE = ["./","./assets/nunito-latin-500-normal-EugFkASW.woff2","./assets/baloo-2-latin-600-normal-tIfxVoAe.woff2","./assets/baloo-2-latin-700-normal-CqTg7A15.woff2","./assets/baloo-2-latin-800-normal-BbF3Etk1.woff2","./assets/nunito-latin-800-normal-Dz8SOQK_.woff2","./assets/nunito-latin-700-normal-Dort48En.woff2","./assets/nunito-latin-500-normal-B5klmw3Q.woff","./assets/baloo-2-latin-600-normal-B4tNaogw.woff","./assets/baloo-2-latin-700-normal-Ld3Zm3l2.woff","./assets/baloo-2-latin-800-normal-B8BXfZF7.woff","./assets/nunito-latin-800-normal-D-J0wlBY.woff","./assets/nunito-latin-700-normal-OcDqTBcA.woff","./assets/index-D970p1hJ.css","./assets/index-CZX7g1R7.js","./manifest.webmanifest","./icon.svg","./icon-192.png","./icon-512.png","./icon-maskable.png"];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) => cache.addAll(PRECACHE))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k.startsWith('writequest-') && k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  // App shell: network first (fresh when online), cached shell when offline.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then((res) => {
          const copy = res.clone();
          caches.open(CACHE).then((c) => c.put('./', copy));
          return res;
        })
        .catch(() => caches.match('./').then((r) => r || caches.match('./index.html'))),
    );
    return;
  }

  // Hashed assets: cache first.
  event.respondWith(
    caches.match(req).then(
      (hit) =>
        hit ||
        fetch(req).then((res) => {
          if (res.ok) {
            const copy = res.clone();
            caches.open(CACHE).then((c) => c.put(req, copy));
          }
          return res;
        }),
    ),
  );
});

self.addEventListener('message', (event) => {
  if (event.data === 'skip-waiting') self.skipWaiting();
});
