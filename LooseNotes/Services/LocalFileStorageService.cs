using System.Text.RegularExpressions;

namespace LooseNotes.Services;

/// <summary>
/// Stores files locally under a configured upload directory.
/// Integrity: stored filenames are UUID-based, never derived from user input.
/// Resilience: validates stored filename format before any filesystem operation.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    // Only UUID-format names are accepted for filesystem access
    private static readonly Regex SafeFileNamePattern =
        new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _uploadDirectory;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IConfiguration config,
        ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;
        _uploadDirectory = config["FileStorage:UploadDirectory"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        Directory.CreateDirectory(_uploadDirectory);
    }

    public async Task<string> StoreAsync(IFormFile file, CancellationToken ct = default)
    {
        var storedName = Guid.NewGuid().ToString();
        var destination = Path.Combine(_uploadDirectory, storedName);

        await using var stream = File.Create(destination);
        await file.CopyToAsync(stream, ct);

        _logger.LogInformation("Stored file {StoredName} ({Bytes} bytes)", storedName, file.Length);
        return storedName;
    }

    public string GetAbsolutePath(string storedFileName)
    {
        // Trust boundary: reject any name that doesn't match the UUID pattern
        if (!SafeFileNamePattern.IsMatch(storedFileName))
            throw new ArgumentException("Invalid stored file name format.", nameof(storedFileName));

        return Path.Combine(_uploadDirectory, storedFileName);
    }

    public async Task DeleteAsync(string storedFileName, CancellationToken ct = default)
    {
        if (!SafeFileNamePattern.IsMatch(storedFileName))
        {
            _logger.LogWarning("Rejected delete attempt with invalid filename: {Name}", storedFileName);
            return;
        }

        var path = Path.Combine(_uploadDirectory, storedFileName);
        if (File.Exists(path))
        {
            await Task.Run(() => File.Delete(path), ct);
            _logger.LogInformation("Deleted stored file {StoredName}", storedFileName);
        }
    }
}
