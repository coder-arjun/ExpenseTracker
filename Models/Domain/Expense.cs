using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExpenseTracker.Validation;

namespace ExpenseTracker.Models.Domain
{
    public class Expense
    {
        public int Id { get; set; }
        public required decimal Amount { get; set; }
        [DataType(DataType.Date)]
        [NotInFuture]
        public required DateTime Date { get; set; }
        public string? Description { get; set; }

        // FK to the user-scoped Category master. Required for expenses.
        [Display(Name = "Category")]
        public required int CategoryId { get; set; }
        public Category? Category { get; set; }

        // Optional — which wallet/account the money came out of. Nullable so existing
        // expenses (created before the Accounts feature) remain valid.
        [Display(Name = "Account")]
        public int? AccountId { get; set; }
        public Account? Account { get; set; }

        public string? UserId { get; set; }
        public required string Month { get; set; }
        public ApplicationUser? User { get; set; } = default!;

        // Receipts attached to this expense (zero or more).
        public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    }
}
