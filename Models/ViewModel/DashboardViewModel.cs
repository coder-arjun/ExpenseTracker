namespace ExpenseTracker.Models.ViewModel
{
    public class DashboardViewModel
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal TotalSaving { get; set; }
        public decimal NetBalance { get; set; }
        public decimal SavingsRate { get; set; }

        // Filter params
        public string FilterType { get; set; } = "Month"; // Month, Year, DateRange
        public string? SelectedMonth { get; set; } // yyyy-MM
        public int? SelectedYear { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        // Chart data
        public Dictionary<string, decimal> ExpensesByCategory { get; set; } = new();
        public Dictionary<string, decimal> MonthlyExpenses { get; set; } = new();
        public Dictionary<string, decimal> MonthlyIncomes { get; set; } = new();
        public Dictionary<string, decimal> MonthlySavings { get; set; } = new();

        // Recent transactions
        public List<RecentTransaction> RecentTransactions { get; set; } = new();

        // Money leakage tracker
        public List<MoneyLeakage> TopLeakages { get; set; } = [];
        public List<BudgetStatusViewModel> BudgetAlerts { get; set; } = [];
    }

    public class MoneyLeakage
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Percentage { get; set; }
    }

    public class RecentTransaction
    {
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty; // Income, Expense, Saving
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
