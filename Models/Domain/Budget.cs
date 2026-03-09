namespace ExpenseTracker.Models.Domain
{
    public class Budget
    {
        public int Id { get; set; }
        public required decimal Amount { get; set; }        
        public ExpenseCategory? Category { get; set; } 
        public required string YearMonth { get; set; } 
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; } = default;
    }
}
