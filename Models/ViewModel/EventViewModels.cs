using ExpenseTracker.Models.Domain;

namespace ExpenseTracker.Models.ViewModel
{
    /// <summary>
    /// Shared allocated/actual/received arithmetic for event budgets. Everything here
    /// is derived at read time — nothing is denormalised, so there is no cache to go stale.
    ///
    /// Variance convention (identical at sub-event and event level):
    ///   positive => under budget (good, rendered green with a leading +)
    ///   negative => over budget  (bad,  rendered red with a leading −)
    /// </summary>
    public abstract class RollupBase
    {
        public decimal Allocated { get; set; }
        public decimal Actual { get; set; }

        public decimal Variance => Allocated - Actual;
        public bool IsOverBudget => Variance < 0;

        /// <summary>False when nothing has been budgeted yet — callers render "unbudgeted" instead of a bar.</summary>
        public bool HasAllocation => Allocated > 0m;

        /// <summary>Spend as a percentage of allocation, capped at 100 for bar width. Zero when unbudgeted.</summary>
        public decimal ProgressPercent =>
            HasAllocation ? Math.Min(100m, Math.Round(Actual / Allocated * 100m, 1)) : 0m;

        /// <summary>Uncapped percentage — used for the "112% of budget" caption.</summary>
        public decimal RawPercent =>
            HasAllocation ? Math.Round(Actual / Allocated * 100m, 1) : 0m;

        /// <summary>Bootstrap contextual class for the progress bar: green / brass / red.</summary>
        public string ProgressClass =>
            !HasAllocation ? "bg-secondary"
            : RawPercent > 100m ? "bg-danger"
            : RawPercent >= 80m ? "bg-warning"
            : "bg-success";
    }

    /// <summary>Event-level roll-up, including money received.</summary>
    public class EventTotals : RollupBase
    {
        public decimal Received { get; set; }

        /// <summary>What the event actually cost the user once contributions are netted off.</summary>
        public decimal NetOutlay => Actual - Received;

        public int SubEventCount { get; set; }
        public int SpendCount { get; set; }
        public int ContributionCount { get; set; }
    }

    /// <summary>One sub-event row on the details page, with its dated spend rows.</summary>
    public class SubEventLine : RollupBase
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int SortOrder { get; set; }
        public List<EventSpendLine> Spends { get; set; } = new();
    }

    public record EventSpendLine(int Id, decimal Amount, DateTime Date, string? PaidTo, string? Note);

    public record EventContributionLine(int Id, decimal Amount, DateTime Date, string? FromWhom, string? Note);

    /// <summary>Backing model for /Events/Details/{id} — the event workspace.</summary>
    public class EventDetailsViewModel
    {
        public Event Event { get; set; } = default!;
        public EventTotals Totals { get; set; } = new();
        public List<SubEventLine> SubEvents { get; set; } = new();
        public List<EventContributionLine> Contributions { get; set; } = new();
    }

    /// <summary>One card on /Events. Projected straight out of EF with its aggregates.</summary>
    public class EventIndexRow : RollupBase
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public EventType EventType { get; set; }
        public DateTime? EventDate { get; set; }
        public EventStatus Status { get; set; }
        public decimal Received { get; set; }
        public int SubEventCount { get; set; }

        public decimal NetOutlay => Actual - Received;
        public bool IsArchived => Status is EventStatus.Completed or EventStatus.Cancelled;
    }

    /// <summary>Flat row used by the Events export (xlsx / pdf / csv).</summary>
    public record EventExportRow(
        string EventName,
        string EventType,
        string Status,
        DateTime? EventDate,
        string SubEvent,
        decimal Allocated,
        decimal Actual,
        decimal Variance);
}
