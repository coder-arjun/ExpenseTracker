using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Backing endpoint for the Cmd/Ctrl-K command palette.
    /// Returns three small groups (navigation, categories, expenses) filtered
    /// by the user's input. Strictly scoped to the current user's data.
    /// </summary>
    [Authorize]
    public class SearchController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        // Static nav targets that the palette can jump to.
        private static readonly (string label, string url, string icon)[] NavTargets =
        {
            ("Dashboard",       "/Dashboard",       "house"),
            ("Expenses",        "/Expenses",        "cash-stack"),
            ("Add Expense",     "/Expenses/Create", "plus-circle"),
            ("Incomes",         "/Incomes",         "graph-up-arrow"),
            ("Add Income",      "/Incomes/Create",  "plus-circle"),
            ("Savings",         "/Savings",         "piggy-bank"),
            ("Budgets",         "/Budget",          "speedometer"),
            ("Categories",      "/Categories",      "tags"),
            ("Add Category",    "/Categories/Create","tag-fill"),
            ("Accounts",        "/Accounts",        "wallet2"),
            ("Add Account",     "/Accounts/Create", "plus-circle"),
            ("Transfers",       "/Transfers",       "arrow-left-right"),
            ("New Transfer",    "/Transfers/Create","arrow-left-right"),
            ("Goals",           "/Goals",           "bullseye"),
            ("Add Goal",        "/Goals/Create",    "plus-circle"),
            ("Recurring",       "/Recurring",       "arrow-repeat"),
            ("Add Recurring",   "/Recurring/Create","plus-circle"),
            ("Owed to Me",      "/Debts",           "cash-coin"),
            ("Event Budgets",   "/Events",          "calendar-event"),
            ("New Event",       "/Events/Create",   "plus-circle"),
            ("Add Debt",        "/Debts/Create",    "plus-circle"),
            ("Insights",        "/Insights",        "lightbulb"),
        };

        public SearchController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET /Search?q=foo  — JSON endpoint (kept for the hidden Ctrl+K palette)
        [HttpGet("/Search")]
        public async Task<IActionResult> Index(string? q)
        {
            q = (q ?? "").Trim();
            if (string.IsNullOrEmpty(q))
                return Json(new { navigation = Array.Empty<object>(), categories = Array.Empty<object>(), expenses = Array.Empty<object>(), debts = Array.Empty<object>(), events = Array.Empty<object>() });

            var data = await FindAsync(q);
            return Json(new
            {
                navigation = data.Nav.Select(n => new { label = n.label, url = n.url, icon = n.icon }),
                categories = data.Categories.Select(c => new { name = c.name, type = c.type, url = c.url }),
                expenses   = data.Expenses.Select(e => new { description = e.description, amount = e.amount, month = e.month, url = e.url }),
                debts      = data.Debts.Select(d => new { person = d.person, direction = d.direction, outstanding = d.outstanding, url = d.url }),
                events     = data.Events.Select(e => new { name = e.name, context = e.context, amount = e.amount, url = e.url }),
            });
        }

        // GET /Search/Results?q=foo  — full-page search results from the navbar search form
        [HttpGet("/Search/Results")]
        public async Task<IActionResult> Results(string? q)
        {
            q = (q ?? "").Trim();
            var vm = new SearchResultsViewModel { Query = q };
            if (string.IsNullOrEmpty(q))
                return View(vm);

            var data = await FindAsync(q);
            vm.Nav = data.Nav.Select(n => new SearchNavHit(n.label, n.url, n.icon)).ToList();
            vm.Categories = data.Categories.Select(c => new SearchCategoryHit(c.name, c.type, c.url)).ToList();
            vm.Expenses = data.Expenses.Select(e => new SearchExpenseHit(e.description, e.amount, e.month, e.url)).ToList();
            vm.Debts = data.Debts.Select(d => new SearchDebtHit(d.person, d.direction, d.outstanding, d.theyOweMe, d.url)).ToList();
            vm.Events = data.Events.Select(e => new SearchEventHit(e.name, e.context, e.amount, e.url)).ToList();
            return View(vm);
        }

        // Shared lookup used by both the JSON endpoint and the page view.
        private async Task<SearchResults> FindAsync(string q)
        {
            var userId = _userManager.GetUserId(User);
            var inr = System.Globalization.CultureInfo.GetCultureInfo("en-IN");

            var nav = NavTargets
                .Where(n => n.label.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .Select(n => (label: n.label, url: n.url, icon: n.icon))
                .ToList();

            // Project raw columns in SQL, then format/compose strings in memory.
            // EF Core cannot translate ToString(format, CultureInfo) and throws on
            // the captured CultureInfo constant — so formatting must happen after
            // the query materialises.
            var catsRaw = await _context.Categories
                .Where(c => c.UserId == userId && EF.Functions.Like(c.Name, $"%{q}%"))
                .OrderBy(c => c.Name)
                .Take(10)
                .Select(c => new { c.Id, c.Name, c.Type })
                .ToListAsync();
            var cats = catsRaw
                .Select(c => (
                    name: c.Name,
                    type: c.Type == CategoryType.Expense ? "Expense" : "Income",
                    url: "/Categories/Edit/" + c.Id))
                .ToList();

            var expRaw = await _context.Expenses
                .Where(e => e.UserId == userId && e.Description != null && EF.Functions.Like(e.Description!, $"%{q}%"))
                .OrderByDescending(e => e.Date)
                .Take(25)
                .Select(e => new { e.Id, e.Description, e.Amount, e.Month })
                .ToListAsync();
            var expenses = expRaw
                .Select(e => (
                    description: e.Description,
                    amount: e.Amount.ToString("C0", inr),
                    month: e.Month,
                    url: "/Expenses/Details/" + e.Id))
                .ToList();

            // Debts — match on the counterparty name or note. Show the outstanding
            // balance (Amount - AmountPaid) and which way the money flows.
            var debtRaw = await _context.Debts
                .Where(d => d.UserId == userId
                    && (EF.Functions.Like(d.PersonName, $"%{q}%") || EF.Functions.Like(d.Note, $"%{q}%")))
                .OrderByDescending(d => d.Amount - d.AmountPaid > 0m)
                .ThenByDescending(d => d.Date)
                .Take(15)
                .Select(d => new { d.Id, d.PersonName, d.Direction, d.Amount, d.AmountPaid })
                .ToListAsync();
            var debts = debtRaw
                .Select(d => (
                    person: d.PersonName,
                    direction: d.Direction == DebtDirection.TheyOweMe ? "Owed to me" : "I owe",
                    outstanding: Math.Max(0m, d.Amount - d.AmountPaid).ToString("C0", inr),
                    theyOweMe: d.Direction == DebtDirection.TheyOweMe,
                    url: "/Debts/Details/" + d.Id))
                .ToList();

            // Event budgets — match the event itself, then any sub-event inside one.
            // Aggregates are projected raw and formatted after materialisation, per the
            // EF/CultureInfo rule above.
            var eventRaw = await _context.Events
                .Where(e => e.UserId == userId && EF.Functions.Like(e.Name, $"%{q}%"))
                .OrderByDescending(e => e.EventDate ?? e.CreatedAt)
                .Take(6)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.EventType,
                    Allocated = e.SubEvents.Sum(s => (decimal?)s.Allocated) ?? 0m
                })
                .ToListAsync();

            var subEventRaw = await _context.SubEvents
                .Where(s => s.Event!.UserId == userId && EF.Functions.Like(s.Name, $"%{q}%"))
                .OrderBy(s => s.Name)
                .Take(8)
                .Select(s => new { s.Name, s.Allocated, s.EventId, EventName = s.Event!.Name })
                .ToListAsync();

            var events = eventRaw
                .Select(e => (
                    name: e.Name,
                    context: ExpenseTracker.Data.EventTemplates.Label(e.EventType),
                    amount: e.Allocated > 0m ? e.Allocated.ToString("C0", inr) + " allocated" : "no budget set",
                    url: "/Events/Details/" + e.Id))
                .Concat(subEventRaw.Select(s => (
                    name: s.Name,
                    context: "in " + s.EventName,
                    amount: s.Allocated > 0m ? s.Allocated.ToString("C0", inr) + " allocated" : "no budget set",
                    url: "/Events/Details/" + s.EventId)))
                .ToList();

            return new SearchResults { Nav = nav, Categories = cats, Expenses = expenses, Debts = debts, Events = events };
        }

        private class SearchResults
        {
            public List<(string label, string url, string icon)> Nav { get; set; } = new();
            public List<(string name, string type, string url)> Categories { get; set; } = new();
            public List<(string? description, string amount, string month, string url)> Expenses { get; set; } = new();
            public List<(string person, string direction, string outstanding, bool theyOweMe, string url)> Debts { get; set; } = new();
            public List<(string name, string context, string amount, string url)> Events { get; set; } = new();
        }
    }
}
