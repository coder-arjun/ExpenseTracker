using System.Globalization;
using System.Text;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Turns a <see cref="FinancialSnapshot"/> into a friendly Markdown summary.
    /// Pure C# — no LLM call. Follows the section structure from the spec:
    ///   ## Overview
    ///   ## Spending anomalies
    ///   ## How you can save
    ///   ## A budget to aim for
    ///   ## Do these first
    ///
    /// NOTE: "Spending anomalies" was previously "Where your money is leaking".
    /// Already-cached MonthlyInsight rows keep the old heading until regenerated,
    /// so the view matches on both.
    /// </summary>
    public static class InsightsRenderer
    {
        private static readonly CultureInfo INR = CultureInfo.GetCultureInfo("en-IN");

        public static string Render(FinancialSnapshot s)
        {
            var sb = new StringBuilder();

            WriteOverview(sb, s);
            WriteLeaks(sb, s);
            WriteSavings(sb, s);
            WriteBudgetTarget(sb, s);
            WriteDoFirst(sb, s);

            sb.AppendLine();
            sb.AppendLine("_This is general money-management guidance, not regulated financial or investment advice._");
            return sb.ToString();
        }

        // ─── ## Overview ────────────────────────────────────────────────────
        private static void WriteOverview(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## Overview");
            sb.AppendLine();

            var net = s.TotalIncome - s.TotalExpenses;
            var savingsPct = (int)Math.Round(s.SavingsRate * 100);
            var periodLabel = PeriodLabel(s);

            if (s.TotalIncome == 0 && s.TotalExpenses == 0)
            {
                sb.AppendLine($"There's no income or expense data recorded for {periodLabel} yet, so there's nothing to analyse for this period.");
                sb.AppendLine();
                return;
            }

            var preposition = s.AsOf.HasValue ? "So far in" : "In";
            if (net >= 0)
            {
                sb.Append($"{preposition} {periodLabel} you earned {Money(s.TotalIncome)} and spent {Money(s.TotalExpenses)}, ");
                sb.Append($"keeping **{Money(net)}** ({savingsPct}% of your income).");
            }
            else
            {
                sb.Append($"{preposition} {periodLabel} you earned {Money(s.TotalIncome)} but spent {Money(s.TotalExpenses)}, ");
                sb.Append($"which is **{Money(-net)} more than you earned**.");
            }

            // For MTD, add a "X days in, Y to go" sentence so the user has context.
            if (s.AsOf.HasValue)
            {
                var daysIn = s.AsOf.Value.Day;
                var totalDays = DateTime.DaysInMonth(s.AsOf.Value.Year, s.AsOf.Value.Month);
                var daysLeft = totalDays - daysIn;
                sb.Append($" You're {daysIn} day{(daysIn == 1 ? "" : "s")} into the month with {daysLeft} to go.");
            }

            // The one thing to notice — pick the strongest signal.
            var headline = ChooseHeadline(s);
            if (!string.IsNullOrEmpty(headline))
                sb.Append(' ').Append(headline);

            sb.AppendLine();
            sb.AppendLine();
        }

        private static string ChooseHeadline(FinancialSnapshot s)
        {
            // Order of precedence: worst overspend > biggest anomaly > biggest rising category > top spend category.
            var worstOver = s.Budgets
                .Where(b => b.Actual > b.Budget)
                .OrderByDescending(b => b.Actual - b.Budget)
                .FirstOrDefault();
            if (worstOver != null)
                return $"The biggest thing to notice: **{worstOver.Category}** is over budget by {Money(worstOver.Actual - worstOver.Budget)}.";

            var biggestAnomaly = s.Anomalies.OrderByDescending(a => a.Amount).FirstOrDefault();
            if (biggestAnomaly != null)
                return $"The biggest thing to notice: **{biggestAnomaly.Category}** is well above its usual level this month.";

            var risingFast = s.Categories
                .Where(c => c.MoMChangePct.HasValue && c.MoMChangePct > 25 && c.Amount > 1000)
                .OrderByDescending(c => c.MoMChangePct)
                .FirstOrDefault();
            if (risingFast != null)
                return $"The biggest thing to notice: **{risingFast.Category}** rose {risingFast.MoMChangePct:+0.0;-0.0}% vs last month.";

            var topCat = s.Categories.FirstOrDefault();
            if (topCat != null)
                return $"Your largest spend was **{topCat.Category}** at {Money(topCat.Amount)} ({topCat.PctOfSpend:0.0}% of expenses).";

            return string.Empty;
        }

        // ─── ## Spending anomalies ──────────────────────────────────────────
        private static void WriteLeaks(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## Spending anomalies");
            sb.AppendLine();

            // Unusual = above this category's own normal, or over an agreed budget.
            var unusual = new List<(decimal impact, string line)>();
            // Planned = steady, recurring commitments. Large, but not a surprise.
            var planned = new List<(decimal impact, string line)>();

            // 1. Over-budget lines
            foreach (var b in s.Budgets.Where(b => b.Actual > b.Budget))
            {
                var over = b.Actual - b.Budget;
                unusual.Add((over,
                    $"- **{b.Category}** — spent {Money(b.Actual)} against a budget of {Money(b.Budget)} ({b.VariancePct:+0.0;-0.0}%). That's {Money(over)} over."));
            }

            // 2. Anomalies (a category running well above its own normal)
            foreach (var a in s.Anomalies)
            {
                unusual.Add((a.Amount,
                    $"- **{a.Category}** is running hot — {a.Note} About {Money(a.Amount)} above its usual."));
            }

            // 3. Rising-fast categories (>25% MoM, meaningful amount, not already flagged as anomaly)
            foreach (var c in s.Categories
                         .Where(c => c.MoMChangePct.HasValue && c.MoMChangePct > 25 && c.Amount >= 1000)
                         .Where(c => !s.Anomalies.Any(a => a.Category == c.Category)))
            {
                unusual.Add((c.Amount * 0.1m,
                    $"- **{c.Category}** rose {c.MoMChangePct:+0.0;-0.0}% versus last month ({Money(c.Amount)} this month)."));
            }

            // 4. Recurring charges (likely-forgotten subscriptions)
            foreach (var r in s.RecurringCharges)
            {
                planned.Add((r.Amount,
                    $"- **{r.Merchant}** — a steady {r.Frequency} charge of {Money(r.Amount)} (last on {r.LastSeen:yyyy-MM-dd}). Expected, but worth a periodic review."));
            }

            if (unusual.Count > 0)
            {
                sb.AppendLine("**Unusual this period**");
                sb.AppendLine();
                foreach (var (_, line) in unusual.OrderByDescending(x => x.impact))
                    sb.AppendLine(line);
                sb.AppendLine();
            }
            else
            {
                // "Nothing unusual" must not read as "everything is fine" when the
                // month is in deficit — say what the spending actually was.
                var top = s.Categories.FirstOrDefault();
                var net = s.TotalIncome - s.TotalExpenses;

                if (top != null && net < 0)
                {
                    sb.AppendLine($"Nothing looks *unusual* — no category ran above its own normal and nothing went over budget. " +
                                  $"The level is the issue, not the pattern: **{top.Category}** accounted for {top.PctOfSpend:0.0}% of spending " +
                                  $"({Money(top.Amount)}), and total spending exceeded recorded income by **{Money(-net)}**.");
                }
                else if (top != null)
                {
                    sb.AppendLine($"Nothing unusual this period. No category ran above its own normal and nothing went over budget. " +
                                  $"Your largest category was **{top.Category}** at {Money(top.Amount)} ({top.PctOfSpend:0.0}% of spending), " +
                                  $"which is in line with your recent months.");
                }
                else
                {
                    sb.AppendLine("No spending recorded for this period, so there is nothing to compare against your usual pattern.");
                }
                sb.AppendLine();
            }

            if (planned.Count > 0)
            {
                sb.AppendLine("**Planned or recurring**");
                sb.AppendLine();
                foreach (var (_, line) in planned.OrderByDescending(x => x.impact))
                    sb.AppendLine(line);
                sb.AppendLine();
            }
        }

        // ─── ## How you can save ────────────────────────────────────────────
        private static void WriteSavings(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## How you can save");
            sb.AppendLine();

            if (s.Opportunities.Count == 0)
            {
                // No ranked opportunity does not mean nothing to say. Name the
                // actual gap in this month's data instead of a stock reassurance.
                var net = s.TotalIncome - s.TotalExpenses;
                var top = s.Categories.FirstOrDefault();

                if (s.TotalIncome == 0 && s.TotalExpenses > 0)
                {
                    sb.AppendLine($"**Your biggest opportunity is cash-flow visibility.** {Money(s.TotalExpenses)} of spending was recorded " +
                                  $"for this period, but no income was. Savings rate, affordability and budget headroom all divide by income, " +
                                  $"so none of them can be calculated yet. Add your income entries and this section becomes specific to you.");
                }
                else if (net < 0 && top != null)
                {
                    sb.AppendLine($"No single charge stands out as recoverable — the gap is structural. You spent {Money(-net)} more than you earned, " +
                                  $"and **{top.Category}** alone was {Money(top.Amount)} ({top.PctOfSpend:0.0}% of spending). " +
                                  $"Bringing that one category down to its 3-month average is the largest single lever available.");
                }
                else if (top != null)
                {
                    sb.AppendLine($"Nothing concrete jumped out — no subscription, spike or overrun cleared the reporting threshold. " +
                                  $"Your largest category was **{top.Category}** at {Money(top.Amount)} ({top.PctOfSpend:0.0}% of spending); " +
                                  $"setting a budget against it is the simplest way to keep it that way.");
                }
                else
                {
                    sb.AppendLine("There's no spending recorded for this period, so there's nothing to find savings in yet.");
                }
                sb.AppendLine();
                return;
            }

            foreach (var o in s.Opportunities)
            {
                var actionVerb = o.Type switch
                {
                    "subscription" => "Cancel or review",
                    "fee"          => "Avoid",
                    "rate"         => "Renegotiate or refinance",
                    "spike"        => "Trim the",
                    "overspend"    => "Pull back the",
                    _              => "Review"
                };

                sb.AppendLine($"- **{actionVerb} {o.Label}** — save **{Money(o.MonthlySaving)}/month** ({o.Difficulty}). {o.Evidence}");
            }

            var totalSaving = s.Opportunities.Sum(o => o.MonthlySaving);
            sb.AppendLine();
            sb.AppendLine($"If you tackle all of these, you could free up around **{Money(totalSaving)} a month** — roughly **{Money(totalSaving * 12)} a year**. That's a real boost to your savings rate. 💪");
            sb.AppendLine();
        }

        // ─── ## A budget to aim for ─────────────────────────────────────────
        private static void WriteBudgetTarget(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## A budget to aim for");
            sb.AppendLine();

            // Take the top 5 categories by current spend
            var top = s.Categories.Take(5).ToList();
            if (top.Count == 0)
            {
                sb.AppendLine("No spending recorded yet — set a few budgets from the Budgets page once your first month's data is in.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("| Category | Target / month | Why |");
            sb.AppendLine("|---|---|---|");

            foreach (var c in top)
            {
                var (target, why) = SuggestTarget(c, s);
                sb.AppendLine($"| {c.Category} | {Money(target)} | {why} |");
            }

            sb.AppendLine();
        }

        /// <summary>
        /// One suggested monthly limit per category, ranked by current spend.
        /// Public so the Insights view can draw these as bars from the same
        /// calculation the narrative table uses — there is no second formula.
        /// </summary>
        public static List<SuggestedBudget> SuggestBudgets(FinancialSnapshot s, int take = 5)
        {
            var rows = new List<SuggestedBudget>();
            foreach (var c in s.Categories.Take(take))
            {
                var (target, why) = SuggestTarget(c, s);
                rows.Add(new SuggestedBudget(c.Category, c.Amount, target, why));
            }
            return rows;
        }

        private static (decimal target, string why) SuggestTarget(CategorySpend c, FinancialSnapshot s)
        {
            // 1. If a budget exists and they consistently overspend, suggest a stretch target
            //    midway between current spend and the existing budget — realistic, not punitive.
            var existing = s.Budgets.FirstOrDefault(b => b.Category == c.Category);
            if (existing != null && existing.Actual > existing.Budget)
            {
                var mid = RoundToNearest((existing.Budget + existing.Actual) / 2m, 100);
                return (mid, $"Halfway between your current {Money(existing.Budget)} budget and {Money(existing.Actual)} actual — a realistic stretch.");
            }
            if (existing != null)
            {
                return (existing.Budget, $"You're already on track against your {Money(existing.Budget)} budget — keep it.");
            }

            // 2. Otherwise base on the 3-month trailing average if we have one.
            if (s.TrailingAverages.TryGetValue(c.Category, out var avg) && avg > 0)
            {
                var rounded = RoundToNearest(avg, 100);
                return (rounded, $"Your 3-month average. Steady spend pattern, no change needed.");
            }

            // 3. Fallback — use this month's spend (just established a baseline).
            return (RoundToNearest(c.Amount, 100), $"Based on this month's spend — set a real budget to track it.");
        }

        // ─── ## Do these first ──────────────────────────────────────────────
        private static void WriteDoFirst(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## Do these first");
            sb.AppendLine();

            // Top 3 = highest-saving opportunities, weighted slightly toward easier ones.
            var ranked = s.Opportunities
                .OrderByDescending(o => o.MonthlySaving * DifficultyWeight(o.Difficulty))
                .Take(3)
                .ToList();

            var lines = new List<string>();

            // Highest priority of all: a gap that stops the analysis working.
            if (s.TotalIncome == 0 && s.TotalExpenses > 0)
            {
                lines.Add($"**Record your income** — *High priority.* Savings rate can't be calculated because " +
                          $"{Money(0)} of income was recorded against {Money(s.TotalExpenses)} of spending.");
            }

            foreach (var o in ranked)
                lines.Add($"{ActionLine(o)} *{PriorityFor(o)} priority.*");

            // Fill out to three with concrete, category-named suggestions —
            // never generic filler.
            var covered = new HashSet<string>(s.Budgets.Select(b => b.Category), StringComparer.OrdinalIgnoreCase);

            if (lines.Count < 3)
            {
                var unbudgeted = s.Categories.FirstOrDefault(c => !covered.Contains(c.Category));
                if (unbudgeted != null)
                    lines.Add($"**Set a budget for {unbudgeted.Category}** — *Medium priority.* " +
                              $"It was {Money(unbudgeted.Amount)} this period ({unbudgeted.PctOfSpend:0.0}% of spending) with no budget against it.");
            }

            if (lines.Count < 3)
            {
                var top = s.Categories.FirstOrDefault();
                if (top != null && top.PctOfSpend >= 25m)
                    lines.Add($"**Review your {top.Category} category** — *Medium priority.* " +
                              $"It represented {top.PctOfSpend:0.0}% of this period's spending on its own.");
            }

            if (lines.Count < 3 && s.SavingsRate > 0)
            {
                var pct = (int)Math.Round(s.SavingsRate * 100);
                lines.Add($"**Lift your savings target** — *Low priority.* You kept {pct}% of income this period; " +
                          $"if that holds for another month or two, the target can safely move up.");
            }

            if (lines.Count == 0)
            {
                lines.Add("**Record a month of income and expenses** — *High priority.* " +
                          "There's nothing in this period to act on yet.");
            }

            var i = 1;
            foreach (var line in lines.Take(3))
                sb.AppendLine($"{i++}. {line}");

            sb.AppendLine();
        }

        // Highest-impact first; easy wins outrank hard ones of similar size.
        private static string PriorityFor(Opportunity o) => o.MonthlySaving switch
        {
            >= 5000m => "High",
            >= 1500m => "Medium",
            _        => o.Difficulty == "easy" ? "Medium" : "Low"
        };

        // Every action line has the same shape: **what to do** — why it matters.
        // The view relies on that to split the label from its evidence.
        private static string ActionLine(Opportunity o) => o.Type switch
        {
            "subscription" => $"**Review or cancel {o.Label}** — a steady charge worth {Money(o.MonthlySaving)}/month. {o.Evidence}",
            "spike"        => $"**Trim {o.Label}** — about {Money(o.MonthlySaving)}/month above its usual level. {o.Evidence}",
            "overspend"    => $"**Pull back {o.Label}** — {Money(o.MonthlySaving)}/month over the agreed budget. {o.Evidence}",
            "fee"          => $"**Avoid {o.Label}** — {Money(o.MonthlySaving)}/month recoverable. {o.Evidence}",
            "rate"         => $"**Renegotiate {o.Label}** — would redirect {Money(o.MonthlySaving)}/month into savings. {o.Evidence}",
            _              => $"**Review {o.Label}** — {Money(o.MonthlySaving)}/month at stake. {o.Evidence}"
        };

        private static decimal DifficultyWeight(string difficulty) => difficulty switch
        {
            "easy"   => 1.2m,
            "medium" => 1.0m,
            "hard"   => 0.7m,
            _        => 1.0m
        };

        // ─── helpers ────────────────────────────────────────────────────────
        private static string Money(decimal amount)
            => amount.ToString("C0", INR);

        private static decimal RoundToNearest(decimal value, int step)
            => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

        private static string MonthLabel(string period)
        {
            if (DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            return period;
        }

        // "May 2026" for full months; "1–15 June 2026" for MTD.
        private static string PeriodLabel(FinancialSnapshot s)
        {
            if (!s.AsOf.HasValue) return MonthLabel(s.Period);
            return $"1–{s.AsOf.Value.Day} {s.AsOf.Value.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        }
    }
}
