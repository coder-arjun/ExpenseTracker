// =============================================================
// Quick-Add FAB + Smart Entry
//   Press N (or click the FAB) to open the sheet.
//   Type: "1850 groceries food" → amount, description, category.
//   The parser is lenient: amount can be anywhere with digits; the
//   trailing word(s) that match a known category name win.
// =============================================================
(function () {
    'use strict';

    var fab = document.getElementById('qaFab');
    var sheet = document.getElementById('qaSheet');
    if (!fab || !sheet) return;

    var input = document.getElementById('qaInput');
    var chipsEl = document.getElementById('qaChips');
    var previewEl = document.getElementById('qaPreview');
    var submitBtn = document.getElementById('qaSubmit');
    var cancelBtn = document.getElementById('qaCancel');

    var categories = []; // [{ id, name }]
    var selectedCatId = null;

    // ---- API ---------------------------------------------------------------
    function antiForgeryToken() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    async function loadCategories() {
        try {
            var r = await fetch('/QuickAdd/Categories', { credentials: 'same-origin' });
            if (!r.ok) return;
            categories = await r.json();
            renderChips();
        } catch (e) {
            console.warn('quick-add: failed to load categories', e);
        }
    }

    function renderChips() {
        chipsEl.innerHTML = '';
        categories.forEach(function (c) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'qa-chip';
            b.textContent = c.name;
            b.dataset.id = c.id;
            b.addEventListener('click', function () {
                selectedCatId = (selectedCatId === c.id) ? null : c.id;
                updateChipActive();
                updatePreview();
            });
            chipsEl.appendChild(b);
        });
    }

    function updateChipActive() {
        chipsEl.querySelectorAll('.qa-chip').forEach(function (chip) {
            chip.classList.toggle('active', String(selectedCatId) === chip.dataset.id);
        });
    }

    // ---- Parser ------------------------------------------------------------
    // Returns { amount, description, categoryId }.
    function parse(raw) {
        var s = (raw || '').trim();
        if (!s) return { amount: null, description: '', categoryId: null };

        // 1. Find a category name appearing as a whole-word substring (case-insensitive).
        //    Longest match wins so "personal loan" beats "loan".
        var lowerS = s.toLowerCase();
        var bestCat = null;
        categories.forEach(function (c) {
            var idx = lowerS.indexOf(c.name.toLowerCase());
            if (idx < 0) return;
            // Whole-word check
            var before = idx === 0 ? ' ' : lowerS[idx - 1];
            var after = (idx + c.name.length >= lowerS.length) ? ' ' : lowerS[idx + c.name.length];
            if (/[^a-z0-9]/i.test(before) && /[^a-z0-9]/i.test(after)) {
                if (!bestCat || c.name.length > bestCat.name.length) bestCat = c;
            }
        });

        // 2. Pull out the first decimal number as amount.
        var amount = null;
        var amountMatch = s.match(/\b\d+(?:[.,]\d+)?\b/);
        if (amountMatch) amount = parseFloat(amountMatch[0].replace(',', '.'));

        // 3. Description = remainder after stripping amount and matched-category text.
        var desc = s;
        if (amountMatch) desc = desc.replace(amountMatch[0], ' ');
        if (bestCat) {
            var re = new RegExp('\\b' + bestCat.name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b', 'i');
            desc = desc.replace(re, ' ');
        }
        desc = desc.replace(/\s+/g, ' ').trim();

        return {
            amount: amount,
            description: desc,
            categoryId: bestCat ? bestCat.id : (selectedCatId || null),
            categoryName: bestCat ? bestCat.name
                          : (selectedCatId ? (categories.find(c => c.id === selectedCatId) || {}).name : null)
        };
    }

    function fmtINR(n) {
        try {
            return n.toLocaleString('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
        } catch (e) {
            return '₹' + n;
        }
    }

    function updatePreview() {
        var p = parse(input.value);
        if (p.amount == null && !p.description && !p.categoryId) {
            previewEl.innerHTML = 'Start typing to see a preview…';
            submitBtn.disabled = true;
            return;
        }
        var parts = [];
        if (p.amount != null) parts.push('<strong>' + fmtINR(p.amount) + '</strong>');
        if (p.categoryName) parts.push('in <strong>' + escapeHtml(p.categoryName) + '</strong>');
        if (p.description) parts.push('for <em>' + escapeHtml(p.description) + '</em>');
        previewEl.innerHTML = parts.length ? parts.join(' ') : 'Add a category or description.';
        submitBtn.disabled = !(p.amount != null && p.amount > 0 && p.categoryId);

        // Sync the chips with auto-detected category
        if (p.categoryId && selectedCatId !== p.categoryId) {
            selectedCatId = p.categoryId;
            updateChipActive();
        }
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
        });
    }

    // ---- Submit -----------------------------------------------------------
    async function submit() {
        var p = parse(input.value);
        if (p.amount == null || p.amount <= 0 || !p.categoryId) return;
        submitBtn.disabled = true;
        try {
            var r = await fetch('/QuickAdd/Expense', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiForgeryToken(),
                },
                credentials: 'same-origin',
                body: JSON.stringify({
                    Amount: p.amount,
                    Description: p.description || null,
                    CategoryId: p.categoryId,
                }),
            });
            if (r.ok) {
                close();
                showToast('Expense added ✓');
                // Refresh after a short delay so the toast is visible
                setTimeout(function () { location.reload(); }, 400);
            } else {
                var msg = await r.text();
                showToast('Failed: ' + (msg || r.status));
                submitBtn.disabled = false;
            }
        } catch (e) {
            showToast('Network error');
            submitBtn.disabled = false;
        }
    }

    // ---- Toast (delegates to global toast() from site.js) -----------------
    function showToast(msg, kind) {
        if (typeof window.toast === 'function') return window.toast(msg, kind || 'success');
        // Fallback if site.js hasn't loaded yet
        alert(msg);
    }

    // ---- Focus trap inside the modal sheet --------------------------------
    function trapFocus(e) {
        if (!sheet.classList.contains('show')) return;
        if (e.key !== 'Tab') return;
        var focusables = sheet.querySelectorAll(
            'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (focusables.length === 0) return;
        var first = focusables[0];
        var last  = focusables[focusables.length - 1];
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault(); last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault(); first.focus();
        }
    }
    document.addEventListener('keydown', trapFocus);

    // ---- Sheet open/close -------------------------------------------------
    function open() {
        sheet.classList.add('show');
        sheet.setAttribute('aria-hidden', 'false');
        if (categories.length === 0) loadCategories();
        setTimeout(function () { input.focus(); input.select(); }, 50);
        updatePreview();
    }
    function close() {
        sheet.classList.remove('show');
        sheet.setAttribute('aria-hidden', 'true');
        input.value = '';
        selectedCatId = null;
        updateChipActive();
        updatePreview();
        submitBtn.disabled = true;
    }

    fab.addEventListener('click', open);
    cancelBtn.addEventListener('click', close);
    submitBtn.addEventListener('click', submit);
    sheet.addEventListener('click', function (e) {
        if (e.target === sheet) close();
    });
    input.addEventListener('input', updatePreview);
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); submit(); }
        if (e.key === 'Escape') { e.preventDefault(); close(); }
    });

    // Global "N" shortcut (when nothing else is focused)
    document.addEventListener('keydown', function (e) {
        if (sheet.classList.contains('show')) return;
        var tag = (document.activeElement && document.activeElement.tagName) || '';
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
        if (e.key === 'n' || e.key === 'N') {
            if (e.ctrlKey || e.metaKey || e.altKey) return;
            e.preventDefault();
            open();
        }
    });
})();
