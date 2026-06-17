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
        public DbSet<MonthlyInsight> MonthlyInsights => Set<MonthlyInsight>();
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<Transfer> Transfers => Set<Transfer>();
        public DbSet<Goal> Goals => Set<Goal>();
        public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();
        public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();
        public DbSet<Attachment> Attachments => Set<Attachment>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Expense>().HasIndex(e => new { e.UserId, e.Month });
            builder.Entity<Saving>().HasIndex(s => new { s.UserId, s.YearMonth });
            builder.Entity<Income>().HasIndex(i => new { i.UserId, i.YearMonth });
            builder.Entity<Budget>().HasIndex(b => new { b.UserId, b.YearMonth, b.CategoryId }).IsUnique();
            builder.Entity<ApplicationUser>().HasIndex(u => u.DisplayUserId).IsUnique();

            // Pin decimal precision so EF stops warning about silent truncation.
            builder.Entity<Expense>().Property(e => e.Amount).HasPrecision(18, 2);
            builder.Entity<Income>().Property(i => i.Amount).HasPrecision(18, 2);
            builder.Entity<Saving>().Property(s => s.Amount).HasPrecision(18, 2);
            builder.Entity<Budget>().Property(b => b.Amount).HasPrecision(18, 2);
            builder.Entity<Account>().Property(a => a.OpeningBalance).HasPrecision(18, 2);
            builder.Entity<Transfer>().Property(t => t.Amount).HasPrecision(18, 2);
            builder.Entity<Goal>().Property(g => g.TargetAmount).HasPrecision(18, 2);
            builder.Entity<GoalContribution>().Property(g => g.Amount).HasPrecision(18, 2);
            builder.Entity<RecurringRule>().Property(r => r.Amount).HasPrecision(18, 2);

            // One name per (user, type). Filtered index so different users can have
            // identically-named categories.
            builder.Entity<Category>()
                .HasIndex(c => new { c.UserId, c.Type, c.Name })
                .IsUnique();
            builder.Entity<Category>().HasIndex(c => c.UserId);

            // One cached insight per (user, period).
            builder.Entity<MonthlyInsight>()
                .HasIndex(m => new { m.UserId, m.Period })
                .IsUnique();

            // ---- Accounts -----------------------------------------------
            builder.Entity<Account>().HasIndex(a => a.UserId);
            // Unique account name per user — prevents accidental duplicates.
            builder.Entity<Account>()
                .HasIndex(a => new { a.UserId, a.Name })
                .IsUnique();

            // ---- Transfers ----------------------------------------------
            builder.Entity<Transfer>().HasIndex(t => new { t.UserId, t.YearMonth });
            builder.Entity<Transfer>()
                .HasOne(t => t.FromAccount)
                .WithMany()
                .HasForeignKey(t => t.FromAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Transfer>()
                .HasOne(t => t.ToAccount)
                .WithMany()
                .HasForeignKey(t => t.ToAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense/Income optional Account FK — SetNull on delete so deleting an account
            // doesn't nuke historical transactions.
            builder.Entity<Expense>()
                .HasOne(e => e.Account)
                .WithMany()
                .HasForeignKey(e => e.AccountId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.Entity<Income>()
                .HasOne(i => i.Account)
                .WithMany()
                .HasForeignKey(i => i.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            // ---- Goals --------------------------------------------------
            builder.Entity<Goal>().HasIndex(g => new { g.UserId, g.Status });
            builder.Entity<GoalContribution>()
                .HasOne(c => c.Goal)
                .WithMany(g => g.Contributions)
                .HasForeignKey(c => c.GoalId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<GoalContribution>().HasIndex(c => c.GoalId);

            // ---- Recurring rules ---------------------------------------
            builder.Entity<RecurringRule>().HasIndex(r => new { r.UserId, r.IsActive, r.NextDueDate });
            builder.Entity<RecurringRule>()
                .HasOne(r => r.Category)
                .WithMany()
                .HasForeignKey(r => r.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<RecurringRule>()
                .HasOne(r => r.Account)
                .WithMany()
                .HasForeignKey(r => r.AccountId)
                .OnDelete(DeleteBehavior.SetNull);

            // ---- Attachments -------------------------------------------
            builder.Entity<Attachment>()
                .HasOne(a => a.Expense)
                .WithMany(e => e.Attachments)
                .HasForeignKey(a => a.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Attachment>().HasIndex(a => a.ExpenseId);

            // ---- Existing Category cascading rules (unchanged) ---------
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
