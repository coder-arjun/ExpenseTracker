using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Validation
{
    /// <summary>
    /// Validation attribute that rejects a date in the future (compared to today).
    /// Null passes (leave emptiness to <see cref="RequiredAttribute"/>). Applied to
    /// transaction dates (income/expense/debt start) — not to forward-looking dates
    /// like a debt's due date.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class NotInFutureAttribute : ValidationAttribute
    {
        public NotInFutureAttribute() : base("The {0} can't be in the future.") { }

        public override bool IsValid(object? value)
        {
            if (value is null) return true;
            if (value is DateTime dt) return dt.Date <= DateTime.Today;
            return true;
        }

        public override string FormatErrorMessage(string name) =>
            string.Format(System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name);
    }
}
