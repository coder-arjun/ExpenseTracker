using System.Globalization;
using System.Text;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Tiny RFC 4180-correct CSV writer. Avoids pulling in a heavier package.
    /// </summary>
    public static class CsvExporter
    {
        public static byte[] Build<T>(IEnumerable<T> rows, IReadOnlyList<(string Header, Func<T, object?> Get)> columns)
        {
            var sb = new StringBuilder();
            // UTF-8 BOM so Excel opens it correctly with non-ASCII (₹, names, etc.)
            sb.Append('﻿');

            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(columns[i].Header));
            }
            sb.Append("\r\n");

            foreach (var row in rows)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var raw = columns[i].Get(row);
                    sb.Append(Escape(Format(raw)));
                }
                sb.Append("\r\n");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        // RFC 4180 §2.6: if a field contains "," | '"' | CR | LF, wrap in quotes and double-up quotes.
        private static string Escape(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string Format(object? value) => value switch
        {
            null            => "",
            DateTime dt     => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal d       => d.ToString("0.00", CultureInfo.InvariantCulture),
            double db       => db.ToString("0.######", CultureInfo.InvariantCulture),
            float f         => f.ToString("0.######", CultureInfo.InvariantCulture),
            bool b          => b ? "true" : "false",
            IFormattable f2 => f2.ToString(null, CultureInfo.InvariantCulture),
            _               => value.ToString() ?? ""
        };
    }
}
