namespace ExpenseTracker.Models.ViewModel
{
    /// <summary>
    /// Strongly-typed model for the full-page search results (the navbar search
    /// bar posts here). Replaces the previous ViewData + dynamic-tuple approach,
    /// which threw RuntimeBinderException at runtime because ValueTuple element
    /// names do not exist after compilation.
    /// </summary>
    public record SearchNavHit(string Label, string Url, string Icon);

    public record SearchCategoryHit(string Name, string Type, string Url);

    public record SearchExpenseHit(string? Description, string Amount, string Month, string Url);

    public record SearchDebtHit(string Person, string Direction, string Outstanding, bool TheyOweMe, string Url);

    public class SearchResultsViewModel
    {
        public string Query { get; set; } = "";
        public List<SearchNavHit> Nav { get; set; } = new();
        public List<SearchCategoryHit> Categories { get; set; } = new();
        public List<SearchExpenseHit> Expenses { get; set; } = new();
        public List<SearchDebtHit> Debts { get; set; } = new();

        public int TotalCount => Nav.Count + Categories.Count + Expenses.Count + Debts.Count;
        public bool HasAny => TotalCount > 0;
    }
}
