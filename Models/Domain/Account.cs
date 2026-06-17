using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public enum AccountType
    {
        Cash = 1,
        Bank = 2,
        Credit = 3,
        Wallet = 4,
        UPI = 5
    }

    /// <summary>
    /// A "wallet" or money location — Cash, a savings bank account, a credit card,
    /// a UPI app's balance, etc. Balances are derived (OpeningBalance + Incomes - Expenses
    /// + Transfers in - Transfers out), not stored.
    /// </summary>
    public class Account
    {
        public int Id { get; set; }

        [Required, StringLength(50, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        public AccountType Type { get; set; }

        // Opening balance lets users start tracking mid-life without a fictional zero point.
        public decimal OpeningBalance { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
