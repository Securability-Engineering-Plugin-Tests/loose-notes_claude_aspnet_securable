namespace LooseNotes.Web.Options;

public sealed class StorageOptions
{
    public string AttachmentsRoot { get; set; } = "App_Data/attachments";
    public long MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}

public sealed class AuthenticationOptions
{
    public string CookieName { get; set; } = "LooseNotes.Auth";
    public int ExpireMinutes { get; set; } = 60;
    public bool SlidingExpiration { get; set; } = true;
}

public sealed class PasswordRecoveryOptions
{
    public int TokenLifetimeMinutes { get; set; } = 30;
    public int MaxAttemptsPerHour { get; set; } = 5;
}

public sealed class AdminBootstrapOptions
{
    public bool Enabled { get; set; } = true;
    public string Email { get; set; } = "admin@example.local";
    public string Username { get; set; } = "admin";
}
