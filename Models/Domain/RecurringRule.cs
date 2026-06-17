using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public enum RecurringType
    {
        Expense = 1,
        Income = 2
    }

    public enum RecurrenceFrequency
    {
        Monthly = 1,
        // Weekly + Yearly reserved for later; v1 implementation handles Monthly only.
        Weekly = 2,
        Yearly = 3
    }

    /// <summary>
    /// A scheduled transaction that materialises on its due date.
    /// Auto-posted lazily — when the user opens the Dashboard, due rules
    /// catch up and post any missed occurrences up to today.
    /// </summary>
    public class RecurringRule
    {
        public int Id { get; set; }

        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        public RecurringType Type { get; set; } = RecurringType.Expense;

        [Required]
        public decimal Amount { get; set; }

        [StringLength(200)]
        public string? Description { get; set; }

        // Only one of CategoryId / Source applies depending on Type.
        // For Expense → CategoryId required; for Income → Source required.
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }

        [StringLength(100)]
        public string? Source { get; set; }

        public int? AccountId { get; set; }
        public Account? Account { get; set; }

        public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

        // For Monthly rules — day-of-month (1..28). Capped at 28 to dodge Feb/30-day edge cases.
        public int DayOfMonth { get; set; } = 1;

        [Required, DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime NextDueDate { get; set; }

        public bool IsActive { get; set; } = true;

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
