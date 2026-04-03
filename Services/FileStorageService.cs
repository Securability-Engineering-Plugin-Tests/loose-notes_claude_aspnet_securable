using Microsoft.Extensions.Options;

namespace LooseNotes.Services;

/// <summary>
/// Handles file uploads with FIASSE trust-boundary discipline:
/// - Extension allowlist (ASVS V5.2.2)
/// - Size enforcement (ASVS V5.2.1)
/// - Server-assigned GUID-based filename (Derived Integrity Principle)
/// - Path confinement check (ASVS V5.3)
/// - Files stored OUTSIDE web root — not web-accessible by default
/// </summary>
public sealed class FileStorageService : IFileStorageService
{
    private readonly string _baseDirectory;
    private readonly FileStorageOptions _options;
    private readonly ILogger<FileStorageService> _logger;

    // Magic bytes for common safe types (partial list; extend as needed)
    private static readonly Dictionary<string, byte[]> MagicBytes = new()
    {
        { ".png",  [0x89, 0x50, 0x4E, 0x47] },
        { ".jpg",  [0xFF, 0xD8, 0xFF] },
        { ".jpeg", [0xFF, 0xD8, 0xFF] },
        { ".gif",  [0x47, 0x49, 0x46, 0x38] },
        { ".pdf",  [0x25, 0x50, 0x44, 0x46] },
    };

    public FileStorageService(
        IOptions<FileStorageOptions> options,
        IWebHostEnvironment env,
        ILogger<FileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Resolve base directory OUTSIDE content root so files are not web-accessible
        _baseDirectory = Path.IsPathRooted(_options.AttachmentsPath)
            ? _options.AttachmentsPath
            : Path.GetFullPath(Path.Combine(
                Directory.GetParent(env.ContentRootPath)?.FullName ?? env.ContentRootPath,
                _options.AttachmentsPath));

        Directory.CreateDirectory(_baseDirectory);
        _logger.LogInformation("Attachment storage directory: {Dir}", _baseDirectory);
    }

    public async Task<string> SaveAttachmentAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty");

        if (file.Length > _options.MaxFileSizeBytes)
            throw new ArgumentException(
                $"File exceeds maximum allowed size of {_options.MaxFileSizeBytes} bytes");

        // Canonicalize: extract only the extension, lowercased
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;

        // Validate against allowlist (never block-list)
        if (!_options.AllowedExtensions.Contains(extension))
            throw new ArgumentException($"File extension '{extension}' is not permitted");

        // Magic-byte check for types where we have signatures
        if (MagicBytes.TryGetValue(extension, out var magic))
        {
            using var peek = file.OpenReadStream();
            var header = new byte[magic.Length];
            var read = await peek.ReadAsync(header.AsMemory(0, magic.Length));
            if (read < magic.Length || !header.SequenceEqual(magic))
                throw new ArgumentException("File content does not match declared type");
        }

        // Server-assigned name: GUID + allowed extension (Derived Integrity Principle)
        var storedName = $"{Guid.NewGuid()}{extension}";
        var destPath = Path.Combine(_baseDirectory, storedName);

        using var stream = file.OpenReadStream();
        using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(dest);

        _logger.LogInformation(
            "Attachment saved: stored={StoredName} original={OrigName} size={Size}",
            storedName, file.FileName, file.Length);

        return storedName;
    }

    public string ResolveAttachmentPath(string storedFileName)
    {
        // Canonicalize: strip directory traversal attempts before combining
        var safeName = Path.GetFileName(storedFileName);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new ArgumentException("Invalid attachment filename");

        var resolved = Path.GetFullPath(Path.Combine(_baseDirectory, safeName));

        // Path confinement check (ASVS V5.3)
        if (!resolved.StartsWith(_baseDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            !resolved.Equals(_baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Path traversal attempt blocked: requested={Requested} resolved={Resolved}",
                storedFileName, resolved);
            throw new ArgumentException("Access to the specified path is not permitted");
        }

        return resolved;
    }

    public Task DeleteAttachmentAsync(string storedFileName)
    {
        try
        {
            var path = ResolveAttachmentPath(storedFileName);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Attempted to delete attachment with invalid path: {Name}", storedFileName);
        }
        return Task.CompletedTask;
    }
}
