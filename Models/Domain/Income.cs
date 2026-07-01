using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Validation;

namespace ExpenseTracker.Models.Domain
{
    public class Income
    {
        public int Id { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [NotInFuture]
        public DateTime Date { get; set; }

        [Required]
        public string Source { get; set; } = string.Empty;

        // FK to user-scoped Category master. Nullable so legacy income rows
        // (created before the category master existed) and uncategorised income remain valid.
        [Display(Name = "Category")]
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }

        // Optional — which wallet/account the money landed in.
        [Display(Name = "Account")]
        public int? AccountId { get; set; }
        public Account? Account { get; set; }

        public string? UserId { get; set; }

        [Required]
        public string YearMonth { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }
    }
}
