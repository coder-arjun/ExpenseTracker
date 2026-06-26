namespace ExpenseTracker.Models.Domain
{
    /// <summary>
    /// One row per (user, period) statement we've emailed. Lets the month-end
    /// cron run be idempotent — it skips users already sent for that period.
    /// </summary>
    public class StatementDelivery
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;   // "yyyy-MM"
        public DateTime SentAtUtc { get; set; }
        public bool Success { get; set; }
    }
}
