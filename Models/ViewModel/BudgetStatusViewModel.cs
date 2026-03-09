using ExpenseTracker.Models.Domain;

namespace ExpenseTracker.Models.ViewModel
{
    public class BudgetStatusViewModel
    {
        public int Id { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public decimal BudgetAmount { get; set; }
        public decimal ActualSpent { get; set; }
        public decimal Remaining => BudgetAmount - ActualSpent;
        public decimal PercentageUsed => BudgetAmount > 0 ? Math.Round((ActualSpent / BudgetAmount) * 100, 1) : 0;
        public ExpenseCategory? Category { get; set; }
    }
}
