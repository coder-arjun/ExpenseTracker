/*
    Event budgets — workspace behaviour for /Events/Details.

    Progressive enhancement, not a JS app:
      • Every control on the page is a real <form> posting to a real MVC action, so the
        whole feature works with this file blocked or broken.
      • The entry sheets are <dialog open>, which lay out inline as ordinary panels
        without JS. Here we close them and reopen them as true modals on demand.
      • On submit we post the form's own FormData (the antiforgery token rides along in
        it) with an X-Partial header. The server answers with the re-rendered board
        fragment, which we swap in. Razor stays the single source of truth for markup —
        there is no templating in this file.

    No jQuery. No build step.
*/
(function () {
    'use strict';

    const board = document.getElementById('ev-board');
    if (!board) return;

    const sheets = document.querySelector('.ev-sheets');
    const DIALOGS = {
        subevent: document.getElementById('dlgSubEvent'),
        spend: document.getElementById('dlgSpend'),
        'edit-spend': document.getElementById('dlgEditSpend'),
        contribution: document.getElementById('dlgContribution')
    };

    // Tell CSS that the enhanced path is live, so the inline-fallback styling drops away.
    document.documentElement.classList.add('ev-js');

    // The dialogs ship open (the no-JS fallback). Close them now that we can manage them.
    Object.values(DIALOGS).forEach(function (d) {
        if (d && d.open) d.close();
    });

    const toast = function (text, kind) {
        if (typeof window.toast === 'function') window.toast(text, kind);
    };

    // ── opening a sheet ──────────────────────────────────────────────
    function openDialog(dialog) {
        if (!dialog) return;
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');           // very old browsers: inline panel
        const first = dialog.querySelector('input:not([type=hidden]), select, textarea');
        if (first) first.focus();
    }

    /**
     * Refill the spend sheet's sub-event picker straight from the board that is
     * currently on screen, so it can never drift out of sync after a swap.
     */
    function syncSubEventOptions(preselectId) {
        const select = document.querySelector('[data-ev-subselect]');
        if (!select) return;

        const rows = board.querySelectorAll('[data-subevent-id]');
        select.innerHTML = '';
        if (rows.length === 0) {
            select.insertAdjacentHTML('beforeend', '<option value="">Add a sub-event first</option>');
            return;
        }
        rows.forEach(function (row) {
            const option = document.createElement('option');
            option.value = row.dataset.subeventId;
            option.textContent = row.dataset.subeventName;
            select.appendChild(option);
        });
        if (preselectId) select.value = preselectId;
    }

    document.addEventListener('click', function (e) {
        const opener = e.target.closest('[data-ev-open]');
        if (opener) {
            const key = opener.dataset.evOpen;
            const dialog = DIALOGS[key];
            if (!dialog) return;

            if (key === 'spend') {
                syncSubEventOptions(opener.dataset.subId);
                const form = dialog.querySelector('form');
                if (form) {
                    form.querySelector('[name=amount]').value = '';
                    form.querySelector('[name=paidTo]').value = '';
                    form.querySelector('[name=note]').value = '';
                }
            }

            if (key === 'edit-spend') {
                const d = opener.dataset;
                const form = dialog.querySelector('form');
                form.querySelector('[data-ev-field=id]').value = d.spendId;
                form.querySelector('[data-ev-field=amount]').value = d.amount;
                form.querySelector('[data-ev-field=date]').value = d.date;
                form.querySelector('[data-ev-field=paidTo]').value = d.paidTo || '';
                form.querySelector('[data-ev-field=note]').value = d.note || '';
            }

            openDialog(dialog);
            return;
        }

        const closer = e.target.closest('[data-ev-close]');
        if (closer) {
            const dialog = closer.closest('dialog');
            if (dialog && dialog.open) dialog.close();
        }
    });

    // Click on the backdrop closes the sheet.
    Object.values(DIALOGS).forEach(function (dialog) {
        if (!dialog) return;
        dialog.addEventListener('click', function (e) {
            if (e.target === dialog) dialog.close();
        });
    });

    // ── swapping the board ───────────────────────────────────────────
    /** Which sub-events the user had expanded, so a swap doesn't collapse their place. */
    function openRowIds() {
        return Array.from(board.querySelectorAll('details.ev-row[open]'))
            .map(function (d) { return d.dataset.subeventId; });
    }

    function restoreRows(ids) {
        ids.forEach(function (id) {
            const row = board.querySelector('details.ev-row[data-subevent-id="' + id + '"]');
            if (row) row.open = true;
        });
    }

    function applyBoard(html) {
        const expanded = openRowIds();
        const swap = function () {
            board.innerHTML = html;
            restoreRows(expanded);
            const flash = board.querySelector('.ev-board');
            const message = flash && flash.dataset.evFlash;
            if (message) toast(message, 'success');
        };

        if (typeof document.startViewTransition === 'function') {
            document.startViewTransition(swap);
        } else {
            swap();
        }
    }

    // ── submitting ───────────────────────────────────────────────────
    function setBusy(form, busy) {
        const button = form.querySelector('button[type=submit]');
        if (button) button.disabled = busy;
        form.classList.toggle('is-busy', busy);
    }

    document.addEventListener('submit', async function (e) {
        const form = e.target.closest('form.js-ev-form');
        if (!form) return;

        const confirmText = form.dataset.confirm;
        if (confirmText && !window.confirm(confirmText)) {
            e.preventDefault();
            return;
        }

        e.preventDefault();
        setBusy(form, true);

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),           // carries __RequestVerificationToken
                headers: { 'X-Partial': '1' },
                credentials: 'same-origin'
            });

            if (response.status === 400) {
                const data = await response.json().catch(function () { return null; });
                toast((data && data.error) || 'That could not be saved.', 'error');
                return;
            }

            if (!response.ok) {
                toast('Something went wrong — reloading.', 'error');
                window.location.reload();
                return;
            }

            const html = await response.text();

            // A session timeout redirects to the login page, which is also 200 + HTML.
            // Only swap when we actually got the board back.
            if (html.indexOf('class="ev-board"') === -1) {
                window.location.reload();
                return;
            }

            const dialog = form.closest('dialog');
            if (dialog && dialog.open) {
                dialog.close();
                form.reset();
            }

            applyBoard(html);
        } catch (err) {
            // Offline or a network blip — fall back to a plain post so nothing is lost.
            form.submit();
        } finally {
            setBusy(form, false);
        }
    });
})();
