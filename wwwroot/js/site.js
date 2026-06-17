// =============================================================
// ExpenseTracker — base UI behaviour (v2)
//   * theme toggle, syncs data-theme + data-bs-theme + system mode
//   * toast notifications (replaces inline alert flashes)
//   * skip-link focus management
//   * IntersectionObserver-driven card fade-in
//   * count-up animation for KPI tiles
//   * inline-confirm pattern for destructive actions
// =============================================================
(function () {
    'use strict';

    // ─── Theme toggle ──────────────────────────────────────────
    function isDarkActive() {
        return document.documentElement.getAttribute('data-theme') === 'dark';
    }
    function applyTheme(mode) {
        // mode = 'light' | 'dark' | 'system'
        var resolved = mode === 'system'
            ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
            : mode;
        document.documentElement.setAttribute('data-theme', resolved);
        document.documentElement.setAttribute('data-bs-theme', resolved);
        document.documentElement.setAttribute('data-color-scheme', mode);
        if (mode === 'system') localStorage.removeItem('theme');
        else localStorage.setItem('theme', resolved);
    }
    function setToggleState(toggle) {
        if (!toggle) return;
        var dark = isDarkActive();
        toggle.setAttribute('aria-pressed', dark ? 'true' : 'false');
        toggle.setAttribute('aria-label', dark ? 'Switch to light mode' : 'Switch to dark mode');
        toggle.setAttribute('title', dark ? 'Switch to light mode' : 'Switch to dark mode');
    }
    function wireThemeToggle() {
        var toggle = document.getElementById('themeToggle');
        if (!toggle) return;
        setToggleState(toggle);
        toggle.addEventListener('click', function () {
            applyTheme(isDarkActive() ? 'light' : 'dark');
            setToggleState(toggle);
        });
        if (window.matchMedia) {
            var mq = window.matchMedia('(prefers-color-scheme: dark)');
            mq.addEventListener && mq.addEventListener('change', function () {
                if (document.documentElement.getAttribute('data-color-scheme') === 'system') {
                    applyTheme('system');
                    setToggleState(toggle);
                }
            });
        }
    }

    // ─── Skip-link focus ───────────────────────────────────────
    function wireSkipLink() {
        var skip = document.querySelector('a.skip-link');
        var main = document.getElementById('main-content');
        if (!skip || !main) return;
        skip.addEventListener('click', function () {
            main.setAttribute('tabindex', '-1');
            main.focus();
            main.addEventListener('blur', function once() {
                main.removeAttribute('tabindex');
                main.removeEventListener('blur', once);
            });
        });
    }

    // ─── Toast system ──────────────────────────────────────────
    // Public API: window.toast(text, kind = 'info'|'success'|'error'|'warn', ms)
    function toast(text, kind, ms) {
        var host = document.getElementById('toastHost');
        if (!host) return;
        var el = document.createElement('div');
        el.className = 'toast-msg ' + (kind || 'info');
        el.setAttribute('role', kind === 'error' ? 'alert' : 'status');
        el.textContent = text;
        host.appendChild(el);
        var lifetime = ms || (kind === 'error' ? 5000 : 3500);
        setTimeout(function () {
            el.classList.add('leaving');
            setTimeout(function () { el.remove(); }, 250);
        }, lifetime);
    }
    window.toast = toast;

    function flushServerToasts() {
        var node = document.getElementById('serverToasts');
        if (!node) return;
        try {
            var data = JSON.parse(node.textContent || '{}');
            (data.messages || []).forEach(function (m) { toast(m.text, m.kind); });
        } catch (e) { /* ignore */ }
        node.remove();
    }

    // ─── Card fade-in on scroll (signature motion #2) ─────────
    function wireFadeIn() {
        if (!('IntersectionObserver' in window)) return;
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) {
                if (e.isIntersecting) {
                    e.target.classList.add('fade-in-up');
                    io.unobserve(e.target);
                }
            });
        }, { rootMargin: '0px 0px -8% 0px', threshold: 0.05 });
        document.querySelectorAll('[data-fade-in]').forEach(function (el) { io.observe(el); });
    }

    // ─── Count-up numbers (signature motion #1) ───────────────
    // Mark KPI <span data-countup="123456" data-countup-format="currency">
    function easeOutCubic(t) { return 1 - Math.pow(1 - t, 3); }
    function formatINR(n) {
        try { return n.toLocaleString('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }); }
        catch (_) { return Math.round(n).toLocaleString('en-IN'); }
    }
    function animateCountUp(el) {
        var target = parseFloat(el.dataset.countup);
        if (isNaN(target)) return;
        var dur = parseInt(el.dataset.countupMs || '900', 10);
        var format = el.dataset.countupFormat || 'number';
        var start = performance.now();
        function tick(now) {
            var t = Math.min(1, (now - start) / dur);
            var v = target * easeOutCubic(t);
            el.textContent = format === 'currency' ? formatINR(v)
                          : format === 'percent'   ? Math.round(v) + '%'
                          : Math.round(v).toLocaleString('en-IN');
            if (t < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
    }
    function wireCountUps() {
        var reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        document.querySelectorAll('[data-countup]').forEach(function (el) {
            if (reduced) {
                var target = parseFloat(el.dataset.countup);
                el.textContent = el.dataset.countupFormat === 'currency' ? formatINR(target) : Math.round(target).toLocaleString('en-IN');
                return;
            }
            animateCountUp(el);
        });
    }

    // ─── Inline confirm (replaces some Delete pages) ──────────
    // Markup:
    //   <span class="inline-confirm">
    //     <button class="btn btn-sm btn-outline-danger" data-inline-confirm>
    //       <i class="bi bi-trash"></i>
    //     </button>
    //     <span class="inline-confirm-bubble">
    //       <div class="inline-confirm-text">Delete this expense?</div>
    //       <form method="post" action="/Expenses/Delete/123">
    //         <input type="hidden" name="__RequestVerificationToken" value="..." />
    //         <input type="hidden" name="id" value="123" />
    //         <button class="btn btn-sm btn-danger">Yes, delete</button>
    //         <button type="button" class="btn btn-sm btn-link inline-confirm-cancel">Cancel</button>
    //       </form>
    //     </span>
    //   </span>
    function wireInlineConfirms() {
        document.addEventListener('click', function (e) {
            // Toggle
            var trigger = e.target.closest('[data-inline-confirm]');
            if (trigger) {
                var host = trigger.closest('.inline-confirm');
                if (host) {
                    // Close any others first
                    document.querySelectorAll('.inline-confirm.open').forEach(function (n) {
                        if (n !== host) n.classList.remove('open');
                    });
                    host.classList.toggle('open');
                }
                return;
            }
            // Cancel
            if (e.target.closest('.inline-confirm-cancel')) {
                var h = e.target.closest('.inline-confirm');
                if (h) h.classList.remove('open');
                return;
            }
            // Click-outside closes any open bubble
            if (!e.target.closest('.inline-confirm.open')) {
                document.querySelectorAll('.inline-confirm.open').forEach(function (n) { n.classList.remove('open'); });
            }
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                document.querySelectorAll('.inline-confirm.open').forEach(function (n) { n.classList.remove('open'); });
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        wireThemeToggle();
        wireSkipLink();
        flushServerToasts();
        wireFadeIn();
        wireCountUps();
        wireInlineConfirms();
    });
})();
