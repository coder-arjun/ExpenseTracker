// =============================================================
// Persistent filters
//   Page opts in by giving its filter <form> a data-persist-key="<unique-key>".
//   On load: if the URL has no filter params for the keys we care about,
//     and LocalStorage has a saved snapshot, replay it (writing to the URL
//     so it's bookmarkable + the back button works).
//   On submit: snapshot the current form into LocalStorage under that key.
// =============================================================
(function () {
    'use strict';

    var STORAGE_PREFIX = 'et.filters.';

    function getForm() {
        return document.querySelector('form[data-persist-key]');
    }

    function persistedKeys(form) {
        // Form inputs we care about — anything with a name attribute that
        // contributes a filter value.
        return Array.from(form.querySelectorAll('[name]'))
            .filter(function (el) { return el.name && el.name !== 'page'; })
            .map(function (el) { return el.name; });
    }

    function snapshot(form) {
        var snap = {};
        persistedKeys(form).forEach(function (key) {
            var el = form.querySelector('[name="' + CSS.escape(key) + '"]');
            if (!el) return;
            // Skip checkboxes/radios for now — none of the existing filters use them.
            if (el.value !== '' && el.value != null) snap[key] = el.value;
        });
        return snap;
    }

    function urlHasFilterParams(keys) {
        var params = new URLSearchParams(window.location.search);
        return keys.some(function (k) { return params.has(k) && params.get(k) !== ''; });
    }

    function rehydrateFromStorage(form, storageKey) {
        var raw = localStorage.getItem(STORAGE_PREFIX + storageKey);
        if (!raw) return;
        var saved;
        try { saved = JSON.parse(raw); } catch (e) { return; }
        if (!saved || typeof saved !== 'object') return;

        // Rebuild the URL with the saved filters and navigate.
        // (We don't just fill the form because the index actions are
        // GET-driven — the page needs the query string to re-render.)
        var params = new URLSearchParams();
        Object.keys(saved).forEach(function (k) {
            if (saved[k] != null && saved[k] !== '') params.set(k, saved[k]);
        });
        if (params.toString() === '') return;
        // Avoid an infinite loop if rehydrating a snapshot whose URL we'd already match.
        if (window.location.search.replace(/^\?/, '') === params.toString()) return;
        window.location.replace(window.location.pathname + '?' + params.toString());
    }

    function wire() {
        var form = getForm();
        if (!form) return;
        var storageKey = form.getAttribute('data-persist-key');
        var keys = persistedKeys(form);

        if (!urlHasFilterParams(keys)) {
            rehydrateFromStorage(form, storageKey);
        }

        form.addEventListener('submit', function () {
            var snap = snapshot(form);
            if (Object.keys(snap).length === 0) {
                localStorage.removeItem(STORAGE_PREFIX + storageKey);
            } else {
                localStorage.setItem(STORAGE_PREFIX + storageKey, JSON.stringify(snap));
            }
        });

        // "Clear" links navigate to the bare action; clear storage too so the
        // next visit really starts fresh.
        document.querySelectorAll('a[href]').forEach(function (a) {
            var href = a.getAttribute('href');
            if (href && href.match(/^\?$|^\/[A-Z][A-Za-z]+\/?$/)) {
                a.addEventListener('click', function () {
                    localStorage.removeItem(STORAGE_PREFIX + storageKey);
                });
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', wire);
    } else {
        wire();
    }
})();
