using System.Threading.RateLimiting;
using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Options;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Options ---------------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<LooseNotes.Web.Options.AuthenticationOptions>(builder.Configuration.GetSection("Authentication"));
builder.Services.Configure<PasswordRecoveryOptions>(builder.Configuration.GetSection("PasswordRecovery"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LooseNotes.Web.Options.AuthenticationOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PasswordRecoveryOptions>>().Value);

// ---------- Data ------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite(builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=loosenotes.db"));

// ---------- Identity --------------------------------------------------------
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 12;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var authOpts = builder.Configuration.GetSection("Authentication").Get<LooseNotes.Web.Options.AuthenticationOptions>()
    ?? new LooseNotes.Web.Options.AuthenticationOptions();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = authOpts.CookieName;
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(authOpts.ExpireMinutes);
    o.SlidingExpiration = authOpts.SlidingExpiration;
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
});

// Anti-forgery is enabled globally; controllers also annotate POST actions so
// the requirement is visible at the call site.
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.HeaderName = "X-XSRF-TOKEN";
});

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly", p => p.RequireRole(Seeder.AdminRole));
});

// ---------- Application services -------------------------------------------
builder.Services.AddSingleton<IHtmlSanitizationService, HtmlSanitizationService>();
builder.Services.AddSingleton<ISafePathResolver, SafePathResolver>();
builder.Services.AddSingleton<ITokenHasher, TokenHasher>();
// Encryption key requirement is intentionally hard — fail fast if missing.
builder.Services.AddSingleton<IEncryptionService>(sp =>
    new EncryptionService(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
builder.Services.AddScoped<INoteAuthorizationService, NoteAuthorizationService>();
builder.Services.AddScoped<IShareTokenService, ShareTokenService>();
builder.Services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
builder.Services.AddScoped<IExportImportService, ExportImportService>();
builder.Services.AddSingleton<IXmlIngestService, XmlIngestService>();
builder.Services.AddScoped<INoteSearchService, NoteSearchService>();
builder.Services.AddScoped<IEmailAutocompleteService, EmailAutocompleteService>();
builder.Services.AddScoped<ISecurityAnswerHasher, SecurityAnswerHasher>();

// ---------- Rate limiting ---------------------------------------------------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("login", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

    o.AddPolicy("autocomplete", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

    o.AddPolicy("recovery", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0
            }));
});

// ---------- Form size limits -----------------------------------------------
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10 MB upload cap
});
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = 60 * 1024 * 1024; // 60 MB ZIP import cap
});

// ---------- MVC -------------------------------------------------------------
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(180);
    o.IncludeSubDomains = true;
});

var app = builder.Build();

// ---------- Pipeline --------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Defense-in-depth response headers. Generic but deliberate — values are
// chosen to keep the application functional for first-time users.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.Append("X-Content-Type-Options", "nosniff");
    h.Append("X-Frame-Options", "DENY");
    h.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    h.Append("Content-Security-Policy",
        "default-src 'self'; img-src 'self' data:; script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'");
    h.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ---------- First-run seed --------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await Seeder.SeedAsync(app.Services, app.Configuration, logger, app.Environment);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "seeder.failed startup will continue but the database may be in an unexpected state");
    }
}

app.Run();

public partial class Program { }
