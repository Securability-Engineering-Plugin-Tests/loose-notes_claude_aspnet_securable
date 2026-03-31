namespace LooseNotes.Services;

public interface IFileStorageService
{
    /// <summary>
    /// Stores a validated file. Returns the generated storage filename.
    /// StoredFileName is a UUID-based name — never derived from user input.
    /// </summary>
    Task<string> StoreAsync(IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Returns the absolute path to a stored file by its server-assigned name.
    /// Only accepts names matching the UUID pattern — rejects path traversal attempts.
    /// </summary>
    string GetAbsolutePath(string storedFileName);

    Task DeleteAsync(string storedFileName, CancellationToken ct = default);
}
