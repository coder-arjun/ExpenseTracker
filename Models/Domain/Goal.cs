using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public enum GoalStatus
    {
        Active = 1,
        Completed = 2,
        Abandoned = 3
    }

    /// <summary>
    /// A named savings/spending target. Progress is sum(Contributions.Amount).
    /// Status flips to Completed when sum &gt;= TargetAmount.
    /// </summary>
    public class Goal
    {
        public int Id { get; set; }

        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public decimal TargetAmount { get; set; }

        [DataType(DataType.Date)]
        public DateTime? Deadline { get; set; }

        public GoalStatus Status { get; set; } = GoalStatus.Active;

        [StringLength(500)]
        public string? Description { get; set; }

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<GoalContribution> Contributions { get; set; } = new List<GoalContribution>();
    }

    public class GoalContribution
    {
        public int Id { get; set; }
        public int GoalId { get; set; }
        public Goal? Goal { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [StringLength(200)]
        public string? Note { get; set; }

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
