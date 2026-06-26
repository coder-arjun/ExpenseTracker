using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExpenseTracker.Services
{
    // ── Data the statement PDF renders (assembled by StatementService) ──
    public record StatementCatLine(string Category, decimal Amount, decimal Pct, decimal? MoMPct = null);
    public record StatementBudgetLine(string Category, decimal Budget, decimal Actual, decimal VariancePct, bool Over);
    public record StatementDebtLine(string Person, bool TheyOweMe, decimal Outstanding, bool Overdue, DateTime? DueDate);

    public class StatementData
    {
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Period { get; set; } = "";        // yyyy-MM
        public string PeriodLabel { get; set; } = "";    // "June 2026"
        public DateTime GeneratedAt { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetSaved => TotalIncome - TotalExpenses;
        public int SavingsRatePct { get; set; }
        public List<StatementCatLine> IncomeByCategory { get; set; } = new();
        public List<StatementCatLine> ExpenseByCategory { get; set; } = new();
        public List<StatementBudgetLine> Budgets { get; set; } = new();
        public List<string> Highlights { get; set; } = new();

        // ── Outstanding IOUs (running balance, as of GeneratedAt — not period-bound) ──
        public decimal OwedToMe { get; set; }
        public decimal IOwe { get; set; }
        public decimal DebtNet => OwedToMe - IOwe;
        public int DebtOverdueCount { get; set; }
        public List<StatementDebtLine> OpenDebts { get; set; } = new();
        public bool HasDebts => OpenDebts.Count > 0;
    }

    /// <summary>
    /// Renders a one-to-two page monthly statement PDF in "The Ledger" theme:
    /// crimson stamp on warm paper, monospaced money columns, double-rule totals.
    /// </summary>
    public static class StatementPdf
    {
        private const string Primary = "#C20E3A", PrimaryDk = "#9C0A2E", Stripe = "#F4F1EA",
                             Border = "#E7E2D7", Subtle = "#8A857A", Text = "#1A1814",
                             Green = "#15715A", White = "#FFFFFF";
        private const string Display = "Space Grotesk", Body = "Inter", Mono = "IBM Plex Mono";

        public static byte[] Build(StatementData d)
        {
            PdfFonts.Ensure();
            var inr = CultureInfo.GetCultureInfo("en-IN");
            string M(decimal a) => a.ToString("C0", inr);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Text).FontFamily(Body));

                    page.Header().Element(h => Header(h, d));
                    page.Content().PaddingVertical(10).Element(c => Content(c, d, M));
                    page.Footer().Element(Footer);
                });
            }).GeneratePdf();
        }

        private static void Header(IContainer c, StatementData d) =>
            c.BorderBottom(2).BorderColor(Primary).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("FINOMA").FontSize(8).LetterSpacing(2).FontColor(Primary).SemiBold().FontFamily(Display);
                    col.Item().PaddingTop(2).Text("Monthly Statement").FontSize(22).Bold().FontColor(Text).FontFamily(Display);
                    col.Item().Text(d.PeriodLabel).FontSize(11).FontColor(Subtle);
                });
                row.ConstantItem(190).Column(col =>
                {
                    col.Item().AlignRight().Text("Prepared for").FontSize(7).LetterSpacing(1).FontColor(Subtle);
                    col.Item().AlignRight().Text(d.DisplayName).FontSize(11).Bold();
                    if (!string.IsNullOrWhiteSpace(d.Email))
                        col.Item().AlignRight().Text(d.Email).FontSize(8).FontColor(Subtle);
                    col.Item().PaddingTop(4).AlignRight().Text("Generated " + d.GeneratedAt.ToString("dd MMM yyyy, HH:mm")).FontSize(8).FontColor(Subtle);
                });
            });

        private static void Content(IContainer c, StatementData d, Func<decimal, string> M)
        {
            c.Column(col =>
            {
                col.Spacing(16);

                // ── Summary 4-up ──
                col.Item().Row(row =>
                {
                    row.Spacing(8);
                    void Box(string label, string value, string color)
                    {
                        row.RelativeItem().Border(1).BorderColor(Border).Background(Stripe).Padding(10).Column(b =>
                        {
                            b.Item().Text(label).FontSize(7.5f).LetterSpacing(1).FontColor(Subtle);
                            b.Item().PaddingTop(3).Text(value).FontSize(13).SemiBold().FontColor(color).FontFamily(Mono);
                        });
                    }
                    Box("EARNED", M(d.TotalIncome), Green);
                    Box("SPENT", M(d.TotalExpenses), Primary);
                    Box(d.NetSaved >= 0 ? "NET SAVED" : "SHORTFALL", M(Math.Abs(d.NetSaved)), d.NetSaved >= 0 ? Text : Primary);
                    Box("SAVINGS RATE", d.SavingsRatePct + "%", Text);
                });

                // ── Income by category ──
                col.Item().Element(e => Section(e, "Income — by source", t => CatTable(t, d.IncomeByCategory, M, showMoM: false)));

                // ── Expenses by category ──
                col.Item().Element(e => Section(e, "Expenses — by category", t => CatTable(t, d.ExpenseByCategory, M, showMoM: true)));

                // ── Budget performance (only if budgets exist) ──
                if (d.Budgets.Count > 0)
                    col.Item().Element(e => Section(e, "Budget performance", t => BudgetTable(t, d.Budgets, M)));

                // ── Money owed — outstanding IOUs (only if any) ──
                if (d.HasDebts)
                    col.Item().Element(e => Section(e, "Money owed — outstanding IOUs", t => DebtBody(t, d, M)));

                // ── Insights ──
                if (d.Highlights.Count > 0)
                    col.Item().Element(e => Section(e, "Insights", t => Highlights(t, d.Highlights)));
            });
        }

        private static void Section(IContainer c, string title, Action<IContainer> body) =>
            c.Column(s =>
            {
                s.Item().PaddingBottom(4).BorderBottom(1).BorderColor(Border)
                    .Text(title).FontSize(11.5f).SemiBold().FontColor(Text).FontFamily(Display);
                s.Item().PaddingTop(7).Element(body);
            });

        private static void CatTable(IContainer c, List<StatementCatLine> lines, Func<decimal, string> M, bool showMoM)
        {
            if (lines.Count == 0) { c.Text("Nothing recorded for this month.").FontSize(9).FontColor(Subtle).Italic(); return; }

            c.Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(3f);
                    cd.RelativeColumn(2f);
                    cd.RelativeColumn(1.4f);
                    if (showMoM) cd.RelativeColumn(1.7f);
                });
                table.Header(h =>
                {
                    void HC(string t, bool right) { var cell = h.Cell().Background(Primary).PaddingVertical(5).PaddingHorizontal(8); (right ? cell.AlignRight() : cell).Text(t).FontColor(White).Bold().FontSize(8.5f); }
                    HC("Category", false); HC("Amount", true); HC("Share", true); if (showMoM) HC("vs last mo.", true);
                });

                var idx = 0;
                foreach (var ln in lines)
                {
                    var bg = (idx++ % 2 == 0) ? White : Stripe;
                    IContainer Cell() => table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(8);
                    Cell().Text(ln.Category).FontSize(9);
                    Cell().AlignRight().Text(M(ln.Amount)).FontSize(9).FontFamily(Mono);
                    Cell().AlignRight().Text(ln.Pct.ToString("0.0") + "%").FontSize(9).FontFamily(Mono).FontColor(Subtle);
                    if (showMoM)
                    {
                        var cell = Cell().AlignRight();
                        if (ln.MoMPct.HasValue)
                        {
                            var up = ln.MoMPct.Value > 0;
                            cell.Text((up ? "+" : "") + ln.MoMPct.Value.ToString("0.0") + "%").FontSize(8.5f).FontFamily(Mono).FontColor(up ? Primary : Green);
                        }
                        else cell.Text("new").FontSize(8.5f).FontColor(Subtle);
                    }
                }

                // Total row (double-rule feel via crimson top border)
                IContainer Tot() => table.Cell().Background(Stripe).BorderTop(1).BorderColor(Primary).PaddingVertical(5).PaddingHorizontal(8);
                Tot().Text("Total").Bold().FontColor(PrimaryDk).FontSize(9.5f);
                Tot().AlignRight().Text(M(lines.Sum(l => l.Amount))).Bold().FontColor(PrimaryDk).FontSize(9.5f).FontFamily(Mono);
                Tot().Text("");
                if (showMoM) Tot().Text("");
            });
        }

        private static void BudgetTable(IContainer c, List<StatementBudgetLine> b, Func<decimal, string> M)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cd => { cd.RelativeColumn(2.6f); cd.RelativeColumn(1.8f); cd.RelativeColumn(1.8f); cd.RelativeColumn(1.4f); cd.RelativeColumn(1.6f); });
                table.Header(h =>
                {
                    void HC(string t, bool right) { var cell = h.Cell().Background(Primary).PaddingVertical(5).PaddingHorizontal(8); (right ? cell.AlignRight() : cell).Text(t).FontColor(White).Bold().FontSize(8.5f); }
                    HC("Category", false); HC("Budget", true); HC("Spent", true); HC("Variance", true); HC("Status", true);
                });
                var idx = 0;
                foreach (var ln in b)
                {
                    var bg = (idx++ % 2 == 0) ? White : Stripe;
                    IContainer Cell() => table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(8);
                    Cell().Text(ln.Category).FontSize(9);
                    Cell().AlignRight().Text(M(ln.Budget)).FontSize(9).FontFamily(Mono);
                    Cell().AlignRight().Text(M(ln.Actual)).FontSize(9).FontFamily(Mono);
                    Cell().AlignRight().Text(ln.VariancePct.ToString("+0.0;-0.0") + "%").FontSize(9).FontFamily(Mono).FontColor(ln.Over ? Primary : Green);
                    Cell().AlignRight().Text(ln.Over ? "Over" : "On track").FontSize(8.5f).SemiBold().FontColor(ln.Over ? Primary : Green);
                }
            });
        }

        private static void DebtBody(IContainer c, StatementData d, Func<decimal, string> M) =>
            c.Column(col =>
            {
                col.Spacing(9);

                // 3-up summary of the running IOU balance
                col.Item().Row(row =>
                {
                    row.Spacing(8);
                    void Box(string label, string value, string color)
                    {
                        row.RelativeItem().Border(1).BorderColor(Border).Background(Stripe).Padding(10).Column(b =>
                        {
                            b.Item().Text(label).FontSize(7.5f).LetterSpacing(1).FontColor(Subtle);
                            b.Item().PaddingTop(3).Text(value).FontSize(13).SemiBold().FontColor(color).FontFamily(Mono);
                        });
                    }
                    Box("OWED TO YOU", M(d.OwedToMe), Green);
                    Box("YOU OWE", M(d.IOwe), Primary);
                    Box(d.DebtNet >= 0 ? "NET IN YOUR FAVOUR" : "NET YOU OWE", M(Math.Abs(d.DebtNet)), d.DebtNet >= 0 ? Green : Primary);
                });

                col.Item().Element(t => DebtTable(t, d.OpenDebts, M));

                if (d.DebtOverdueCount > 0)
                    col.Item().Text($"{d.DebtOverdueCount} of these are past their due date.")
                        .FontSize(8.5f).FontColor(Primary).Italic();

                col.Item().Text("Outstanding balances as of " + d.GeneratedAt.ToString("dd MMM yyyy") + ".")
                    .FontSize(8).FontColor(Subtle).Italic();
            });

        private static void DebtTable(IContainer c, List<StatementDebtLine> lines, Func<decimal, string> M)
        {
            c.Table(table =>
            {
                table.ColumnsDefinition(cd => { cd.RelativeColumn(2.8f); cd.RelativeColumn(1.6f); cd.RelativeColumn(1.7f); cd.RelativeColumn(1.7f); });
                table.Header(h =>
                {
                    void HC(string t, bool right) { var cell = h.Cell().Background(Primary).PaddingVertical(5).PaddingHorizontal(8); (right ? cell.AlignRight() : cell).Text(t).FontColor(White).Bold().FontSize(8.5f); }
                    HC("Person", false); HC("Direction", false); HC("Outstanding", true); HC("Due", true);
                });

                var idx = 0;
                foreach (var ln in lines)
                {
                    var bg = (idx++ % 2 == 0) ? White : Stripe;
                    IContainer Cell() => table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(8);
                    Cell().Text(ln.Person).FontSize(9);
                    Cell().Text(ln.TheyOweMe ? "Owes you" : "You owe").FontSize(9).FontColor(ln.TheyOweMe ? Green : Primary);
                    Cell().AlignRight().Text(M(ln.Outstanding)).FontSize(9).FontFamily(Mono);
                    var due = Cell().AlignRight();
                    if (ln.Overdue) due.Text("Overdue").FontSize(8.5f).SemiBold().FontColor(Primary);
                    else if (ln.DueDate.HasValue) due.Text(ln.DueDate.Value.ToString("dd MMM yyyy")).FontSize(8.5f).FontColor(Subtle);
                    else due.Text("—").FontSize(8.5f).FontColor(Subtle);
                }

                // Total row — net outstanding owed to the user (owed-to-me minus you-owe across listed rows).
                IContainer Tot() => table.Cell().Background(Stripe).BorderTop(1).BorderColor(Primary).PaddingVertical(5).PaddingHorizontal(8);
                Tot().Text("Owed to you").Bold().FontColor(PrimaryDk).FontSize(9.5f);
                Tot().Text("");
                Tot().AlignRight().Text(M(lines.Where(l => l.TheyOweMe).Sum(l => l.Outstanding))).Bold().FontColor(PrimaryDk).FontSize(9.5f).FontFamily(Mono);
                Tot().Text("");
            });
        }

        private static void Highlights(IContainer c, List<string> items) =>
            c.Column(col =>
            {
                col.Spacing(5);
                foreach (var h in items)
                    col.Item().Row(r =>
                    {
                        r.ConstantItem(14).Text("•").FontColor(Primary).Bold().FontSize(11);
                        r.RelativeItem().Text(h).FontSize(9.5f).FontColor(Text).LineHeight(1.25f);
                    });
            });

        private static void Footer(IContainer c) =>
            c.BorderTop(0.5f).BorderColor(Border).PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text("Confidential · Generated by Finoma").FontSize(7).FontColor(Subtle);
                row.RelativeItem().AlignCenter().Text("finoma.runasp.net").FontSize(7).FontColor(Subtle);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Page ").FontSize(7).FontColor(Subtle);
                    t.CurrentPageNumber().FontSize(7).FontColor(Subtle);
                    t.Span(" of ").FontSize(7).FontColor(Subtle);
                    t.TotalPages().FontSize(7).FontColor(Subtle);
                });
            });
    }
}
