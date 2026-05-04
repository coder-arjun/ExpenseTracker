using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AppClaimsPrincipalFactory>();

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
});

// Data-protection keys: written to disk but cleared on every app start.
// This ensures all previous auth cookies are invalidated on restart (forces re-login),
// while still allowing "Remember me" to persist cookies across browser sessions
// within a single app run.
var keysDir = Path.Combine(builder.Environment.ContentRootPath, "keys");
if (Directory.Exists(keysDir))
{
    foreach (var file in Directory.GetFiles(keysDir))
        File.Delete(file);
}
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("ExpenseTracker");

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.MaxAge = null;  // session cookie by default — cleared on browser close
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".ExpenseTracker.Auth";

    // When "Remember me" is checked, make the cookie persistent (30 days).
    // Default (unchecked) stays a session cookie — browser close = logged out.
    options.Events.OnSigningIn = context =>
    {
        if (context.Properties.IsPersistent)
        {
            context.CookieOptions.MaxAge = TimeSpan.FromDays(30);
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
        }
        return Task.CompletedTask;
    };
});

// Re-validate security stamp every 5 minutes (catches password changes, etc.)
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
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
