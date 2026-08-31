using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// The rows INSIDE an event: sub-events, their dated spend entries, and the money
    /// received towards the event. <see cref="EventsController"/> owns the event itself.
    ///
    /// Every action is a real form POST, so the feature works with JavaScript disabled
    /// (normal submit then redirect to the workspace). When the browser sends the
    /// <c>X-Partial</c> header the same action instead redirects to
    /// <c>/Events/Board/{id}</c>, which returns the re-rendered board fragment for the
    /// client to swap in — Razor stays the single source of truth for rendering.
    ///
    /// An id arriving from the client is NEVER trusted: every action re-resolves it
    /// through <see cref="EventBudgetService"/>, which joins up to Event.UserId.
    /// </summary>
    [Route("[controller]/[action]")]
    public class EventLedgerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EventBudgetService _events;

        public EventLedgerController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            EventBudgetService events)
        {
            _context = context;
            _userManager = userManager;
            _events = events;
        }

        // ================= sub-events =================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSubEvent(int eventId, string? name, decimal allocated, string? note)
        {
            var userId = _userManager.GetUserId(User)!;
            if (!await _events.OwnsEventAsync(userId, eventId)) return NotFound();

            name = (name ?? "").Trim();
            if (name.Length == 0) return Fail(eventId, "Sub-event name is required.");
            if (name.Length > 100) return Fail(eventId, "Sub-event name can be at most 100 characters.");
            if (allocated < 0) return Fail(eventId, "Allocated amount can't be negative.");

            // Pre-check the unique (EventId, Name) index so the user gets a sentence,
            // not a database exception.
            var duplicate = await _context.SubEvents
                .AnyAsync(s => s.EventId == eventId && s.Name == name);
            if (duplicate) return Fail(eventId, $"'{name}' already exists in this event.");

            var nextOrder = await _context.SubEvents
                .Where(s => s.EventId == eventId)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync() ?? -1;

            _context.SubEvents.Add(new SubEvent
            {
                EventId = eventId,
                Name = name,
                Allocated = allocated,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                SortOrder = nextOrder + 1,
                UserId = userId
            });
            await _context.SaveChangesAsync();

            return Done(eventId, $"'{name}' added.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSubEvent(int id, string? name, decimal allocated, string? note)
        {
            var userId = _userManager.GetUserId(User)!;
            var sub = await _events.FindSubEventAsync(userId, id);
            if (sub == null) return NotFound();

            name = (name ?? "").Trim();
            if (name.Length == 0) return Fail(sub.EventId, "Sub-event name is required.");
            if (name.Length > 100) return Fail(sub.EventId, "Sub-event name can be at most 100 characters.");
            if (allocated < 0) return Fail(sub.EventId, "Allocated amount can't be negative.");

            var duplicate = await _context.SubEvents
                .AnyAsync(s => s.EventId == sub.EventId && s.Name == name && s.Id != sub.Id);
            if (duplicate) return Fail(sub.EventId, $"'{name}' already exists in this event.");

            sub.Name = name;
            sub.Allocated = allocated;
            sub.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await _context.SaveChangesAsync();

            return Done(sub.EventId, $"'{sub.Name}' updated.");
        }

        /// <summary>Inline allocation edit from the ledger table — amount only.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAllocation(int id, decimal allocated)
        {
            var userId = _userManager.GetUserId(User)!;
            var sub = await _events.FindSubEventAsync(userId, id);
            if (sub == null) return NotFound();

            if (allocated < 0) return Fail(sub.EventId, "Allocated amount can't be negative.");

            sub.Allocated = allocated;
            await _context.SaveChangesAsync();

            return Done(sub.EventId, $"Allocation for '{sub.Name}' updated.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSubEvent(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var sub = await _events.FindSubEventAsync(userId, id);
            if (sub == null) return NotFound();

            var eventId = sub.EventId;
            var name = sub.Name;
            var spendCount = await _context.EventSpends.CountAsync(s => s.SubEventId == sub.Id);

            // Spend rows cascade with the sub-event.
            _context.SubEvents.Remove(sub);
            await _context.SaveChangesAsync();

            return Done(eventId, spendCount > 0
                ? $"'{name}' and its {spendCount} spend entr{(spendCount == 1 ? "y" : "ies")} were deleted."
                : $"'{name}' deleted.");
        }

        // ================= spend entries ==============================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSpend(
            int subEventId, decimal amount, DateTime date, string? paidTo, string? note, SpendStatus status = SpendStatus.Paid)
        {
            var userId = _userManager.GetUserId(User)!;
            var sub = await _events.FindSubEventAsync(userId, subEventId);
            if (sub == null) return NotFound();

            var error = ValidateEntry(amount, date);
            if (error != null) return Fail(sub.EventId, error);

            _context.EventSpends.Add(new EventSpend
            {
                SubEventId = sub.Id,
                Amount = amount,
                Date = date.Date,
                PaidTo = Clean(paidTo, 120),
                Note = Clean(note, 200),
                Status = status,
                UserId = userId
            });
            await _context.SaveChangesAsync();

            var verb = status == SpendStatus.Committed ? "Commitment" : "Payment";
            return Done(sub.EventId, $"{verb} recorded against '{sub.Name}'.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSpend(
            int id, decimal amount, DateTime date, string? paidTo, string? note, SpendStatus status = SpendStatus.Paid)
        {
            var userId = _userManager.GetUserId(User)!;
            var spend = await _events.FindSpendAsync(userId, id);
            if (spend == null || spend.SubEvent == null) return NotFound();

            var eventId = spend.SubEvent.EventId;
            var error = ValidateEntry(amount, date);
            if (error != null) return Fail(eventId, error);

            spend.Amount = amount;
            spend.Date = date.Date;
            spend.PaidTo = Clean(paidTo, 120);
            spend.Note = Clean(note, 200);
            spend.Status = status;
            await _context.SaveChangesAsync();

            return Done(eventId, "Entry updated.");
        }

        /// <summary>One-tap "this commitment has now been paid".</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkPaid(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var spend = await _events.FindSpendAsync(userId, id);
            if (spend == null || spend.SubEvent == null) return NotFound();

            spend.Status = SpendStatus.Paid;
            await _context.SaveChangesAsync();

            return Done(spend.SubEvent.EventId, "Marked as paid.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSpend(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var spend = await _events.FindSpendAsync(userId, id);
            if (spend == null || spend.SubEvent == null) return NotFound();

            var eventId = spend.SubEvent.EventId;
            _context.EventSpends.Remove(spend);
            await _context.SaveChangesAsync();

            return Done(eventId, "Spend entry deleted.");
        }

        // ================= contributions ==============================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddContribution(int eventId, decimal amount, DateTime date, string? fromWhom, string? note)
        {
            var userId = _userManager.GetUserId(User)!;
            if (!await _events.OwnsEventAsync(userId, eventId)) return NotFound();

            var error = ValidateEntry(amount, date);
            if (error != null) return Fail(eventId, error);

            _context.EventContributions.Add(new EventContribution
            {
                EventId = eventId,
                Amount = amount,
                Date = date.Date,
                FromWhom = Clean(fromWhom, 120),
                Note = Clean(note, 200),
                UserId = userId
            });
            await _context.SaveChangesAsync();

            return Done(eventId, "Contribution recorded.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteContribution(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var contribution = await _events.FindContributionAsync(userId, id);
            if (contribution == null) return NotFound();

            var eventId = contribution.EventId;
            _context.EventContributions.Remove(contribution);
            await _context.SaveChangesAsync();

            return Done(eventId, "Contribution deleted.");
        }

        // ================= plumbing ===================================

        /// <summary>True when the browser asked for the board fragment rather than a page.</summary>
        private bool WantsPartial => Request.Headers.ContainsKey("X-Partial");

        /// <summary>
        /// Success. For a fetch() caller, redirect to the board fragment so the response
        /// body is the re-rendered HTML; for a plain form post, flash and go back to the page.
        /// </summary>
        private IActionResult Done(int eventId, string message)
        {
            if (WantsPartial)
                return RedirectToAction("Board", "Events", new { id = eventId, msg = message });

            TempData["SuccessMessage"] = message;
            return RedirectToAction("Details", "Events", new { id = eventId });
        }

        /// <summary>
        /// Validation failure. A fetch() caller gets 400 + JSON so it can keep the dialog
        /// open and show the message; a plain form post gets a flash and the page back.
        /// </summary>
        private IActionResult Fail(int eventId, string message)
        {
            if (WantsPartial)
                return BadRequest(new { error = message });

            TempData["ErrorMessage"] = message;
            return RedirectToAction("Details", "Events", new { id = eventId });
        }

        /// <summary>Shared amount/date rules for spends and contributions.</summary>
        private static string? ValidateEntry(decimal amount, DateTime date)
        {
            if (amount <= 0m) return "Amount must be greater than zero.";
            if (amount > 999999999.99m) return "Amount is too large.";
            if (date.Date > DateTime.Today) return "The date can't be in the future.";
            if (date.Year < 1900) return "Please enter a valid date.";
            return null;
        }

        private static string? Clean(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }
}
