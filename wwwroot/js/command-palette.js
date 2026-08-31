// =============================================================
// Command palette (Ctrl/Cmd + K)
//   Hits /Search?q=… and renders three groups: Navigation, Categories, Expenses.
//   Keyboard navigable: ↑ ↓ ↵ Esc.
// =============================================================
(function () {
    'use strict';

    var overlay = document.getElementById('cmdkOverlay');
    if (!overlay) return;
    var input = document.getElementById('cmdkInput');
    var resultsEl = document.getElementById('cmdkResults');
    var trigger = document.getElementById('cmdkTrigger');

    var items = [];        // flattened, in display order — for keyboard nav
    var activeIdx = -1;
    var lastQuery = '';
    var fetchToken = 0;

    // ---- Open / close -----------------------------------------------------
    function open() {
        overlay.classList.add('show');
        overlay.setAttribute('aria-hidden', 'false');
        setTimeout(function () { input.focus(); input.select(); }, 30);
        if (input.value) doSearch(input.value);
        else renderEmpty('Type to search.');
    }
    function close() {
        overlay.classList.remove('show');
        overlay.setAttribute('aria-hidden', 'true');
        activeIdx = -1;
    }

    if (trigger) trigger.addEventListener('click', open);
    overlay.addEventListener('click', function (e) {
        if (e.target === overlay) close();
    });

    document.addEventListener('keydown', function (e) {
        var isCmdK = (e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K');
        if (isCmdK) {
            e.preventDefault();
            overlay.classList.contains('show') ? close() : open();
            return;
        }
        // Forward slash also opens — common pattern (GitHub, Linear).
        if (e.key === '/' && !overlay.classList.contains('show')) {
            var tag = (document.activeElement && document.activeElement.tagName) || '';
            if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
            e.preventDefault();
            open();
        }
    });

    // ---- Search -----------------------------------------------------------
    var debounce = null;
    input.addEventListener('input', function () {
        clearTimeout(debounce);
        var q = input.value.trim();
        debounce = setTimeout(function () { doSearch(q); }, 120);
    });
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') { e.preventDefault(); close(); return; }
        if (e.key === 'ArrowDown') { e.preventDefault(); moveActive(1); return; }
        if (e.key === 'ArrowUp')   { e.preventDefault(); moveActive(-1); return; }
        if (e.key === 'Enter')     { e.preventDefault(); openActive(); return; }
    });

    async function doSearch(q) {
        lastQuery = q;
        var myToken = ++fetchToken;
        if (!q) { renderEmpty('Type to search.'); return; }
        try {
            var r = await fetch('/Search?q=' + encodeURIComponent(q), { credentials: 'same-origin' });
            if (myToken !== fetchToken) return; // newer request in flight
            if (!r.ok) { renderEmpty('Search failed.'); return; }
            var data = await r.json();
            render(data);
        } catch (e) {
            renderEmpty('Search failed.');
        }
    }

    // ---- Render -----------------------------------------------------------
    function render(data) {
        resultsEl.innerHTML = '';
        items = [];
        var any = false;

        function group(label, rows, build) {
            if (!rows || rows.length === 0) return;
            any = true;
            var h = document.createElement('div');
            h.className = 'cmdk-group-label';
            h.textContent = label;
            resultsEl.appendChild(h);
            rows.forEach(function (row) {
                var a = build(row);
                a.classList.add('cmdk-item');
                a.setAttribute('role', 'option');
                a.addEventListener('mouseenter', function () { setActive(items.indexOf(a)); });
                resultsEl.appendChild(a);
                items.push(a);
            });
        }

        group('Navigation', data.navigation, function (n) {
            var a = document.createElement('a');
            a.href = n.url;
            a.innerHTML = '<i class="bi bi-' + escapeAttr(n.icon || 'arrow-right') + '"></i><span>' + escapeHtml(n.label) + '</span>';
            return a;
        });
        group('Categories', data.categories, function (c) {
            var a = document.createElement('a');
            a.href = c.url;
            a.innerHTML = '<i class="bi bi-tag"></i><span>' + escapeHtml(c.name) + '</span><span class="cmdk-item-meta">' + escapeHtml(c.type) + '</span>';
            return a;
        });
        group('Expenses', data.expenses, function (x) {
            var a = document.createElement('a');
            a.href = x.url;
            a.innerHTML = '<i class="bi bi-cash"></i><span>' + escapeHtml(x.description || '(no description)')
                          + '</span><span class="cmdk-item-meta">' + escapeHtml(x.amount) + ' · ' + escapeHtml(x.month) + '</span>';
            return a;
        });

        group('Events', data.events, function (e) {
            var a = document.createElement('a');
            a.href = e.url;
            a.innerHTML = '<i class="bi bi-calendar-event"></i><span>' + escapeHtml(e.name)
                          + '</span><span class="cmdk-item-meta">' + escapeHtml(e.context) + ' · ' + escapeHtml(e.amount) + '</span>';
            return a;
        });

        if (!any) renderEmpty('No matches for "' + lastQuery + '"');
        else setActive(0);
    }

    function renderEmpty(msg) {
        resultsEl.innerHTML = '';
        items = [];
        var d = document.createElement('div');
        d.className = 'cmdk-empty';
        d.textContent = msg;
        resultsEl.appendChild(d);
        activeIdx = -1;
    }

    function setActive(i) {
        if (i < 0 || i >= items.length) return;
        items.forEach(function (el, idx) {
            el.classList.toggle('active', idx === i);
        });
        items[i].scrollIntoView({ block: 'nearest' });
        activeIdx = i;
    }
    function moveActive(delta) {
        if (items.length === 0) return;
        var next = activeIdx + delta;
        if (next < 0) next = items.length - 1;
        if (next >= items.length) next = 0;
        setActive(next);
    }
    function openActive() {
        if (activeIdx < 0 || activeIdx >= items.length) return;
        var url = items[activeIdx].getAttribute('href');
        if (url) window.location.href = url;
    }

    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
        });
    }
    function escapeAttr(s) { return escapeHtml(s); }
})();
