using ClosedXML.Excel;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Styled XLSX writer. Produces a real Excel file — not a CSV renamed.
    /// Title row, blue header band, alternating row stripes, INR currency
    /// formatting on amount columns, bold totals row.
    /// </summary>
    public static class ExcelExporter
    {
        // Brand palette — keeps the export aesthetic in step with the app.
        private static readonly XLColor Primary    = XLColor.FromHtml("#0066FF");
        private static readonly XLColor HeaderText = XLColor.White;
        private static readonly XLColor Stripe     = XLColor.FromHtml("#F4F7FB");
        private static readonly XLColor Border     = XLColor.FromHtml("#E4E4E7");
        private static readonly XLColor SubtleText = XLColor.FromHtml("#71717A");

        public record Column<T>(string Header, Func<T, object?> Get, bool IsCurrency = false, XLAlignmentHorizontalValues? Align = null);

        public static byte[] Build<T>(
            string title,
            string subtitle,
            IEnumerable<T> rows,
            IReadOnlyList<Column<T>> columns,
            string sheetName = "Data")
        {
            var data = rows.ToList();
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet(sheetName);
            var lastCol = columns.Count;

            // ── Title band ─────────────────────────────────────────────────
            ws.Cell(1, 1).Value = title;
            var titleRange = ws.Range(1, 1, 1, lastCol);
            titleRange.Merge();
            titleRange.Style
                .Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(Primary)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Row(1).Height = 28;

            // ── Subtitle row ───────────────────────────────────────────────
            ws.Cell(2, 1).Value = subtitle;
            var subRange = ws.Range(2, 1, 2, lastCol);
            subRange.Merge();
            subRange.Style
                .Font.SetFontSize(10).Font.SetFontColor(SubtleText)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);

            // Row 3 left empty as breathing room.

            // ── Header row ─────────────────────────────────────────────────
            const int headerRow = 4;
            for (int i = 0; i < columns.Count; i++)
            {
                var c = ws.Cell(headerRow, i + 1);
                c.Value = columns[i].Header;
                c.Style
                    .Font.SetBold().Font.SetFontColor(HeaderText)
                    .Fill.SetBackgroundColor(Primary)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                    .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                    .Border.SetBottomBorderColor(Primary);
            }
            ws.Row(headerRow).Height = 22;
            // Currency-column headers right-align so amounts look anchored beneath.
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].IsCurrency)
                    ws.Cell(headerRow, i + 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }

            // ── Data rows ──────────────────────────────────────────────────
            int row = headerRow + 1;
            foreach (var item in data)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    var cell = ws.Cell(row, i + 1);
                    var raw = columns[i].Get(item);
                    WriteCell(cell, raw, columns[i]);
                }
                // Zebra stripe every other data row
                if ((row - headerRow) % 2 == 0)
                    ws.Range(row, 1, row, lastCol).Style.Fill.SetBackgroundColor(Stripe);
                row++;
            }

            // ── Totals row (only if any currency columns exist) ─────────────
            var currencyColIdxs = columns
                .Select((c, idx) => (col: c, idx))
                .Where(t => t.col.IsCurrency)
                .Select(t => t.idx + 1).ToList();
            if (data.Count > 0 && currencyColIdxs.Count > 0)
            {
                var totalRow = row;
                ws.Cell(totalRow, 1).Value = "Total";
                ws.Cell(totalRow, 1).Style.Font.SetBold();
                foreach (var colIdx in currencyColIdxs)
                {
                    var sum = data.Sum(d =>
                    {
                        var v = columns[colIdx - 1].Get(d);
                        return v is decimal dec ? dec : v is double db ? (decimal)db : 0m;
                    });
                    var c = ws.Cell(totalRow, colIdx);
                    c.Value = sum;
                    c.Style.NumberFormat.Format = "[$₹-409] #,##0.00";
                    c.Style.Font.SetBold();
                    c.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                }
                ws.Range(totalRow, 1, totalRow, lastCol).Style
                    .Border.SetTopBorder(XLBorderStyleValues.Thin)
                    .Border.SetTopBorderColor(Border);
                ws.Row(totalRow).Height = 22;
            }

            // ── Layout polish ──────────────────────────────────────────────
            ws.Columns().AdjustToContents(minWidth: 12, maxWidth: 48);
            ws.SheetView.FreezeRows(headerRow);            // keeps header visible on scroll
            ws.PageSetup.PrintAreas.Add(ws.RangeUsed()!.RangeAddress.ToStringRelative());
            ws.PageSetup.Margins.Top = 0.5;
            ws.PageSetup.Margins.Bottom = 0.5;
            ws.PageSetup.SetPageOrientation(XLPageOrientation.Landscape);
            ws.PageSetup.FitToPages(1, 0);                 // fit width to 1 page

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteCell<T>(IXLCell cell, object? raw, Column<T> col)
        {
            switch (raw)
            {
                case null:
                    cell.Value = "";
                    break;
                case DateTime dt:
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = "yyyy-MM-dd";
                    break;
                case decimal dec when col.IsCurrency:
                    cell.Value = dec;
                    cell.Style.NumberFormat.Format = "[$₹-409] #,##0.00";
                    cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    break;
                case decimal dec:
                    cell.Value = dec;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                    break;
                case bool b:
                    cell.Value = b ? "Yes" : "No";
                    break;
                default:
                    cell.Value = raw.ToString();
                    break;
            }
            if (col.Align.HasValue)
                cell.Style.Alignment.SetHorizontal(col.Align.Value);
        }
    }
}
