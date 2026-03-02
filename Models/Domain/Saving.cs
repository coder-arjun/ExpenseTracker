using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public class Saving
    {
        public int Id { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        public string Note { get; set; } = string.Empty;

        [Required]
        public string YearMonth { get; set; } = string.Empty;

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
