using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    /// <summary>
    /// Moves money between two of the user's accounts. Not an expense; not income.
    /// </summary>
    public class Transfer
    {
        public int Id { get; set; }

        public int FromAccountId { get; set; }
        public Account? FromAccount { get; set; }

        public int ToAccountId { get; set; }
        public Account? ToAccount { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime Date { get; set; }

        public string? Description { get; set; }

        // yyyy-MM for consistency with other entities; derived from Date server-side.
        [Required, StringLength(7)]
        public string YearMonth { get; set; } = string.Empty;

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
