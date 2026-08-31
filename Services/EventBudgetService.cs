using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Models.ViewModel;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Read-side arithmetic for event budgets: allocated vs committed vs paid vs
    /// received, rolled up from entries to sub-events to the event. Nothing is stored —
    /// every figure is derived per request, so the numbers can never disagree.
    ///
    /// Deliberately touches ONLY the four event tables. It must never read or write
    /// Expenses, Incomes, Accounts or Transfers: event money is isolated from the
    /// monthly ledgers, the dashboard and net worth by construction.
    /// </summary>
    public class EventBudgetService
    {
        private readonly ApplicationDbContext _db;

        public EventBudgetService(ApplicationDbContext db) => _db = db;

        /// <summary>The filter rail on the index. Anything unrecognised falls back to "all".</summary>
        private IQueryable<Event> ApplyFilter(IQueryable<Event> q, string? filter)
        {
            var today = DateTime.Today;
            return (filter ?? "all").ToLowerInvariant() switch
            {
                "upcoming" => q.Where(e => e.EventDate != null && e.EventDate >= today
                                        && e.Status != EventStatus.Completed && e.Status != EventStatus.Cancelled),
                "planning" => q.Where(e => e.Status == EventStatus.Planning),
                "active" => q.Where(e => e.Status == EventStatus.Active),
                "completed" => q.Where(e => e.Status == EventStatus.Completed),
                "archived" => q.Where(e => e.Status == EventStatus.Completed || e.Status == EventStatus.Cancelled),
                _ => q.Where(e => e.Status != EventStatus.Completed && e.Status != EventStatus.Cancelled),
            };
        }

        // ---- Index -----------------------------------------------------
        public async Task<PaginatedList<EventIndexRow>> GetIndexAsync(string userId, int page, string? filter)
        {
            var query = ApplyFilter(_db.Events.Where(e => e.UserId == userId), filter);

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
            await FillEntriesAsync(rows);
            return rows;
        }

        /// <summary>Back-fill Paid/Committed for a materialised page of rows.</summary>
        private async Task FillEntriesAsync(IList<EventIndexRow> rows)
        {
            if (rows.Count == 0) return;

            var ids = rows.Select(r => r.Id).ToList();
            var sums = await _db.EventSpends
                .Where(s => ids.Contains(s.SubEvent!.EventId))
                .GroupBy(s => new { s.SubEvent!.EventId, s.Status })
                .Select(g => new { g.Key.EventId, g.Key.Status, Total = g.Sum(x => x.Amount) })
                .ToListAsync();

            foreach (var row in rows)
            {
                row.Paid = sums.Where(s => s.EventId == row.Id && s.Status == SpendStatus.Paid)
                               .Sum(s => s.Total);
                row.Committed = sums.Where(s => s.EventId == row.Id && s.Status == SpendStatus.Committed)
                                    .Sum(s => s.Total);
            }
        }

        /// <summary>Counts for the filter rail, so each tab can show how much sits behind it.</summary>
        public async Task<EventFilterCounts> GetFilterCountsAsync(string userId)
        {
            var mine = _db.Events.Where(e => e.UserId == userId);
            var today = DateTime.Today;

            var byStatus = await mine
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, N = g.Count() })
                .ToListAsync();

            int N(EventStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.N ?? 0;

            return new EventFilterCounts
            {
                All = N(EventStatus.Planning) + N(EventStatus.Active),
                Upcoming = await mine.CountAsync(e => e.EventDate != null && e.EventDate >= today
                                                   && e.Status != EventStatus.Completed && e.Status != EventStatus.Cancelled),
                Planning = N(EventStatus.Planning),
                Active = N(EventStatus.Active),
                Completed = N(EventStatus.Completed),
                Archived = N(EventStatus.Completed) + N(EventStatus.Cancelled)
            };
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
                .Select(s => new { s.Id, s.SubEventId, s.Amount, s.Date, s.PaidTo, s.Note, s.Status })
                .ToListAsync();

            var contributions = await _db.EventContributions
                .Where(c => c.EventId == eventId)
                .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
                .Select(c => new EventContributionLine(c.Id, c.Amount, c.Date, c.FromWhom, c.Note))
                .ToListAsync();

            var bySub = spends.GroupBy(s => s.SubEventId).ToDictionary(g => g.Key, g => g.ToList());

            var lines = subs.Select(s =>
            {
                bySub.TryGetValue(s.Id, out var rows);
                rows ??= new();
                return new SubEventLine
                {
                    Id = s.Id,
                    Name = s.Name,
                    Note = s.Note,
                    SortOrder = s.SortOrder,
                    Allocated = s.Allocated,
                    Paid = rows.Where(r => r.Status == SpendStatus.Paid).Sum(r => r.Amount),
                    Committed = rows.Where(r => r.Status == SpendStatus.Committed).Sum(r => r.Amount),
                    Spends = rows
                        .Select(r => new EventSpendLine(r.Id, r.Amount, r.Date, r.PaidTo, r.Note, r.Status))
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
                    Paid = lines.Sum(l => l.Paid),
                    Committed = lines.Sum(l => l.Committed),
                    Received = contributions.Sum(c => c.Amount),
                    SubEventCount = lines.Count,
                    SpendCount = spends.Count,
                    CommittedCount = spends.Count(s => s.Status == SpendStatus.Committed),
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

        /// <summary>An entry the user owns, with its sub-event loaded, or null.</summary>
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

            var sums = await _db.EventSpends
                .Where(s => ids.Contains(s.SubEvent!.EventId))
                .GroupBy(s => new { s.SubEventId, s.Status })
                .Select(g => new { g.Key.SubEventId, g.Key.Status, Total = g.Sum(x => x.Amount) })
                .ToListAsync();

            decimal Sum(int subId, SpendStatus st) =>
                sums.Where(x => x.SubEventId == subId && x.Status == st).Sum(x => x.Total);

            var rows = new List<EventExportRow>();
            foreach (var e in events)
            {
                var mine = subs.Where(s => s.EventId == e.Id).ToList();
                var typeLabel = EventTemplates.Label(e.EventType);
                var statusLabel = e.Status.ToString();

                foreach (var s in mine)
                {
                    var paid = Sum(s.Id, SpendStatus.Paid);
                    var committed = Sum(s.Id, SpendStatus.Committed);
                    rows.Add(new EventExportRow(
                        e.Name, typeLabel, statusLabel, e.EventDate,
                        s.Name, s.Allocated, paid, committed, s.Allocated - paid - committed));
                }

                var a = mine.Sum(s => s.Allocated);
                var p = mine.Sum(s => Sum(s.Id, SpendStatus.Paid));
                var c = mine.Sum(s => Sum(s.Id, SpendStatus.Committed));
                rows.Add(new EventExportRow(
                    e.Name, typeLabel, statusLabel, e.EventDate,
                    "TOTAL", a, p, c, a - p - c));
            }

            return rows;
        }
    }
}
