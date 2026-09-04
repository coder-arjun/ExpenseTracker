using ExpenseTracker.Models.Domain;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Presentation-only mapping from a category name to a Bootstrap Icons class.
    /// A Category has no icon field and never will as far as this type is concerned —
    /// nothing here is persisted, and an unknown name falls back to a neutral mark.
    ///
    /// Shared so the ledger (Categories/Index) and the live preview on
    /// Categories/Create cannot drift apart: the create page serialises
    /// <see cref="Map"/> straight to the browser.
    /// </summary>
    public static class CategoryIcons
    {
        public const string ExpenseFallback = "bi-tag";
        public const string IncomeFallback = "bi-arrow-down-circle";

        /// <summary>Lower-cased name → icon class. Keys are matched exactly (trimmed, case-insensitive).</summary>
        public static readonly IReadOnlyDictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["food"] = "bi-egg-fried",
                ["groceries"] = "bi-egg-fried",
                ["grocery"] = "bi-egg-fried",
                ["dining"] = "bi-egg-fried",
                ["tea"] = "bi-cup-hot",
                ["coffee"] = "bi-cup-hot",
                ["travel"] = "bi-airplane",
                ["trip"] = "bi-airplane",
                ["flight"] = "bi-airplane",
                ["bills"] = "bi-receipt",
                ["bill"] = "bi-receipt",
                ["utilities"] = "bi-receipt",
                ["rent"] = "bi-receipt",
                ["shopping"] = "bi-bag",
                ["clothes"] = "bi-bag",
                ["clothing"] = "bi-bag",
                ["entertainment"] = "bi-play-circle",
                ["movies"] = "bi-play-circle",
                ["subscription"] = "bi-play-circle",
                ["subscriptions"] = "bi-play-circle",
                ["vehicle"] = "bi-car-front",
                ["fuel"] = "bi-car-front",
                ["petrol"] = "bi-car-front",
                ["car"] = "bi-car-front",
                ["marriage"] = "bi-suit-heart",
                ["wedding"] = "bi-suit-heart",
                ["loan"] = "bi-bank",
                ["emi"] = "bi-bank",
                ["debt"] = "bi-bank",
                ["mortgage"] = "bi-bank",
                ["health"] = "bi-heart-pulse",
                ["medical"] = "bi-heart-pulse",
                ["medicine"] = "bi-heart-pulse",
                ["insurance"] = "bi-heart-pulse",
                ["education"] = "bi-mortarboard",
                ["school"] = "bi-mortarboard",
                ["college"] = "bi-mortarboard",
                ["course"] = "bi-mortarboard",
                ["salary"] = "bi-wallet2",
                ["wages"] = "bi-wallet2",
                ["pay"] = "bi-wallet2",
                ["business"] = "bi-briefcase",
                ["freelance"] = "bi-laptop",
                ["consulting"] = "bi-laptop",
                ["side hustle"] = "bi-laptop",
                ["investment"] = "bi-graph-up-arrow",
                ["investments"] = "bi-graph-up-arrow",
                ["dividend"] = "bi-graph-up-arrow",
                ["dividends"] = "bi-graph-up-arrow",
                ["rental"] = "bi-house",
                ["rental income"] = "bi-house",
                ["house"] = "bi-house",
                ["property"] = "bi-house",
                ["interest"] = "bi-percent",
                ["gift"] = "bi-gift",
                ["gifts"] = "bi-gift",
                ["bonus"] = "bi-star",
                ["reward"] = "bi-star",
                ["rewards"] = "bi-star",
                ["other"] = "bi-three-dots",
                ["misc"] = "bi-three-dots",
                ["miscellaneous"] = "bi-three-dots",
            };

        public static string For(string? name, CategoryType type) =>
            Map.TryGetValue((name ?? "").Trim(), out var icon)
                ? icon
                : type == CategoryType.Income ? IncomeFallback : ExpenseFallback;
    }
}
