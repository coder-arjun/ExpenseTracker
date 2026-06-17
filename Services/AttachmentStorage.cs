using ExpenseTracker.Models.Domain;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Persists uploaded receipts to disk under &lt;ContentRoot&gt;/Storage/Attachments/{userId}/.
    /// Outside wwwroot so files aren't directly URL-addressable; the
    /// <see cref="ExpenseTracker.Controllers.AttachmentsController"/> streams them after auth checks.
    /// </summary>
    public class AttachmentStorage
    {
        // What the client is allowed to upload. Tight by design for a receipts feature.
        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };

        private static readonly HashSet<string> AllowedContentTypes =
            new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp", "application/pdf" };

        public const long MaxBytes = 5 * 1024 * 1024; // 5 MB

        private readonly string _root;

        public AttachmentStorage(IWebHostEnvironment env)
        {
            _root = Path.Combine(env.ContentRootPath, "Storage", "Attachments");
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        public bool IsAccepted(IFormFile file, out string? reason)
        {
            reason = null;
            if (file.Length <= 0) { reason = "File is empty."; return false; }
            if (file.Length > MaxBytes) { reason = $"File too large (max {MaxBytes / (1024 * 1024)} MB)."; return false; }
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            { reason = "Allowed types: JPG, PNG, WEBP, PDF."; return false; }
            if (!AllowedContentTypes.Contains(file.ContentType))
            { reason = "Unrecognised file content type."; return false; }
            return true;
        }

        public async Task<Attachment> SaveAsync(IFormFile file, string userId, int expenseId, CancellationToken ct = default)
        {
            var ext = Path.GetExtension(file.FileName);
            var safeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var userDir = Path.Combine(_root, userId);
            Directory.CreateDirectory(userDir);

            var fullPath = Path.Combine(userDir, safeName);
            await using (var fs = File.Create(fullPath))
            {
                await file.CopyToAsync(fs, ct);
            }

            return new Attachment
            {
                ExpenseId = expenseId,
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                StoredPath = Path.Combine(userId, safeName).Replace('\\', '/'),
                UploadedAt = DateTime.UtcNow,
                UserId = userId,
            };
        }

        public string FullPath(Attachment a) => Path.Combine(_root, a.StoredPath.Replace('/', Path.DirectorySeparatorChar));

        public void Delete(Attachment a)
        {
            var p = FullPath(a);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}
