using ExpenseTracker.Models.Domain;

namespace ExpenseTracker.Models.ViewModel
{
    /// <summary>
    /// Shared budget arithmetic for events. Everything here is derived at read time —
    /// nothing is denormalised, so allocations and entries can never disagree.
    ///
    /// The model is Allocated -> Committed -> Paid -> Available:
    ///   Paid       money that has actually left
    ///   Committed  agreed or quoted but not yet paid — it still claims budget
    ///   Actual     Paid + Committed, i.e. everything the budget is on the hook for
    ///   Available  Allocated - Actual  (positive = under plan, negative = over)
    ///
    /// Variance convention, identical at sub-event and event level:
    ///   positive => under budget (green, leading +)
    ///   negative => over budget  (red,   leading -)
    /// </summary>
    public abstract class RollupBase
    {
        public decimal Allocated { get; set; }
        public decimal Paid { get; set; }
        public decimal Committed { get; set; }

        /// <summary>Everything the allocation is on the hook for.</summary>
        public decimal Actual => Paid + Committed;

        /// <summary>What is left of the allocation. Negative means over budget.</summary>
        public decimal Available => Allocated - Actual;

        /// <summary>Alias kept for readability where "variance" is the natural word.</summary>
        public decimal Variance => Available;

        public bool IsOverBudget => Available < 0m;
        public bool HasCommitted => Committed > 0m;

        /// <summary>False when nothing has been budgeted yet.</summary>
        public bool HasAllocation => Allocated > 0m;

        /// <summary>Uncapped share of the allocation used, e.g. 116.7.</summary>
        public decimal RawPercent =>
            HasAllocation ? Math.Round(Actual / Allocated * 100m, 1) : 0m;

        // ---- meter geometry -------------------------------------------
        // The track spans max(allocated, actual), so an overrun is drawn running PAST
        // the plan tick instead of a bar silently pinned at 100%.
        //
        //   under:  |==paid==|~committed~|            . plan at the right edge
        //   over:   |==paid========|~committed~|      . plan tick sits mid-track
        private decimal Scale => Math.Max(Allocated, Actual);

        public decimal PaidPercent =>
            Scale > 0m ? Math.Round(Paid / Scale * 100m, 2) : 0m;

        public decimal CommittedPercent =>
            Scale > 0m ? Math.Round(Committed / Scale * 100m, 2) : 0m;

        /// <summary>Where the allocation sits on the track — the "plan" tick.</summary>
        public decimal PlanPercent =>
            Scale > 0m && Allocated > 0m ? Math.Round(Allocated / Scale * 100m, 2) : 0m;

        // ---- budget health --------------------------------------------
        /// <summary>Modifier class driving the accent for the health state.</summary>
        public string HealthClass =>
            !HasAllocation ? "is-none"
            : RawPercent > 100m ? "is-over"
            : RawPercent >= 85m ? "is-tight"
            : "is-ok";

        /// <summary>Short state word shown beside the dot.</summary>
        public string HealthLabel =>
            !HasAllocation ? "Not budgeted"
            : RawPercent > 100m ? "Over budget"
            : RawPercent >= 85m ? "Watch"
            : "Healthy";
    }

    /// <summary>Event-level roll-up, including money received.</summary>
    public class EventTotals : RollupBase
    {
        public decimal Received { get; set; }

        /// <summary>What the event actually costs once contributions are netted off.</summary>
        public decimal NetOutlay => Actual - Received;

        public int SubEventCount { get; set; }
        public int SpendCount { get; set; }
        public int CommittedCount { get; set; }
        public int ContributionCount { get; set; }
    }

    /// <summary>One sub-event row on the details page, with its dated entries.</summary>
    public class SubEventLine : RollupBase
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int SortOrder { get; set; }
        public List<EventSpendLine> Spends { get; set; } = new();
    }

    public record EventSpendLine(
        int Id, decimal Amount, DateTime Date, string? PaidTo, string? Note, SpendStatus Status)
    {
        public bool IsCommitted => Status == SpendStatus.Committed;
    }

    public record EventContributionLine(int Id, decimal Amount, DateTime Date, string? FromWhom, string? Note);

    /// <summary>Backing model for /Events/Details/{id} — the event workspace.</summary>
    public class EventDetailsViewModel
    {
        public Event Event { get; set; } = default!;
        public EventTotals Totals { get; set; } = new();
        public List<SubEventLine> SubEvents { get; set; } = new();
        public List<EventContributionLine> Contributions { get; set; } = new();
    }

    /// <summary>One card on /Events.</summary>
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

    /// <summary>
    /// What the shared _Meter partial needs: any roll-up, plus the optional hover hint
    /// and accessible label. Keeps the bar defined in exactly one place.
    /// </summary>
    public record MeterModel(RollupBase Roll, string? Hint = null, string? AriaLabel = null);

    /// <summary>Counts behind the filter rail on the index.</summary>
    public class EventFilterCounts
    {
        public int All { get; set; }
        public int Upcoming { get; set; }
        public int Planning { get; set; }
        public int Active { get; set; }
        public int Completed { get; set; }
        public int Archived { get; set; }
    }

    /// <summary>Flat row used by the Events export (xlsx / pdf / csv).</summary>
    public record EventExportRow(
        string EventName,
        string EventType,
        string Status,
        DateTime? EventDate,
        string SubEvent,
        decimal Allocated,
        decimal Paid,
        decimal Committed,
        decimal Available);
}
