const CACHE_NAME = 'phonegrade-v1';
const ASSETS = [
    '/',
    '/manifest.json',
    '/styles.css',
    '/app.js',
    '/modules/DeviceTest.js',
    '/modules/TouchTest.js',
    '/modules/DisplayTest.js',
    '/modules/MicrophoneTest.js',
    '/modules/SpeakerTest.js',
    '/modules/CameraTest.js',
    '/modules/SensorTest.js'
];

self.addEventListener('install', (e) => {
    e.waitUntil(
        caches.open(CACHE_NAME).then((cache) => {
            return cache.addAll(ASSETS);
        })
    );
});

self.addEventListener('fetch', (e) => {
    e.respondWith(
        caches.match(e.request).then((response) => {
            return response || fetch(e.request);
        })
    );
});

self.addEventListener('activate', (e) => {
    e.waitUntil(
        caches.keys().then((cacheNames) => {
            return Promise.all(
                cacheNames.filter((name) => name !== CACHE_NAME).map((name) => {
                    return caches.delete(name);
                })
            );
        })
    );
});