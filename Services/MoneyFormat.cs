using System.Globalization;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Money rendering for display. <see cref="Full"/> is the exact figure; <see cref="Compact"/>
    /// is the Indian short form (K / L / Cr) used where the number is the hero and precision
    /// would only add noise.
    ///
    /// A compact figure is lossy, so anywhere one is shown the exact value must stay reachable —
    /// pass <see cref="Full"/> as the element's title/aria-label. The views do this.
    /// </summary>
    public static class MoneyFormat
    {
        private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

        /// <summary>
        /// ICU's en-IN currency pattern puts a non-breaking space after the symbol
        /// ("₹ 12,40,000.00"). It reads as a gap in a column of figures, so it is
        /// removed for display. Only the separator goes — digits, grouping and the
        /// minus sign are untouched.
        /// </summary>
        private static string Tighten(string s) =>
            s.Replace(" ", "").Replace(" ", "").Replace("₹ ", "₹");

        /// <summary>Exact amount, two decimals: ₹12,40,000.00</summary>
        public static string Full(decimal amount) => Tighten(amount.ToString("C2", Inr));

        /// <summary>Exact amount, no decimals: ₹12,40,000</summary>
        public static string Exact(decimal amount) => Tighten(amount.ToString("C0", Inr));

        /// <summary>
        /// Indian short form: ₹12.40L, ₹8.79L, ₹50K, ₹18.5K, ₹1.24Cr, ₹850.
        /// Lakhs and crores keep two decimals (₹3.50L reads as money); thousands keep at
        /// most one and drop a trailing zero (₹50K, not ₹50.0K).
        /// </summary>
        public static string Compact(decimal amount)
        {
            var sign = amount < 0 ? "−" : "";
            var a = Math.Abs(amount);

            if (a >= 10_000_000m) return $"{sign}₹{(a / 10_000_000m).ToString("0.00", Inr)}Cr";
            if (a >= 100_000m) return $"{sign}₹{(a / 100_000m).ToString("0.00", Inr)}L";
            if (a >= 1_000m) return $"{sign}₹{(a / 1_000m).ToString("0.#", Inr)}K";
            return $"{sign}₹{a.ToString("0.##", Inr)}";
        }

        /// <summary>Compact, with an explicit leading + or −. Used for variance.</summary>
        public static string CompactSigned(decimal amount) =>
            (amount < 0 ? "" : "+") + Compact(amount);

        /// <summary>Exact, with an explicit leading + or −. Used for variance tooltips.</summary>
        public static string FullSigned(decimal amount) =>
            (amount < 0 ? "−" : "+") + Full(Math.Abs(amount));

        /// <summary>A percentage for display: 70.8% — trailing ".0" dropped.</summary>
        public static string Percent(decimal value) => value.ToString("0.#", Inr) + "%";

        /// <summary>A percentage as an invariant CSS length, e.g. "70.8" for width:70.8%.</summary>
        public static string Css(decimal value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
