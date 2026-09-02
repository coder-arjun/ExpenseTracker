import { chromium } from 'playwright';
import fs from 'fs';

const BASE = 'http://localhost:5238';
const WIDTHS = [320, 390, 576, 600, 768, 900, 991, 992, 1024, 1280, 1920];
const issues = [];
let pagesChecked = 0;

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await ctx.newPage();
page.setDefaultTimeout(20000);

await page.goto(BASE + '/Identity/Account/Login', { waitUntil: 'domcontentloaded' });
await page.fill('input[name="Input.EmailOrUserId"]', 'arjun');
await page.fill('input[name="Input.Password"]', 'Finoma@2026');
await page.click('#login-submit');
await page.waitForLoadState('networkidle');

// ---- discover a real id per section so Edit/Details/Delete are reachable ----
const SECTIONS = ['Expenses','Incomes','Savings','Budget','Categories','Accounts',
                  'Transfers','Goals','Recurring','Debts','Events'];
const ids = {};
await page.setViewportSize({ width: 1280, height: 900 });
for (const s of SECTIONS) {
  await page.goto(`${BASE}/${s}`, { waitUntil: 'networkidle' }).catch(() => {});
  const href = await page.$$eval('a[href]', els => {
    const m = els.map(e => e.getAttribute('href'))
      .filter(h => h && /\/(Edit|Details)\/\d+/.test(h));
    return m[0] || null;
  }).catch(() => null);
  const id = href ? href.match(/\/(\d+)$/)?.[1] : null;
  if (id) ids[s] = id;
}
console.log('discovered ids:', JSON.stringify(ids));

// ---- build the route list ----
const routes = [
  '/Dashboard', '/Insights', '/Search/Results?q=loan',
  '/Identity/Account/Manage', '/Identity/Account/Manage/ChangePassword',
];
for (const s of SECTIONS) {
  routes.push(`/${s}`);
  routes.push(`/${s}/Create`);
  if (ids[s]) {
    routes.push(`/${s}/Edit/${ids[s]}`);
    routes.push(`/${s}/Delete/${ids[s]}`);
  }
}
for (const s of ['Expenses','Incomes','Savings','Debts','Events']) {
  if (ids[s]) routes.push(`/${s}/Details/${ids[s]}`);
}
routes.push('/Goals/Contribute/' + (ids['Goals'] || '1'));
routes.push('/Expenses?selectedCategory=' + '1');
routes.push('/Events?filter=archived');

const seen = new Set();
const ROUTES = routes.filter(r => !seen.has(r) && seen.add(r));
console.log(`sweeping ${ROUTES.length} routes x ${WIDTHS.length} widths\n`);

for (const w of WIDTHS) {
  await page.setViewportSize({ width: w, height: 900 });
  for (const route of ROUTES) {
    let status = 0;
    try {
      const resp = await page.goto(BASE + route, { waitUntil: 'networkidle' });
      status = resp ? resp.status() : 0;
    } catch (e) {
      issues.push({ w, route, kind: 'LOAD', detail: e.message.slice(0, 70) });
      continue;
    }
    if (status >= 400) continue;              // 404 on a missing id is not a layout bug
    pagesChecked++;

    const r = await page.evaluate(() => {
      const de = document.documentElement;
      const vw = de.clientWidth;
      const out = { over: de.scrollWidth - de.clientWidth, worst: null, stacks: [], wideTables: [] };

      if (out.over > 0) {
        let worst = null;
        for (const el of document.querySelectorAll('body *')) {
          if (el.closest('.offcanvas')) continue;
          const b = el.getBoundingClientRect();
          if (b.width === 0 && b.height === 0) continue;
          if (b.right > vw + 0.5) {
            const d = Math.round(b.right - vw);
            if (!worst || d > worst.by) worst = {
              by: d,
              sel: el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + '.' +
                   (el.className || '').toString().split(/\s+/).filter(Boolean).slice(0, 3).join('.')
            };
          }
        }
        out.worst = worst;
      }

      // A run of >=3 stacked blocks that each lead with a big number reads as the
      // "unprofessional vertical list" — the exact pattern reported on /Events.
      for (const el of document.querySelectorAll('div, section, ul')) {
        const cs = getComputedStyle(el);
        if (cs.display !== 'grid' && cs.display !== 'flex') continue;
        const kids = [...el.children].filter(k => k.getBoundingClientRect().height > 0);
        if (kids.length < 3) continue;
        const box = el.getBoundingClientRect();
        if (box.width < 200) continue;
        const fullWidth = kids.every(k => k.getBoundingClientRect().width > box.width * 0.92);
        if (!fullWidth) continue;
        // does each child lead with a large numeric figure?
        const bigNum = kids.filter(k => {
          const t = (k.textContent || '').trim();
          if (!/[\d]/.test(t) || t.length > 60) return false;
          const cand = k.querySelector('span, div, dd, strong');
          if (!cand) return false;
          const fs = parseFloat(getComputedStyle(cand).fontSize);
          return fs >= 17;
        }).length;
        // A label/value ROW (caption and figure on one baseline) is the fixed form,
        // not the bug. Only flag children whose figure and caption are truly stacked.
        const stacked = kids.filter(k => {
          const v = k.querySelector('span, div, dd, strong');
          if (!v) return false;
          const other = [...k.children].find(x => x !== v && x.getBoundingClientRect().height > 0);
          if (!other) return true;
          return Math.abs(v.getBoundingClientRect().top - other.getBoundingClientRect().top) >= 14;
        }).length;
        if (bigNum >= 3 && bigNum === kids.length && stacked === kids.length) {
          out.stacks.push({
            sel: el.tagName.toLowerCase() + '.' + (el.className || '').toString().split(/\s+/).filter(Boolean).slice(0, 2).join('.'),
            n: kids.length, h: Math.round(box.height)
          });
        }
      }

      for (const t of document.querySelectorAll('table')) {
        const p = t.parentElement;
        if (!p) continue;
        const pc = getComputedStyle(p);
        const scrolls = /auto|scroll/.test(pc.overflowX);
        if (!scrolls && t.getBoundingClientRect().width > p.getBoundingClientRect().width + 1) {
          out.wideTables.push(t.className || '(table)');
        }
      }
      return out;
    });

    if (r.over > 0) issues.push({ w, route, kind: 'OVERFLOW', detail: `${r.over}px` + (r.worst ? ` — ${r.worst.sel} (+${r.worst.by}px)` : '') });
    for (const s of r.stacks) if (w <= 480) issues.push({ w, route, kind: 'STACKED', detail: `${s.sel} — ${s.n} figures stacked, ${s.h}px tall` });
    for (const t of r.wideTables) issues.push({ w, route, kind: 'TABLE', detail: t });
  }
  process.stdout.write(`  ${w}px done\n`);
}

// ---- public pages, signed out ----
const anon = await browser.newContext();
const ap = await anon.newPage();
for (const w of WIDTHS) {
  await ap.setViewportSize({ width: w, height: 900 });
  for (const route of ['/', '/Identity/Account/Login', '/Identity/Account/Register',
                       '/Identity/Account/ForgotPassword']) {
    try { await ap.goto(BASE + route, { waitUntil: 'networkidle' }); } catch { continue; }
    pagesChecked++;
    const over = await ap.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (over > 0) issues.push({ w, route: '(anon) ' + route, kind: 'OVERFLOW', detail: over + 'px' });
  }
}
await anon.close();

await browser.close();

console.log(`\nchecked ${pagesChecked} page renders`);
if (issues.length === 0) {
  console.log('\nNO RESPONSIVENESS ISSUES FOUND');
} else {
  console.log(`\n${issues.length} ISSUE(S):\n`);
  const byKind = {};
  for (const i of issues) (byKind[i.kind] ||= []).push(i);
  for (const kind of Object.keys(byKind)) {
    console.log(`--- ${kind} (${byKind[kind].length}) ---`);
    for (const i of byKind[kind]) console.log(`  ${String(i.w).padStart(4)}px  ${i.route.padEnd(38)} ${i.detail}`);
  }
}
fs.writeFileSync(process.argv[2] || 'sweep.json', JSON.stringify(issues, null, 2));
process.exit(issues.length === 0 ? 0 : 1);
