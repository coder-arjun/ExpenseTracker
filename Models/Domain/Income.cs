using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public class Income
    {
        public int Id { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required]
        public string Source { get; set; } = string.Empty;

        public string? UserId { get; set; }

        [Required]
        public string YearMonth { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }
    }
}
