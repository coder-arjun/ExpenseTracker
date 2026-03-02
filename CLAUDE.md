# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
dotnet build                    # Build the project
dotnet run                      # Run the app (http://localhost:5238, https://localhost:7277)
dotnet ef migrations add <Name> # Add a new EF Core migration
dotnet ef database update       # Apply migrations to the database
```

No test project exists. No linter or formatter is configured.

## Architecture

ASP.NET Core 10 MVC app (single project, `.slnx` format) with ASP.NET Identity authentication, Entity Framework Core, and SQL Server LocalDB (`ExpenseTrackerDb`).

### Key layers

- **Models/Domain/** — Entity classes: `ApplicationUser` (extends `IdentityUser`), `Expense`, `Income`, `Saving`, `ExpenseCategory` (enum with values Food=1 through Other=6).
- **Models/ViewModel/** — View models for controllers (e.g., `DashboardViewModel`).
- **Data/ApplicationDbContext** — EF Core context inheriting `IdentityDbContext<IdentityUser>`.
- **Controllers/** — `DashboardController` (monthly totals/metrics with Month/Year/DateRange filters), `ExpensesController`, `IncomesController`, `SavingsController` (all CRUD), `HomeController` (public pages).
- **Views/** — Razor views with Bootstrap 5 layout. Identity UI pages scaffolded under `Areas/Identity/Pages/Account/`.
- **Services/** — Exists but is currently empty; controllers use DbContext directly.

### Data model

All user-owned entities have a `UserId` FK and a year-month string field (`"yyyy-MM"`) with composite indexes on `(UserId, <month-field>)`.

**Important naming inconsistency:** `Expense` uses a field called `Month` while `Income` and `Saving` use `YearMonth`. Both store the same `"yyyy-MM"` format.

Relationships: `Expense` → `ExpenseCategory` (enum), `Expense`/`Income`/`Saving` → `ApplicationUser`.

### Auth & routing

- **Global auth filter** in `Program.cs` requires authentication on all MVC controllers by default. `HomeController` is the exception (no `[Authorize]`).
- Default route: `{controller=Dashboard}/{action=Index}/{id?}` — Dashboard is the landing page for authenticated users.
- Cookie paths configured to `/Identity/Account/Login` and `/Identity/Account/Logout`.
- `RequireConfirmedAccount = false`.

### Key code patterns

- **User-scoped queries**: Always filter by `UserId` via `UserManager.GetUserId(User)`.
- **Async throughout**: All controller actions return `Task<IActionResult>` and use `await` with EF async methods.
- **Null-safe aggregates**: `SumAsync(x => (decimal?)x.Amount) ?? 0` pattern for totals.
- **Anti-forgery tokens**: All POST actions use `[ValidateAntiForgeryToken]`.
- **Nullable annotations enabled**: `required` keyword on mandatory fields, `?` on optional/FK fields.
- **TempData flash messages**: Controllers set `TempData["Success"]`, `TempData["Error"]`, etc. Rendered by `_AlertPartial.cshtml` in the layout.
- **ClearServerFieldErrors()**: CRUD controllers call this private helper to remove `UserId`/`User` from `ModelState` before checking `ModelState.IsValid`, since those fields are set server-side.
- **Ephemeral data protection**: `UseEphemeralDataProtectionProvider()` in `Program.cs` means auth cookies are invalidated on app restart.
