using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public class Budget
    {
        public int Id { get; set; }
        public required decimal Amount { get; set; }

        // FK to the user-scoped Category master. NULL = "Overall" budget for the month.
        [Display(Name = "Category")]
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }

        public required string YearMonth { get; set; }
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; } = default;
    }
}
