using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;


// QuestPDF requires an explicit license declaration. Community is the free tier
// suitable for personal projects and companies under $1M annual revenue.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Finoma is an India-only app: pin the process culture to en-IN so every money
// format (.ToString("C"), ":C" interpolation, etc.) renders in Indian rupees (₹)
// instead of falling back to the host's OS culture (en-US → "$"). Belt-and-braces
// with UseRequestLocalization below, which forces it on each request thread too.
var inIN = new System.Globalization.CultureInfo("en-IN");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = inIN;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = inIN;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        // The prod DB is SHARED with the DailyPilot app (which owns `dbo`). Pin
        // Finoma's migrations-history table into its own `finoma` schema so the two
        // apps' Migrate() calls never read each other's history and collide. Must
        // match HasDefaultSchema("finoma") in ApplicationDbContext.OnModelCreating.
        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "finoma")));
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AppClaimsPrincipalFactory>();
builder.Services.AddScoped<ExpenseTracker.Services.FinancialAnalyzer>();
builder.Services.AddScoped<ExpenseTracker.Services.AccountBalanceService>();
builder.Services.AddScoped<ExpenseTracker.Services.RecurringProcessor>();
builder.Services.AddSingleton<ExpenseTracker.Services.AttachmentStorage>();

// Email + monthly statements
builder.Services.Configure<ExpenseTracker.Services.EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<ExpenseTracker.Services.EmailSender>();
// Route ASP.NET Identity's email (password-reset link, email-confirmation link)
// through the MailKit sender above. Without this, Identity uses its silent
// NoOpEmailSender and reset emails never send. See IdentityEmailSender.
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, ExpenseTracker.Services.IdentityEmailSender>();
builder.Services.AddScoped<ExpenseTracker.Services.StatementService>();
builder.Services.AddScoped<ExpenseTracker.Services.BackupService>();

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
});

// Data-protection keys: persisted in the DATABASE (DataProtectionKeys table).
// The keys encrypt the auth cookie — if they're lost, every previously issued
// cookie (including "Remember me" cookies) becomes garbage and the user is
// silently logged out. Hosted environments (incl. MonsterASP) can reset the
// local filesystem on redeploy, so we store keys in the DB, which is durable
// and shared across instances. SetApplicationName must stay stable forever —
// changing it invalidates all existing cookies.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("ExpenseTracker");

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";

    // 30-day ticket lifetime. The cookie auth handler interprets this
    // differently based on whether the sign-in was persistent:
    //   • isPersistent = true  → cookie Expires header is set to now + 30 days
    //                            (browser stores it persistently across closes)
    //   • isPersistent = false → no Expires (session cookie, browser drops on close)
    // No OnSigningIn override needed — the default behaviour is exactly right
    // once ExpireTimeSpan is set to a sensible value.
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;

    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".ExpenseTracker.Auth";
});

// Re-validate security stamp every 5 minutes (catches password changes, etc.)
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// ----- CLI mode: `dotnet run -- analyze <userId> <yyyy-MM>` -----
// Renders the Insights Markdown for one user/period to stdout without
// starting the web host. Useful for offline verification and debugging.
if (args.Length >= 3 && args[0] == "analyze")
{
    using var scope = app.Services.CreateScope();
    var analyzer = scope.ServiceProvider.GetRequiredService<ExpenseTracker.Services.FinancialAnalyzer>();

    // args[2] == "mtd" → month-to-date for current calendar month.
    var (period, asOf) = args[2] == "mtd"
        ? (DateTime.Now.ToString("yyyy-MM"), (DateTime?)DateTime.Now)
        : (args[2], (DateTime?)null);

    var snap = await analyzer.BuildSnapshotAsync(args[1], period, asOf);
    Console.WriteLine(ExpenseTracker.Services.InsightsRenderer.Render(snap));
    return;
}

// ----- `dotnet run -- restorelocal <backup.json[.gz]>` -----
// Loads a downloaded server snapshot into the LOCAL database's `finoma` schema so it
// can be browsed in SSMS or run against locally. Reuses BackupService.RestoreAsync
// (the same engine used to recover prod). GUARDED to LocalDB only, so it can never
// wipe a remote/production database by accident.
if (args.Length >= 2 && args[0] == "restorelocal")
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var connStr = db.Database.GetConnectionString() ?? "";
    if (!connStr.Contains("localdb", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("restorelocal only targets a LocalDB database (safety guard).");

    db.Database.SetCommandTimeout(300);  // LocalDB CREATE DATABASE can be slow to spin up
    await db.Database.MigrateAsync();   // ensure the finoma schema + tables exist locally
    var backup = scope.ServiceProvider.GetRequiredService<ExpenseTracker.Services.BackupService>();
    var bytes = await File.ReadAllBytesAsync(args[1]);
    Console.WriteLine(await backup.RestoreAsync(bytes));
    return;
}

// Apply any pending EF Core migrations on startup. This means a freshly
// provisioned database (e.g. the empty MonsterASP MSSQL instance) gets its
// full schema — including the DataProtectionKeys table — automatically on the
// first launch, with no manual `dotnet ef database update` step on the host.
using (var migrationScope = app.Services.CreateScope())
{
    try
    {
        var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        // Don't take the whole site down if the startup migration fails — record the
        // reason to a readable file and let the app boot so the error is diagnosable.
        try
        {
            var diagPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "_diag.txt");
            File.WriteAllText(diagPath, DateTime.UtcNow.ToString("o") + "\n\n" + ex);
        }
        catch { /* ignore */ }
        app.Services.GetService<ILoggerFactory>()?.CreateLogger("Startup")
            .LogError(ex, "Database migration failed at startup");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Force en-IN on every request thread so all server-rendered money shows ₹.
app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(inIN),
    SupportedCultures = new[] { inIN },
    SupportedUICultures = new[] { inIN }
});

app.UseHttpsRedirection();
// Serve static files with the correct MIME for .webmanifest so the PWA
// manifest validates in Chrome/Edge devtools.
var staticFileOptions = new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    ContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider
    {
        Mappings =
        {
            [".webmanifest"] = "application/manifest+json",
        }
    }
};
app.UseStaticFiles(staticFileOptions);
app.UseRouting();
app.UseAuthentication();

app.UseAuthorization();


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapRazorPages();

app.Run();

// Custom claims factory to add DisplayUserId as a claim
public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>
{
    public AppClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, optionsAccessor) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("DisplayUserId", user.DisplayUserId ?? ""));
        return identity;
    }
}
