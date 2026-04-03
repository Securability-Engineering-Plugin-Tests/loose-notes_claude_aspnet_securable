namespace LooseNotes.Services;

public sealed class FileStorageOptions
{
    // Base directory for attachments — resolved to absolute path at startup
    public string AttachmentsPath { get; set; } = "attachments";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".txt", ".md", ".png", ".jpg", ".jpeg", ".gif", ".csv", ".docx", ".xlsx"
    ];
}
