using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    /// <summary>
    /// Cached, pre-rendered Markdown insights for one user / one period.
    /// All analysis is computed locally (no external API), then stored here
    /// so re-opening the Insights page doesn't recompute every time.
    /// </summary>
    public class MonthlyInsight
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        // yyyy-MM, e.g. "2026-05"
        [Required]
        [StringLength(7)]
        public string Period { get; set; } = string.Empty;

        // The full rendered Markdown document. Generation is cheap, so storing
        // Markdown (rather than HTML) lets us re-render with a newer template
        // without a full regeneration.
        [Required]
        public string MarkdownContent { get; set; } = string.Empty;

        public DateTime GeneratedAt { get; set; }
    }
}
