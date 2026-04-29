using LooseNotes.Web.Services;
using Xunit;

namespace LooseNotes.Tests;

public class SafePathResolverTests
{
    private readonly ISafePathResolver _r = new SafePathResolver();

    [Fact]
    public void RejectsTraversal()
    {
        var baseDir = _r.EnsureBaseDirectory(Path.Combine(Path.GetTempPath(), "spr-1-" + Guid.NewGuid().ToString("N")));
        Assert.Throws<PathTraversalException>(() => _r.ResolveUnder(baseDir, "../escape.txt"));
    }

    [Fact]
    public void RejectsAbsolutePath()
    {
        var baseDir = _r.EnsureBaseDirectory(Path.Combine(Path.GetTempPath(), "spr-2-" + Guid.NewGuid().ToString("N")));
        Assert.Throws<PathTraversalException>(() => _r.ResolveUnder(baseDir, "/etc/passwd"));
    }

    [Fact]
    public void RejectsNullByte()
    {
        var baseDir = _r.EnsureBaseDirectory(Path.Combine(Path.GetTempPath(), "spr-3-" + Guid.NewGuid().ToString("N")));
        Assert.Throws<PathTraversalException>(() => _r.ResolveUnder(baseDir, "ok.txt\0extra"));
    }

    [Fact]
    public void AcceptsLeafFilename()
    {
        var baseDir = _r.EnsureBaseDirectory(Path.Combine(Path.GetTempPath(), "spr-4-" + Guid.NewGuid().ToString("N")));
        var p = _r.ResolveUnder(baseDir, "report.pdf");
        Assert.StartsWith(baseDir, p);
    }
}
