using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExpenseTracker.Models.Domain
{
    /// <summary>
    /// Which way the money flows for a <see cref="Debt"/> record.
    /// </summary>
    public enum DebtDirection
    {
        /// <summary>Someone owes the user money (a receivable / IOU in the user's favour).</summary>
        TheyOweMe = 1,
        /// <summary>The user owes someone else money (a payable).</summary>
        IOweThem = 2
    }

    /// <summary>
    /// Derived settlement state — never stored, computed from Amount vs AmountPaid.
    /// </summary>
    public enum DebtStatus
    {
        Outstanding,
        PartlyPaid,
        Settled
    }

    /// <summary>
    /// A personal IOU: money someone owes the user (<see cref="DebtDirection.TheyOweMe"/>)
    /// or money the user owes someone (<see cref="DebtDirection.IOweThem"/>). Supports
    /// partial repayments via <see cref="AmountPaid"/>; the running balance and status
    /// are computed, not stored.
    /// </summary>
    public class Debt
    {
        public int Id { get; set; }

        [Required]
        public DebtDirection Direction { get; set; } = DebtDirection.TheyOweMe;

        [Required]
        [StringLength(80)]
        [Display(Name = "Person")]
        public string PersonName { get; set; } = string.Empty;

        [Required]
        public decimal Amount { get; set; }

        /// <summary>How much has been repaid/settled so far. Kept in [0, Amount] by the controller.</summary>
        [Display(Name = "Amount paid")]
        public decimal AmountPaid { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Due date")]
        public DateTime? DueDate { get; set; }

        public string? Note { get; set; }

        /// <summary>"yyyy-MM" of <see cref="Date"/>; derived server-side for indexing parity with the other ledgers.</summary>
        [Required]
        public string YearMonth { get; set; } = string.Empty;

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        // ----- computed (not persisted) -------------------------------------
        [NotMapped]
        public decimal Outstanding => Math.Max(0m, Amount - AmountPaid);

        [NotMapped]
        public DebtStatus Status =>
            AmountPaid <= 0m ? DebtStatus.Outstanding
            : AmountPaid >= Amount ? DebtStatus.Settled
            : DebtStatus.PartlyPaid;

        [NotMapped]
        public bool IsOverdue =>
            DueDate.HasValue && DueDate.Value.Date < DateTime.Today && Outstanding > 0m;
    }
}
