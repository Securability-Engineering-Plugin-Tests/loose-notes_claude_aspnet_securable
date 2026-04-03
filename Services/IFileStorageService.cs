namespace LooseNotes.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Saves an uploaded file after validating extension, size, and MIME prefix.
    /// Returns the server-assigned stored filename (never the client-supplied name).
    /// Throws ArgumentException when validation fails.
    /// </summary>
    Task<string> SaveAttachmentAsync(IFormFile file);

    /// <summary>
    /// Returns the absolute path for a stored attachment filename.
    /// Throws ArgumentException when the resolved path escapes the attachments directory.
    /// </summary>
    string ResolveAttachmentPath(string storedFileName);

    /// <summary>
    /// Deletes a stored attachment by its server-assigned filename.
    /// </summary>
    Task DeleteAttachmentAsync(string storedFileName);
}
