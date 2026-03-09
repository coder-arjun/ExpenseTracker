using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Expense>().HasIndex(e=>new { e.UserId, e.Month });
            builder.Entity<Saving>().HasIndex(s=>new { s.UserId, s.YearMonth });
            builder.Entity<Income>().HasIndex(i => new { i.UserId, i.YearMonth });
            builder.Entity<Budget>().HasIndex(b => new { b.UserId, b.YearMonth, b.Category }).IsUnique();
        }
    }
}


