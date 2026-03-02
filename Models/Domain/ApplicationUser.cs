using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Models.Domain
{
    public class ApplicationUser:IdentityUser
    {
        ICollection<Expense> Expenses { get; set; } = [];
        ICollection<Income> Incomes { get; set; } = [];
        ICollection<Saving> Savings { get; set; } = [];
    }
}