namespace ExpenseTracker.Services
{
    /// <summary>
    /// Pre-computed financial picture for one user for one month.
    /// Shape mirrors the spec in docs/Prompt_Share — every figure is
    /// computed locally in <see cref="FinancialAnalyzer"/>; nothing is sent
    /// to any external service.
    /// </summary>
    public class FinancialSnapshot
    {
        public string Period { get; set; } = string.Empty;       // yyyy-MM
        public string Currency { get; set; } = "INR";

        // When set, this snapshot is "month-to-date": only days 1..AsOf are
        // included, and the renderer should phrase the prose accordingly
        // (e.g. "1–15 June 2026" rather than "June 2026"). null = full month.
        public DateTime? AsOf { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal OutstandingDues { get; set; }              // v1: always 0
        public decimal SavingsRate { get; set; }                  // 0.0–1.0

        public List<CategorySpend> Categories { get; set; } = new();
        public List<RecurringCharge> RecurringCharges { get; set; } = new();
        public List<Anomaly> Anomalies { get; set; } = new();
        public List<BudgetLine> Budgets { get; set; } = new();
        public List<Opportunity> Opportunities { get; set; } = new();

        // Per-category trailing-3-month averages, exposed for the "Budget to aim for"
        // section so the renderer can suggest a sensible target without recomputing.
        public Dictionary<string, decimal> TrailingAverages { get; set; } = new();
    }

    public class CategorySpend
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal PctOfSpend { get; set; }                   // 0–100
        // null = no prior-month spend in this category (avoid divide-by-zero).
        public decimal? MoMChangePct { get; set; }
    }

    public class RecurringCharge
    {
        public string Merchant { get; set; } = string.Empty;
        public decimal Amount { get; set; }                       // median across detected occurrences
        public string Frequency { get; set; } = "monthly";
        public DateTime LastSeen { get; set; }
    }

    public class Anomaly
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }                       // EXCESS above trailing 3-month average
        public string Note { get; set; } = string.Empty;
    }

    public class BudgetLine
    {
        public string Category { get; set; } = string.Empty;
        public decimal Budget { get; set; }
        public decimal Actual { get; set; }
        public decimal VariancePct { get; set; }                  // + over, - under
    }

    public class Opportunity
    {
        // "subscription" | "overspend" | "spike" | "fee" | "rate"
        public string Type { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public decimal MonthlySaving { get; set; }
        public string Difficulty { get; set; } = "medium";        // easy | medium | hard
        public string Evidence { get; set; } = string.Empty;
    }
}
