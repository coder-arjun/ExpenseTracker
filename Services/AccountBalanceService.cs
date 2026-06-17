using ExpenseTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Derives current balances for the user's accounts from
    /// OpeningBalance + (incomes - expenses) + (transfers in - out).
    /// Saving entries don't have an Account FK in v1 so they're ignored.
    /// </summary>
    public class AccountBalanceService
    {
        private readonly ApplicationDbContext _db;

        public AccountBalanceService(ApplicationDbContext db) => _db = db;

        public async Task<Dictionary<int, decimal>> GetBalancesAsync(string userId)
        {
            var accounts = await _db.Accounts
                .Where(a => a.UserId == userId)
                .Select(a => new { a.Id, a.OpeningBalance })
                .ToListAsync();

            var balances = accounts.ToDictionary(a => a.Id, a => a.OpeningBalance);

            // Incomes credit, expenses debit (only those with AccountId set).
            var incomes = await _db.Incomes
                .Where(i => i.UserId == userId && i.AccountId != null)
                .GroupBy(i => i.AccountId!.Value)
                .Select(g => new { AccountId = g.Key, Total = g.Sum(i => i.Amount) })
                .ToListAsync();
            foreach (var x in incomes)
                if (balances.ContainsKey(x.AccountId)) balances[x.AccountId] += x.Total;

            var expenses = await _db.Expenses
                .Where(e => e.UserId == userId && e.AccountId != null)
                .GroupBy(e => e.AccountId!.Value)
                .Select(g => new { AccountId = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync();
            foreach (var x in expenses)
                if (balances.ContainsKey(x.AccountId)) balances[x.AccountId] -= x.Total;

            // Transfers: subtract from source, add to destination.
            var transfersOut = await _db.Transfers
                .Where(t => t.UserId == userId)
                .GroupBy(t => t.FromAccountId)
                .Select(g => new { AccountId = g.Key, Total = g.Sum(t => t.Amount) })
                .ToListAsync();
            foreach (var x in transfersOut)
                if (balances.ContainsKey(x.AccountId)) balances[x.AccountId] -= x.Total;

            var transfersIn = await _db.Transfers
                .Where(t => t.UserId == userId)
                .GroupBy(t => t.ToAccountId)
                .Select(g => new { AccountId = g.Key, Total = g.Sum(t => t.Amount) })
                .ToListAsync();
            foreach (var x in transfersIn)
                if (balances.ContainsKey(x.AccountId)) balances[x.AccountId] += x.Total;

            return balances;
        }
    }
}
