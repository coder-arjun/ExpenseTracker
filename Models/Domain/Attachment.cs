using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    /// <summary>
    /// A file attached to an Expense (typically a receipt). Files live outside
    /// wwwroot under &lt;ContentRoot&gt;/Storage/Attachments/{userId}/{guid}.{ext}.
    /// They are streamed only through the authorised
    /// <see cref="ExpenseTracker.Controllers.AttachmentsController"/>.
    /// </summary>
    public class Attachment
    {
        public int Id { get; set; }

        public int ExpenseId { get; set; }
        public Expense? Expense { get; set; }

        [Required, StringLength(200)]
        public string FileName { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        // Relative path under the storage root; e.g. "{userId}/abc123.jpg".
        // Storing relative keeps the DB portable across hosts.
        [Required, StringLength(300)]
        public string StoredPath { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
