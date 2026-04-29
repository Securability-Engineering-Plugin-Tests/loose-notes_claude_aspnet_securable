using System.Security.Cryptography;
using LooseNotes.Web.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LooseNotes.Tests;

public class EncryptionTests
{
    private static IConfiguration BuildConfig()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:KeyBase64"] = key })
            .Build();
    }

    [Fact]
    public void RoundTrip()
    {
        using var svc = new EncryptionService(BuildConfig());
        var ct1 = svc.EncryptToBase64("hello");
        var ct2 = svc.EncryptToBase64("hello");
        Assert.NotEqual(ct1, ct2); // fresh nonce per call
        Assert.Equal("hello", svc.DecryptFromBase64(ct1));
    }

    [Fact]
    public void TamperDetected()
    {
        using var svc = new EncryptionService(BuildConfig());
        var ct = svc.EncryptToBase64("payload");
        var bytes = Convert.FromBase64String(ct);
        bytes[^1] ^= 0x01;
        var tampered = Convert.ToBase64String(bytes);
        // AES-GCM throws AuthenticationTagMismatchException, a CryptographicException subtype.
        Assert.ThrowsAny<CryptographicException>(() => svc.DecryptFromBase64(tampered));
    }

    [Fact]
    public void MissingKeyThrowsAtStartup()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => new EncryptionService(emptyConfig));
    }
}
