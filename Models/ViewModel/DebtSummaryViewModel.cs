namespace ExpenseTracker.Models.ViewModel
{
    /// <summary>One open debt, condensed for the Insights "Money owed" card.</summary>
    public record DebtPeek(string Name, decimal Outstanding, bool TheyOweMe, bool Overdue);

    /// <summary>
    /// Live snapshot of the user's IOU ledger, surfaced on the Insights page.
    /// Independent of the selected period — it's a running balance, not a monthly figure.
    /// </summary>
    public class DebtSummaryViewModel
    {
        public decimal OwedToMe { get; set; }
        public decimal IOwe { get; set; }
        public decimal Net => OwedToMe - IOwe;
        public int OverdueCount { get; set; }
        public bool HasAny => OwedToMe > 0m || IOwe > 0m;
        public List<DebtPeek> Top { get; set; } = new();
    }
}
