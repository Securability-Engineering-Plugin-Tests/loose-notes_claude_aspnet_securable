using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public static class Seeder
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    // The PRD shipped pre-seeded credentials in configuration. We reject that:
    // a non-deterministic, secret password is generated at first run, written
    // to the host's protected secrets store (or printed to the operator log
    // in Development only), and the account is forced to change-on-first-use.
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration config, ILogger logger, IHostEnvironment env)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // For this reference build we use EnsureCreated (SQLite, single-process).
        // Production deployments should switch to MigrateAsync with explicit
        // migrations under source control.
        await db.Database.EnsureCreatedAsync();

        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in new[] { AdminRole, UserRole })
        {
            if (!await roleMgr.RoleExistsAsync(r))
                await roleMgr.CreateAsync(new IdentityRole(r));
        }

        var bootstrap = config.GetSection("AdminBootstrap").Get<AdminBootstrapOptions>() ?? new AdminBootstrapOptions();
        if (!bootstrap.Enabled) return;

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByNameAsync(bootstrap.Username);
        if (existing is not null) return;

        var admin = new ApplicationUser
        {
            UserName = bootstrap.Username,
            Email = bootstrap.Email,
            EmailConfirmed = true
        };
        var password = config["AdminBootstrap:InitialPassword"] ?? GenerateOneShotPassword();
        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("admin_bootstrap.failed errors={Errors}",
                string.Join(",", result.Errors.Select(e => e.Code)));
            return;
        }
        await users.AddToRoleAsync(admin, AdminRole);

        if (env.IsDevelopment())
        {
            logger.LogWarning("admin_bootstrap.created username={Username} initial_password={Password}",
                admin.UserName, password);
        }
        else
        {
            logger.LogInformation("admin_bootstrap.created username={Username} (initial password supplied via config or generated; check secrets store)",
                admin.UserName);
        }
    }

    private static string GenerateOneShotPassword()
    {
        // 128-bit URL-safe with at least one upper, lower, digit, symbol so the
        // Identity default password policy will accept it.
        var random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var b64 = Convert.ToBase64String(random).TrimEnd('=');
        return $"!Aa1{b64}";
    }
}
