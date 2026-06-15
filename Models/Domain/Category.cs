using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Models.Domain
{
    public class Category
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public CategoryType Type { get; set; }

        // User who owns this category. All categories are user-scoped; defaults
        // are seeded per-user on registration and via the AddCategoryMaster migration
        // for existing users. UserId is set server-side from UserManager.GetUserId.
        public string? UserId { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
