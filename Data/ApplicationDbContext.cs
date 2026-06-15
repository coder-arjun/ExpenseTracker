using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        { }
        public DbSet<Income> Incomes => Set<Income>();
        public DbSet<Saving> Savings => Set<Saving>();
        public DbSet<Expense> Expenses => Set<Expense>();
        public DbSet<Budget> Budgets => Set<Budget>();
        public DbSet<Category> Categories => Set<Category>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Expense>().HasIndex(e => new { e.UserId, e.Month });
            builder.Entity<Saving>().HasIndex(s => new { s.UserId, s.YearMonth });
            builder.Entity<Income>().HasIndex(i => new { i.UserId, i.YearMonth });
            builder.Entity<Budget>().HasIndex(b => new { b.UserId, b.YearMonth, b.CategoryId }).IsUnique();
            builder.Entity<ApplicationUser>().HasIndex(u => u.DisplayUserId).IsUnique();

            // One name per (user, type). Filtered index so different users can have
            // identically-named categories.
            builder.Entity<Category>()
                .HasIndex(c => new { c.UserId, c.Type, c.Name })
                .IsUnique();
            builder.Entity<Category>().HasIndex(c => c.UserId);

            // Don't cascade-delete transactions when a Category is deleted —
            // the controller blocks deletion if the category is in use, so we want
            // restrictive behaviour rather than silent data loss.
            builder.Entity<Expense>()
                .HasOne(e => e.Category)
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Income>()
                .HasOne(i => i.Category)
                .WithMany()
                .HasForeignKey(i => i.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Budget>()
                .HasOne(b => b.Category)
                .WithMany()
                .HasForeignKey(b => b.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
