// Demo walkthrough recording for ExpenseTracker.
// Captures: signup → dashboard → income → expense → filter+total →
//           categories → budgets → insights.
// Caption banner is injected via addInitScript so it survives navigation.

const { test } = require('@playwright/test');

// Stable, repeatable per-run identity. Timestamp guarantees no collision
// with an existing AspNetUsers row across re-recordings.
const STAMP = Date.now().toString().slice(-9);
const DEMO_USERID = `demo_${STAMP}`;
const DEMO_EMAIL = `demo_${STAMP}@example.com`;
const DEMO_PASSWORD = 'Demo@2026Pass';

// ---- Caption banner helpers -----------------------------------------------
async function installBanner(page) {
  // Runs before every navigation, so the helper is always present.
  await page.addInitScript(() => {
    window.__demoBanner = function (text) {
      const apply = () => {
        let b = document.getElementById('__demo-banner');
        if (!b) {
          b = document.createElement('div');
          b.id = '__demo-banner';
          b.style.cssText = [
            'position:fixed', 'left:0', 'right:0', 'bottom:0',
            'z-index:2147483647', 'padding:22px 48px',
            'background:linear-gradient(135deg,#0f172a 0%,#1e3a8a 50%,#2563eb 100%)',
            'color:white', 'font-family:Inter,-apple-system,Segoe UI,Roboto,sans-serif',
            'font-size:26px', 'font-weight:600', 'letter-spacing:0.2px',
            'text-align:center', 'box-shadow:0 -8px 30px rgba(0,0,0,0.35)',
            'transition:opacity 220ms ease', 'opacity:0',
            'border-top:3px solid rgba(255,255,255,0.25)',
          ].join(';');
          document.body.appendChild(b);
        }
        b.textContent = text;
        // Force reflow then fade in
        // eslint-disable-next-line no-unused-expressions
        b.offsetHeight;
        b.style.opacity = '1';
      };
      if (document.body) apply();
      else document.addEventListener('DOMContentLoaded', apply);
    };
    window.__demoHide = function () {
      const b = document.getElementById('__demo-banner');
      if (b) b.style.opacity = '0';
    };
  });
}

async function caption(page, text, dwellMs = 1800) {
  await page.evaluate((t) => window.__demoBanner(t), text);
  await page.waitForTimeout(dwellMs);
}

test('ExpenseTracker client walkthrough', async ({ page }) => {
  await installBanner(page);

  // ── 1. Landing ────────────────────────────────────────────────────────
  await page.goto('/');
  await caption(page, 'ExpenseTracker — take control of your personal finances.', 2200);

  // ── 2. Register a fresh demo user ─────────────────────────────────────
  await page.goto('/Identity/Account/Register');
  await caption(page, 'Sign up in seconds — no email confirmation required.', 1800);

  await page.locator('input[name="Input.UserId"]').fill(DEMO_USERID);
  await page.locator('input[name="Input.Email"]').fill(DEMO_EMAIL);
  await page.locator('input[name="Input.Password"]').fill(DEMO_PASSWORD);
  await page.locator('input[name="Input.ConfirmPassword"]').fill(DEMO_PASSWORD);
  await caption(page, 'Your data stays local — nothing is sent to third parties.', 1600);
  await page.locator('#registerSubmit').click();
  await page.waitForLoadState('networkidle');

  // ── 3. Dashboard ──────────────────────────────────────────────────────
  await caption(page, 'Welcome to your personal finance dashboard.', 2400);

  // ── 4. Add an income ──────────────────────────────────────────────────
  await page.locator('a.nav-link:has-text("Incomes")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Log income from any source — salary, freelance, gifts, more.', 1800);

  await page.locator('a:has-text("Add Income")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Categorise each entry so the reports stay meaningful.', 1500);
  await page.locator('input[name="Amount"]').fill('85000');
  await page.locator('input[name="Source"]').fill('June Salary');
  // Category dropdown is optional for income — pick "Salary" if present.
  try {
    await page.locator('select[name="CategoryId"]').selectOption({ label: 'Salary' });
  } catch (_) { /* swallow if option missing */ }
  await page.locator('button[type="submit"]:has-text("Create")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Recorded — with totals and filters out of the box.', 1800);

  // ── 5. Add an expense ─────────────────────────────────────────────────
  await page.locator('a.nav-link:has-text("Expenses")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Track every expense — one click per entry.', 1600);

  await page.locator('a:has-text("Add Expense")').click();
  await page.waitForLoadState('networkidle');
  await page.locator('input[name="Amount"]').fill('1850');
  await page.locator('input[name="Description"]').fill('Weekly groceries');
  try {
    await page.locator('select[name="CategoryId"]').selectOption({ label: 'Food' });
  } catch (_) { /* Food should always exist as a seeded default */ }
  await page.locator('button[type="submit"]:has-text("Create")').click();
  await page.waitForLoadState('networkidle');

  // Add a second expense in a different category to make the filter meaningful
  await page.locator('a:has-text("Add Expense")').click();
  await page.waitForLoadState('networkidle');
  await page.locator('input[name="Amount"]').fill('4200');
  await page.locator('input[name="Description"]').fill('Electricity bill');
  try {
    await page.locator('select[name="CategoryId"]').selectOption({ label: 'Bills' });
  } catch (_) {}
  await page.locator('button[type="submit"]:has-text("Create")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Filter by category — the running total updates live.', 1800);

  // Demonstrate the category filter on Expenses
  try {
    await page.locator('select[name="selectedCategory"]').first().selectOption({ label: 'Food' });
    await page.locator('button:has-text("Apply")').click();
    await page.waitForLoadState('networkidle');
    await caption(page, 'Filtered view: just Food expenses, with a category-specific total.', 2200);
  } catch (_) { /* selector format may differ across pages */ }

  // ── 6. Categories master ──────────────────────────────────────────────
  await page.locator('a.nav-link:has-text("Categories")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Full category master — add, rename, or remove your own.', 2200);

  // ── 7. Budgets ────────────────────────────────────────────────────────
  await page.locator('a.nav-link:has-text("Budgets")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Set monthly limits — get visual alerts as you approach them.', 2000);

  try {
    await page.locator('a:has-text("Add Budget")').click();
    await page.waitForLoadState('networkidle');
    await page.locator('input[name="Amount"]').fill('5000');
    try {
      await page.locator('select[name="CategoryId"]').selectOption({ label: 'Bills' });
    } catch (_) {}
    await page.locator('button[type="submit"]:has-text("Create")').click();
    await page.waitForLoadState('networkidle');
    await caption(page, 'Budget saved — progress bar tracks your spend in real time.', 2200);
  } catch (_) { /* skip if the layout differs */ }

  // ── 8. Insights (the local-first analyser) ────────────────────────────
  await page.locator('a.nav-link:has-text("Insights")').click();
  await page.waitForLoadState('networkidle');
  await caption(page, 'Insights — plain-English analysis of your money.', 2200);
  await caption(page, 'Computed locally. No third-party API. Your data never leaves the device.', 2400);

  // ── 9. Closing card ───────────────────────────────────────────────────
  await page.goto('/');
  await caption(page, 'Built with .NET 10, EF Core, and a privacy-first design.', 2600);
});
