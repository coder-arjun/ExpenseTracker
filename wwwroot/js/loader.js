// ============================================================
// Finoma — the brand mark as the loader
//
// This is a server-rendered MVC app, so the real waiting happens between a
// click and the next document. The overlay is shown on outbound navigation
// after a short grace period, so a fast page never flashes it.
//
// Public API:  window.finomaLoader.show(text?)  /  .hide()
// ============================================================
(function () {
    'use strict';

    var el = document.getElementById('finLoader');
    if (!el) return;

    var textEl = document.getElementById('finLoaderText');
    var showTimer = null;
    var failSafe = null;

    // Only show once a navigation has actually taken a moment. Below this,
    // the page usually arrives first and a flash would be worse than nothing.
    var GRACE_MS = 180;
    // Never leave the overlay stuck if a navigation is cancelled in a way we
    // cannot observe (a download that fails, a blocked popup, a dead link).
    var FAILSAFE_MS = 10000;

    // Contextual copy, longest paths first so /Expenses/Create wins over /Expenses.
    var COPY = [
        [/^\/(dashboard)?$/i, 'Updating your financial picture'],
        [/^\/dashboard/i, 'Updating your financial picture'],
        [/^\/insights/i, 'Reading your month'],
        [/^\/(expenses|incomes|transfers|savings|debts)/i, 'Organising your transactions'],
        [/^\/(budget|goals|recurring|events)/i, 'Calculating your progress'],
        [/^\/(categories|accounts)/i, 'Loading your setup'],
        [/^\/identity/i, 'One moment']
    ];

    function copyFor(path) {
        for (var i = 0; i < COPY.length; i++) {
            if (COPY[i][0].test(path)) return COPY[i][1];
        }
        return 'Preparing your workspace';
    }

    function show(message) {
        clearTimeout(failSafe);
        if (textEl && message) textEl.textContent = message;
        el.hidden = false;
        // Force a frame so the fade-in transition actually runs.
        void el.offsetWidth;
        el.classList.add('is-on');
        document.documentElement.classList.add('fin-loading');
        failSafe = setTimeout(hide, FAILSAFE_MS);
    }

    function hide() {
        clearTimeout(showTimer);
        clearTimeout(failSafe);
        showTimer = null;
        el.classList.remove('is-on');
        document.documentElement.classList.remove('fin-loading');
        // Wait out the fade before removing it from the a11y tree.
        setTimeout(function () {
            if (!el.classList.contains('is-on')) el.hidden = true;
        }, 260);
    }

    function scheduleFor(path) {
        clearTimeout(showTimer);
        var message = copyFor(path || '/');
        showTimer = setTimeout(function () { show(message); }, GRACE_MS);
    }

    // ── What must NOT trigger the loader ─────────────────────
    // Anything that does not replace the current document: downloads, new tabs,
    // in-page anchors, Bootstrap toggles, and modified clicks.
    function isPlainNavigation(a, e) {
        if (e.defaultPrevented) return false;
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return false;
        if (a.hasAttribute('download')) return false;
        if (a.target && a.target !== '_self') return false;
        if (a.hasAttribute('data-bs-toggle') || a.hasAttribute('data-inline-confirm')) return false;
        if (a.getAttribute('data-no-loader') !== null) return false;

        var href = a.getAttribute('href') || '';
        if (!href || href.charAt(0) === '#') return false;
        if (/^(mailto:|tel:|javascript:|blob:|data:)/i.test(href)) return false;

        var url;
        try { url = new URL(a.href, location.href); } catch (_) { return false; }
        if (url.origin !== location.origin) return false;
        // Same page, different hash — no document change.
        if (url.pathname === location.pathname && url.search === location.search && url.hash) return false;
        // Exports stream a file; the page stays where it is.
        if (/\/(export|download|backup)\b/i.test(url.pathname)) return false;

        return url;
    }

    document.addEventListener('click', function (e) {
        var a = e.target.closest && e.target.closest('a[href]');
        if (!a) return;
        var url = isPlainNavigation(a, e);
        if (!url) return;
        scheduleFor(url.pathname);
    }, true);

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (e.defaultPrevented) return;
        if (!form || form.getAttribute('data-no-loader') !== null) return;
        // Forms the page handles itself (Events board, quick add) never navigate.
        if (form.hasAttribute('data-partial') || form.classList.contains('js-async')) return;
        var action = form.getAttribute('action') || location.pathname;
        if (/\/(export|download|backup|email)\b/i.test(action)) return;
        try { scheduleFor(new URL(action, location.href).pathname); }
        catch (_) { scheduleFor(location.pathname); }
    }, true);

    // Restoring from the back/forward cache shows a stale overlay otherwise.
    window.addEventListener('pageshow', hide);
    window.addEventListener('pagehide', function () { clearTimeout(showTimer); });

    window.finomaLoader = { show: show, hide: hide };
})();
