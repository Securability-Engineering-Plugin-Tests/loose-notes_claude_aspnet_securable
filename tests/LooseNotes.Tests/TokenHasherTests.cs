using LooseNotes.Web.Services;
using Xunit;

namespace LooseNotes.Tests;

public class TokenHasherTests
{
    private readonly ITokenHasher _h = new TokenHasher();

    [Fact]
    public void NewTokenIsUrlSafe()
    {
        var t = _h.NewUrlSafeToken();
        Assert.Matches("^[A-Za-z0-9_-]+$", t);
        Assert.True(t.Length >= 32);
    }

    [Fact]
    public void HashIsStable()
    {
        var t = _h.NewUrlSafeToken();
        Assert.Equal(_h.Hash(t), _h.Hash(t));
    }

    [Fact]
    public void VerifyDistinguishesValues()
    {
        var t = _h.NewUrlSafeToken();
        var hash = _h.Hash(t);
        Assert.True(_h.Verify(t, hash));
        Assert.False(_h.Verify(t + "x", hash));
    }
}
