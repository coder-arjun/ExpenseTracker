using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public class ApplicationUser:IdentityUser
    {
        ICollection<Expense> Expenses { get; set; } = [];
        ICollection<Income> Incomes { get; set; } = [];
        ICollection<Saving> Savings { get; set; } = [];

        [Required]
        [StringLength(30, MinimumLength = 3)]
        public string DisplayUserId { get; set; } = string.Empty;
    }
}