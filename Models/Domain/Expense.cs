using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExpenseTracker.Models.Domain
{
    public class Expense
    {
        public int Id { get; set; }
        public required decimal Amount { get; set; }
        [DataType(DataType.Date)]
        public required DateTime Date { get; set; }
        public string? Description { get; set; }
        public required ExpenseCategory Category { get; set; }
        public string? UserId { get; set; }
        public required string Month { get; set; }
        public ApplicationUser? User { get; set; } = default!;
    }
}