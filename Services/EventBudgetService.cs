using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Models.ViewModel;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Read-side arithmetic for event budgets: allocated vs actual vs received, rolled
    /// up from spend rows to sub-events to the event itself. Nothing is stored — every
    /// figure is derived per request, so allocations and spends can never disagree.
    ///
    /// Deliberately touches ONLY the four event tables. It must never read or write
    /// Expenses, Incomes, Accounts or Transfers: event money is isolated from the
    /// monthly ledgers, the dashboard and net worth by construction.
    /// </summary>
    public class EventBudgetService
    {
        private readonly ApplicationDbContext _db;

        public EventBudgetService(ApplicationDbContext db) => _db = db;

        // ---- Index -----------------------------------------------------
        /// <summary>
        /// One page of event cards with their roll-ups. Allocated/Received/SubEventCount
        /// come from navigation aggregates; Actual is filled by a second grouped query
        /// over the page of events, which is cheaper and simpler to translate than a
        /// nested SelectMany aggregate.
        /// </summary>
        public async Task<PaginatedList<EventIndexRow>> GetIndexAsync(
            string userId, int page, EventStatus? status, bool showArchived)
        {
            var query = _db.Events.Where(e => e.UserId == userId);

            if (status.HasValue)
                query = query.Where(e => e.Status == status.Value);
            else if (!showArchived)
                query = query.Where(e => e.Status != EventStatus.Completed && e.Status != EventStatus.Cancelled);

            var projected = query
                .OrderByDescending(e => e.EventDate ?? e.CreatedAt)
                .ThenByDescending(e => e.Id)
                .Select(e => new EventIndexRow
                {
                    Id = e.Id,
                    Name = e.Name,
                    EventType = e.EventType,
                    EventDate = e.EventDate,
                    Status = e.Status,
                    SubEventCount = e.SubEvents.Count,
                    Allocated = e.SubEvents.Sum(s => (decimal?)s.Allocated) ?? 0m,
                    Received = e.Contributions.Sum(c => (decimal?)c.Amount) ?? 0m
                });

            var rows = await PaginatedList<EventIndexRow>.CreateAsync(projected, page, 9);
            await FillActualsAsync(rows);
            return rows;
        }

        /// <summary>Back-fill Actual for a materialised page of rows.</summary>
        private async Task FillActualsAsync(IList<EventIndexRow> rows)
        {
            if (rows.Count == 0) return;

            var ids = rows.Select(r => r.Id).ToList();
            var actuals = await _db.EventSpends
                .Where(s => ids.Contains(s.SubEvent!.EventId))
                .GroupBy(s => s.SubEvent!.EventId)
                .Select(g => new { EventId = g.Key, Total = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.EventId, x => x.Total);

            foreach (var row in rows)
                row.Actual = actuals.TryGetValue(row.Id, out var total) ? total : 0m;
        }

        // ---- Details ---------------------------------------------------
        /// <summary>
        /// The full workspace model for one event, or null if it does not exist or is
        /// not owned by the caller. Ownership is checked on the event itself, and
        /// everything else is fetched through that event's id.
        /// </summary>
        public async Task<EventDetailsViewModel?> GetDetailsAsync(string userId, int eventId)
        {
            var ev = await _db.Events
                .FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);
            if (ev == null) return null;

            var subs = await _db.SubEvents
                .Where(s => s.EventId == eventId)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Select(s => new { s.Id, s.Name, s.Allocated, s.Note, s.SortOrder })
                .ToListAsync();

            var spends = await _db.EventSpends
                .Where(s => s.SubEvent!.EventId == eventId)
                .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)
                .Select(s => new { s.Id, s.SubEventId, s.Amount, s.Date, s.PaidTo, s.Note })
                .ToListAsync();

            var contributions = await _db.EventContributions
                .Where(c => c.EventId == eventId)
                .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
                .Select(c => new EventContributionLine(c.Id, c.Amount, c.Date, c.FromWhom, c.Note))
                .ToListAsync();

            var spendsBySub = spends
                .GroupBy(s => s.SubEventId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var lines = subs.Select(s =>
            {
                spendsBySub.TryGetValue(s.Id, out var rows);
                rows ??= new();
                return new SubEventLine
                {
                    Id = s.Id,
                    Name = s.Name,
                    Note = s.Note,
                    SortOrder = s.SortOrder,
                    Allocated = s.Allocated,
                    Actual = rows.Sum(r => r.Amount),
                    Spends = rows
                        .Select(r => new EventSpendLine(r.Id, r.Amount, r.Date, r.PaidTo, r.Note))
                        .ToList()
                };
            }).ToList();

            return new EventDetailsViewModel
            {
                Event = ev,
                SubEvents = lines,
                Contributions = contributions,
                Totals = new EventTotals
                {
                    Allocated = lines.Sum(l => l.Allocated),
                    Actual = lines.Sum(l => l.Actual),
                    Received = contributions.Sum(c => c.Amount),
                    SubEventCount = lines.Count,
                    SpendCount = spends.Count,
                    ContributionCount = contributions.Count
                }
            };
        }

        // ---- Ownership guards ------------------------------------------
        /// <summary>The owning event id for a sub-event, or null when the user does not own it.</summary>
        public Task<int?> ResolveEventIdAsync(string userId, int subEventId) =>
            _db.SubEvents
                .Where(s => s.Id == subEventId && s.Event!.UserId == userId)
                .Select(s => (int?)s.EventId)
                .FirstOrDefaultAsync();

        /// <summary>A sub-event the user owns, or null. Never trust a raw id from the client.</summary>
        public Task<SubEvent?> FindSubEventAsync(string userId, int subEventId) =>
            _db.SubEvents.FirstOrDefaultAsync(s => s.Id == subEventId && s.Event!.UserId == userId);

        /// <summary>A spend row the user owns, with its sub-event loaded, or null.</summary>
        public Task<EventSpend?> FindSpendAsync(string userId, int spendId) =>
            _db.EventSpends
                .Include(s => s.SubEvent)
                .FirstOrDefaultAsync(s => s.Id == spendId && s.SubEvent!.Event!.UserId == userId);

        /// <summary>A contribution the user owns, or null.</summary>
        public Task<EventContribution?> FindContributionAsync(string userId, int contributionId) =>
            _db.EventContributions
                .FirstOrDefaultAsync(c => c.Id == contributionId && c.Event!.UserId == userId);

        public Task<bool> OwnsEventAsync(string userId, int eventId) =>
            _db.Events.AnyAsync(e => e.Id == eventId && e.UserId == userId);

        // ---- Export ----------------------------------------------------
        /// <summary>
        /// Every event flattened to one row per sub-event, each event closed with a
        /// total row, ordered so the export reads like the on-screen ledger.
        /// </summary>
        public async Task<List<EventExportRow>> GetExportRowsAsync(string userId)
        {
            var events = await _db.Events
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.EventDate ?? e.CreatedAt)
                .ThenByDescending(e => e.Id)
                .Select(e => new { e.Id, e.Name, e.EventType, e.Status, e.EventDate })
                .ToListAsync();

            if (events.Count == 0) return new List<EventExportRow>();

            var ids = events.Select(e => e.Id).ToList();

            var subs = await _db.SubEvents
                .Where(s => ids.Contains(s.EventId))
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Select(s => new { s.Id, s.EventId, s.Name, s.Allocated })
                .ToListAsync();

            var actuals = await _db.EventSpends
                .Where(s => ids.Contains(s.SubEvent!.EventId))
                .GroupBy(s => s.SubEventId)
                .Select(g => new { SubEventId = g.Key, Total = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.SubEventId, x => x.Total);

            var rows = new List<EventExportRow>();
            foreach (var e in events)
            {
                var mine = subs.Where(s => s.EventId == e.Id).ToList();
                var typeLabel = EventTemplates.Label(e.EventType);
                var statusLabel = e.Status.ToString();

                foreach (var s in mine)
                {
                    var actual = actuals.TryGetValue(s.Id, out var t) ? t : 0m;
                    rows.Add(new EventExportRow(
                        e.Name, typeLabel, statusLabel, e.EventDate,
                        s.Name, s.Allocated, actual, s.Allocated - actual));
                }

                var allocated = mine.Sum(s => s.Allocated);
                var spent = mine.Sum(s => actuals.TryGetValue(s.Id, out var t) ? t : 0m);
                rows.Add(new EventExportRow(
                    e.Name, typeLabel, statusLabel, e.EventDate,
                    "TOTAL", allocated, spent, allocated - spent));
            }

            return rows;
        }
    }
}
