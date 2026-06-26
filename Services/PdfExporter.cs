using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Professional PDF reports via QuestPDF. A4 landscape, brand-coloured header
    /// strip, zebra-striped table, right-aligned amounts, total row, paginated footer.
    /// </summary>
    public static class PdfExporter
    {
        // Brand palette — "The Ledger": crimson stamp on warm paper, ink text.
        private const string Primary    = "#C20E3A"; // crimson
        private const string PrimaryDk  = "#9C0A2E";
        private const string Stripe     = "#F4F1EA"; // warm paper
        private const string Border     = "#E7E2D7"; // rule
        private const string Subtle     = "#8A857A"; // ink-muted
        private const string Text       = "#1A1814"; // ink

        // Font families to match the web app.
        private const string Display = "Space Grotesk"; // titles / brand
        private const string Body    = "Inter";          // body text
        private const string Mono    = "IBM Plex Mono";  // figures / amounts

        // Register the embedded TTFs with QuestPDF once, before any document is built.
        static PdfExporter() => PdfFonts.Ensure();

        public record Column<T>(string Header, Func<T, object?> Get, bool IsCurrency = false, float? RelativeWidth = null);

        public static byte[] Build<T>(
            string title,
            string subtitle,
            IEnumerable<T> rows,
            IReadOnlyList<Column<T>> columns)
        {
            var data = rows.ToList();
            var generatedAt = DateTime.Now;
            var inr = System.Globalization.CultureInfo.GetCultureInfo("en-IN");

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontColor(Text).FontFamily(Body));

                    page.Header().Element(h => ComposeHeader(h, title, subtitle, generatedAt));
                    page.Content().PaddingVertical(10).Element(b => ComposeBody(b, data, columns, inr));
                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        // ─── Header band ─────────────────────────────────────────────────
        private static void ComposeHeader(IContainer container, string title, string subtitle, DateTime generatedAt)
            => container.PaddingBottom(12).BorderBottom(2).BorderColor(Primary).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("FINOMA").FontSize(8).LetterSpacing(2).FontColor(Primary).SemiBold().FontFamily(Display);
                    col.Item().PaddingTop(2).Text(title).FontSize(20).Bold().FontColor(Text).FontFamily(Display);
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        col.Item().PaddingTop(2).Text(subtitle).FontSize(10).FontColor(Subtle);
                });
                row.ConstantItem(150).AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text("Generated").FontSize(7).FontColor(Subtle).LetterSpacing(1);
                    col.Item().AlignRight().Text(generatedAt.ToString("dd MMM yyyy")).FontSize(11).Bold();
                    col.Item().AlignRight().Text(generatedAt.ToString("HH:mm")).FontSize(9).FontColor(Subtle);
                });
            });

        // ─── Table ───────────────────────────────────────────────────────
        private static void ComposeBody<T>(
            IContainer container,
            List<T> data,
            IReadOnlyList<Column<T>> columns,
            System.Globalization.CultureInfo inr)
        {
            if (data.Count == 0)
            {
                container.AlignCenter().AlignMiddle().Text("No records to display.").FontSize(12).FontColor(Subtle);
                return;
            }

            container.Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    foreach (var c in columns)
                        cd.RelativeColumn(c.RelativeWidth ?? 1f);
                });

                // Header row
                table.Header(h =>
                {
                    foreach (var c in columns)
                    {
                        var cell = h.Cell()
                            .Background(Primary)
                            .PaddingVertical(6).PaddingHorizontal(8);
                        if (c.IsCurrency)
                            cell.AlignRight().Text(c.Header).Bold().FontColor(Colors.White).FontSize(9);
                        else
                            cell.Text(c.Header).Bold().FontColor(Colors.White).FontSize(9);
                    }
                });

                // Data rows (zebra-striped).
                // Use string-typed hex so we don't trip Color↔string implicit-conversion ambiguity.
                const string RowWhite = "#FFFFFF";
                var rowIdx = 0;
                foreach (var item in data)
                {
                    var bg = (rowIdx++ % 2 == 0) ? RowWhite : Stripe;
                    foreach (var col in columns)
                    {
                        var raw = col.Get(item);
                        var text = Format(raw, col.IsCurrency, inr);
                        var cell = table.Cell()
                            .Background(bg)
                            .BorderBottom(0.5f).BorderColor(Border)
                            .PaddingVertical(5).PaddingHorizontal(8);
                        if (col.IsCurrency)
                            cell.AlignRight().Text(text).FontSize(9).FontFamily(Mono);
                        else
                            cell.Text(text).FontSize(9);
                    }
                }

                // Totals row (only currency columns get summed)
                var currencyCols = columns
                    .Select((c, i) => (c, i))
                    .Where(t => t.c.IsCurrency).ToList();
                if (currencyCols.Count > 0)
                {
                    var totals = new object?[columns.Count];
                    foreach (var (c, i) in currencyCols)
                    {
                        decimal sum = 0;
                        foreach (var item in data)
                        {
                            var v = c.Get(item);
                            if (v is decimal dec) sum += dec;
                        }
                        totals[i] = sum;
                    }

                    for (int i = 0; i < columns.Count; i++)
                    {
                        var col = columns[i];
                        var cell = table.Cell()
                            .Background(Stripe)
                            .BorderTop(1).BorderColor(Primary)
                            .PaddingVertical(6).PaddingHorizontal(8);
                        if (i == 0)
                            cell.Text("Total").Bold().FontColor(PrimaryDk).FontSize(10);
                        else if (col.IsCurrency)
                            cell.AlignRight().Text(Format(totals[i], true, inr)).Bold().FontColor(PrimaryDk).FontSize(10).FontFamily(Mono);
                        else
                            cell.Text("");
                    }
                }
            });
        }

        // ─── Footer (page number + confidentiality note) ─────────────────
        private static void ComposeFooter(IContainer container)
            => container.BorderTop(0.5f).BorderColor(Border).PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text("Confidential · For internal use only")
                    .FontSize(7).FontColor(Subtle);
                row.RelativeItem().AlignCenter().Text("Finoma — privacy-first personal finance")
                    .FontSize(7).FontColor(Subtle);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Page ").FontSize(7).FontColor(Subtle);
                    t.CurrentPageNumber().FontSize(7).FontColor(Subtle);
                    t.Span(" of ").FontSize(7).FontColor(Subtle);
                    t.TotalPages().FontSize(7).FontColor(Subtle);
                });
            });

        private static string Format(object? value, bool currency, System.Globalization.CultureInfo inr)
        {
            return value switch
            {
                null            => "",
                DateTime dt     => dt.ToString("yyyy-MM-dd"),
                decimal d when currency => d.ToString("C0", inr),
                decimal d       => d.ToString("N2", inr),
                _               => value.ToString() ?? ""
            };
        }
    }
}
