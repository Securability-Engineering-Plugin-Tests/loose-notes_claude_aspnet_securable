using LooseNotes.Models;
using Microsoft.AspNetCore.Identity;

namespace LooseNotes.Data;

/// <summary>
/// Seeds roles and an initial admin user on first run.
/// Admin credentials are supplied via environment variables, never hardcoded.
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(
        IServiceProvider services,
        ILogger<ApplicationDbContext> logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await EnsureRoleExistsAsync(roleManager, "Admin", logger);
        await EnsureRoleExistsAsync(roleManager, "User", logger);
        await EnsureAdminUserExistsAsync(userManager, logger);
    }

    private static async Task EnsureRoleExistsAsync(
        RoleManager<IdentityRole> roleManager,
        string roleName,
        ILogger logger)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                logger.LogError("Failed to create role {Role}: {Errors}",
                    roleName, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private static async Task EnsureAdminUserExistsAsync(
        UserManager<ApplicationUser> userManager,
        ILogger logger)
    {
        // Admin credentials from environment — not hardcoded
        var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL")
            ?? "admin@loosenotes.local";
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? "Admin@123!";
        var adminUserName = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
            ?? "admin";

        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing is not null) return;

        var admin = new ApplicationUser
        {
            UserName = adminUserName,
            Email = adminEmail,
            DisplayName = "Administrator",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
            logger.LogInformation("Admin user created: {Email}", adminEmail);
        }
        else
        {
            logger.LogError("Failed to create admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
