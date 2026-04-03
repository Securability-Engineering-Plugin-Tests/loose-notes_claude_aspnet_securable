using LooseNotes.Models;
using Microsoft.AspNetCore.Identity;

namespace LooseNotes.Data;

/// <summary>
/// Seeds the admin role and a single admin account whose credentials are
/// loaded entirely from IConfiguration (never hardcoded in source).
/// SSEM: Confidentiality — no credentials in source; Modifiability — config-driven.
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        await EnsureRoleAsync(roleManager, "Admin", logger);
        await EnsureRoleAsync(roleManager, "User", logger);

        var seedAccounts = config.GetSection("SeedAccounts")
                                 .Get<SeedAccountOptions[]>() ?? [];

        foreach (var account in seedAccounts)
        {
            if (string.IsNullOrWhiteSpace(account.UserName) ||
                string.IsNullOrWhiteSpace(account.Email) ||
                string.IsNullOrWhiteSpace(account.Password))
            {
                logger.LogWarning("Skipping malformed seed account entry");
                continue;
            }

            await EnsureUserAsync(userManager, account, logger);
        }
    }

    private static async Task EnsureRoleAsync(
        RoleManager<IdentityRole> roleManager,
        string roleName,
        ILogger logger)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
                logger.LogError("Failed to create role {Role}: {Errors}", roleName,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        SeedAccountOptions account,
        ILogger logger)
    {
        var existing = await userManager.FindByNameAsync(account.UserName);
        if (existing != null) return;

        var user = new ApplicationUser
        {
            UserName = account.UserName,
            Email = account.Email,
            DisplayName = account.DisplayName ?? account.UserName,
            EmailConfirmed = true
        };

        // Identity hashes the password using PBKDF2-SHA256 — no Base64
        var result = await userManager.CreateAsync(user, account.Password);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to create seed user {User}: {Errors}",
                account.UserName,
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        if (!string.IsNullOrWhiteSpace(account.Role))
        {
            await userManager.AddToRoleAsync(user, account.Role);
        }

        logger.LogInformation("Seed user {User} created", account.UserName);
    }
}

public sealed class SeedAccountOptions
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
}
