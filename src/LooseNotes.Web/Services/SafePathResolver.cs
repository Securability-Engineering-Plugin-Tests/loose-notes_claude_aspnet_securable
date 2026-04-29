using System.Runtime.InteropServices;

namespace LooseNotes.Web.Services;

public interface ISafePathResolver
{
    // Resolves a candidate filename underneath baseDirectory, asserting the
    // resolved path remains inside baseDirectory after canonicalization. Throws
    // PathTraversalException when the assertion fails.
    string ResolveUnder(string baseDirectory, string candidateFileName);

    string EnsureBaseDirectory(string baseDirectory);
}

public sealed class PathTraversalException : Exception
{
    public PathTraversalException(string message) : base(message) { }
}

// Path canonicalization is the single most failure-prone boundary in this
// application (PRD §7, §20, §21, §23). Funnel every filesystem write/read
// through this resolver so the containment check exists in exactly one place.
// FIASSE: Modifiability + Integrity (boundary canonicalization).
public sealed class SafePathResolver : ISafePathResolver
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    public string EnsureBaseDirectory(string baseDirectory)
    {
        var full = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(full);
        return full;
    }

    public string ResolveUnder(string baseDirectory, string candidateFileName)
    {
        if (string.IsNullOrWhiteSpace(candidateFileName))
            throw new PathTraversalException("filename is empty");

        if (candidateFileName.IndexOf('\0') >= 0)
            throw new PathTraversalException("filename contains null byte");

        var leaf = Path.GetFileName(candidateFileName);
        if (string.IsNullOrEmpty(leaf) || leaf != candidateFileName)
            throw new PathTraversalException("filename must not contain path separators");

        if (leaf.IndexOfAny(InvalidNameChars) >= 0)
            throw new PathTraversalException("filename contains invalid characters");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var stem = Path.GetFileNameWithoutExtension(leaf);
            if (WindowsReservedNames.Contains(stem))
                throw new PathTraversalException("filename uses a reserved name");
        }

        var baseFull = Path.GetFullPath(baseDirectory);
        var combined = Path.GetFullPath(Path.Combine(baseFull, leaf));

        var withSep = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(withSep, StringComparison.Ordinal))
            throw new PathTraversalException("resolved path escapes the base directory");

        return combined;
    }
}
