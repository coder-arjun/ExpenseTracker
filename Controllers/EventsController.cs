using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Models.ViewModel;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Event budgets — one-off project budgets such as a wedding, a birthday party or a
    /// house warming. This controller owns the event itself (list, workspace, CRUD,
    /// export); the rows inside an event live in <see cref="EventLedgerController"/>.
    ///
    /// Event money is an ISOLATED ledger: no code here reads or writes Expenses,
    /// Incomes, Accounts or Transfers, so allocations and event spend never reach the
    /// monthly totals, the dashboard or net worth.
    /// </summary>
    public class EventsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventBudgetService _events;

        public EventsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            EventBudgetService events)
        {
            _context = context;
            _userManager = userManager;
            _events = events;
        }

        // GET: /Events?filter=all|upcoming|planning|active|completed|archived
        public async Task<IActionResult> Index(int page = 1, string filter = "all")
        {
            var userId = _userManager.GetUserId(User)!;
            var rows = await _events.GetIndexAsync(userId, page, filter);

            ViewData["Filter"] = filter;
            ViewData["Counts"] = await _events.GetFilterCountsAsync(userId);
            ViewData["AnyEvents"] = await _context.Events.AnyAsync(e => e.UserId == userId);

            return View(rows);
        }

        // GET: /Events/Details/5 — the event workspace
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            var vm = await _events.GetDetailsAsync(userId, id.Value);
            if (vm == null) return NotFound();

            return View(vm);
        }

        /// <summary>
        /// GET /Events/Board/5 — just the live region of the workspace (totals, sub-event
        /// ledger, contributions), re-rendered so the client can swap it in after a
        /// mutation. Razor stays the single source of truth: there is no duplicate
        /// rendering logic in JavaScript.
        /// </summary>
        public async Task<IActionResult> Board(int id, string? msg = null)
        {
            var userId = _userManager.GetUserId(User)!;
            var vm = await _events.GetDetailsAsync(userId, id);
            if (vm == null) return NotFound();

            ViewData["Flash"] = msg;
            return PartialView("_Board", vm);
        }

        // GET: /Events/Create
        public IActionResult Create()
        {
            return View(new Event { EventDate = DateTime.Today });
        }

        // POST: /Events/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,EventType,EventDate,Note")] Event ev)
        {
            var userId = _userManager.GetUserId(User)!;
            ev.UserId = userId;
            ev.CreatedAt = DateTime.UtcNow;
            ev.Status = EventStatus.Planning;
            ClearServerFieldErrors();

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return View(ev);
            }

            ev.Name = ev.Name.Trim();
            _context.Events.Add(ev);
            await _context.SaveChangesAsync();

            // Seed the template's sub-events at zero allocation for the user to fill in.
            var seeded = EventTemplates.BuildFor(ev.EventType, ev.Id, userId);
            if (seeded.Count > 0)
            {
                _context.SubEvents.AddRange(seeded);
                await _context.SaveChangesAsync();
            }

            // Drives the one-time "created" sequence on the details page. Deliberately
            // not a toast — the arrival on the workspace is the moment worth marking.
            TempData["EventJustCreated"] = ev.Name;
            TempData["EventSeededCount"] = seeded.Count;

            return RedirectToAction(nameof(Details), new { id = ev.Id });
        }

        // GET: /Events/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (ev == null) return NotFound();

            // The form is deliberately narrow; the space beside it carries a live summary.
            var vm = await _events.GetDetailsAsync(userId, ev.Id);
            ViewData["Totals"] = vm?.Totals;

            return View(ev);
        }

        // POST: /Events/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,EventType,EventDate,Status,Note")] Event input)
        {
            if (id != input.Id) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            var existing = await _context.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (existing == null) return NotFound();

            ClearServerFieldErrors();
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return View(input);
            }

            existing.Name = input.Name.Trim();
            existing.EventType = input.EventType;
            existing.EventDate = input.EventDate;
            existing.Status = input.Status;
            existing.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Event updated.";
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        /// <summary>
        /// POST /Events/Complete/5 — close an event once it has happened, or reopen it.
        /// Same effect as changing Status in Edit, but reachable in one click from the
        /// workspace, which is where you actually are when the event is over.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id, bool reopen = false)
        {
            var userId = _userManager.GetUserId(User)!;
            var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (ev == null) return NotFound();

            ev.Status = reopen ? EventStatus.Active : EventStatus.Completed;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = reopen
                ? $"'{ev.Name}' reopened."
                : $"'{ev.Name}' marked complete and moved to Archived.";

            return RedirectToAction(nameof(Details), new { id = ev.Id });
        }

        // GET: /Events/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (ev == null) return NotFound();

            // Spell out exactly what cascades, so the confirmation is honest.
            ViewData["SubEventCount"] = await _context.SubEvents.CountAsync(s => s.EventId == ev.Id);
            ViewData["SpendCount"] = await _context.EventSpends.CountAsync(s => s.SubEvent!.EventId == ev.Id);
            ViewData["ContributionCount"] = await _context.EventContributions.CountAsync(c => c.EventId == ev.Id);

            return View(ev);
        }

        // POST: /Events/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (ev != null)
            {
                // Sub-events, their spends and the contributions all cascade in the DB.
                _context.Events.Remove(ev);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"'{ev.Name}' and everything inside it was deleted.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: /Events/Export?format=csv|xlsx|pdf
        public async Task<IActionResult> Export(string? format = "csv")
        {
            var userId = _userManager.GetUserId(User)!;
            var rows = await _events.GetExportRowsAsync(userId);

            // Count from the source, not by matching the "TOTAL" label — a user is free
            // to name a sub-event "TOTAL".
            var eventCount = await _context.Events.CountAsync(e => e.UserId == userId);
            var subtitle = $"All events · {eventCount} event{(eventCount == 1 ? "" : "s")}";
            var dateStamp = DateTime.Today.ToString("yyyy-MM-dd");

            switch ((format ?? "csv").ToLowerInvariant())
            {
                case "xlsx":
                    var xlsx = ExcelExporter.Build<EventExportRow>("Events", subtitle, rows, new[]
                    {
                        new ExcelExporter.Column<EventExportRow>("Event",     r => r.EventName),
                        new ExcelExporter.Column<EventExportRow>("Type",      r => r.EventType),
                        new ExcelExporter.Column<EventExportRow>("Status",    r => r.Status),
                        new ExcelExporter.Column<EventExportRow>("Date",      r => r.EventDate),
                        new ExcelExporter.Column<EventExportRow>("Sub-event", r => r.SubEvent),
                        new ExcelExporter.Column<EventExportRow>("Allocated", r => r.Allocated, IsCurrency: true),
                        new ExcelExporter.Column<EventExportRow>("Paid",      r => r.Paid,      IsCurrency: true),
                        new ExcelExporter.Column<EventExportRow>("Committed", r => r.Committed, IsCurrency: true),
                        new ExcelExporter.Column<EventExportRow>("Available", r => r.Available, IsCurrency: true),
                    }, sheetName: "Events");
                    return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"events-{dateStamp}.xlsx");

                case "pdf":
                    var pdf = PdfExporter.Build<EventExportRow>("Events", subtitle, rows, new[]
                    {
                        new PdfExporter.Column<EventExportRow>("Event",     r => r.EventName,                    RelativeWidth: 1.6f),
                        new PdfExporter.Column<EventExportRow>("Sub-event", r => r.SubEvent,                     RelativeWidth: 1.8f),
                        new PdfExporter.Column<EventExportRow>("Allocated", r => r.Allocated, IsCurrency: true,  RelativeWidth: 1.1f),
                        new PdfExporter.Column<EventExportRow>("Paid",      r => r.Paid,      IsCurrency: true,  RelativeWidth: 1.1f),
                        new PdfExporter.Column<EventExportRow>("Committed", r => r.Committed, IsCurrency: true,  RelativeWidth: 1.1f),
                        new PdfExporter.Column<EventExportRow>("Available", r => r.Available, IsCurrency: true,  RelativeWidth: 1.1f),
                    });
                    return File(pdf, "application/pdf", $"events-{dateStamp}.pdf");

                default: // csv
                    var csv = CsvExporter.Build<EventExportRow>(rows, new (string, Func<EventExportRow, object?>)[]
                    {
                        ("Event",     r => r.EventName),
                        ("Type",      r => r.EventType),
                        ("Status",    r => r.Status),
                        ("Date",      r => r.EventDate),
                        ("Sub-event", r => r.SubEvent),
                        ("Allocated", r => r.Allocated),
                        ("Paid",      r => r.Paid),
                        ("Committed", r => r.Committed),
                        ("Available", r => r.Available),
                    });
                    return File(csv, "text/csv", $"events-{dateStamp}.csv");
            }
        }

        /// <summary>
        /// Drop model errors for fields the server owns (UserId/User/navigations) before
        /// checking ModelState — same pattern as the other CRUD controllers.
        /// </summary>
        private void ClearServerFieldErrors()
        {
            var keysToRemove = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase)
                         || k.StartsWith("SubEvents", StringComparison.OrdinalIgnoreCase)
                         || k.StartsWith("Contributions", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keysToRemove)
                ModelState.Remove(key);
        }
    }
}
