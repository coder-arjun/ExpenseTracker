namespace ExpenseTracker.Services
{
    /// <summary>
    /// Registers the embedded brand TTFs (Space Grotesk / Inter / IBM Plex Mono)
    /// with QuestPDF exactly once, so both <see cref="PdfExporter"/> and
    /// <see cref="StatementPdf"/> render with the app's typography.
    /// </summary>
    internal static class PdfFonts
    {
        private static bool _done;
        private static readonly object _gate = new();

        public static void Ensure()
        {
            if (_done) return;
            lock (_gate)
            {
                if (_done) return;
                var asm = typeof(PdfFonts).Assembly;
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                    using var stream = asm.GetManifestResourceStream(res);
                    if (stream != null) QuestPDF.Drawing.FontManager.RegisterFont(stream);
                }
                _done = true;
            }
        }
    }
}
