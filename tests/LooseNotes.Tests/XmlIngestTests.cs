using System.Text;
using LooseNotes.Web.Services;
using Xunit;

namespace LooseNotes.Tests;

public class XmlIngestTests
{
    private readonly IXmlIngestService _x = new XmlIngestService();

    [Fact]
    public void RejectsDoctype()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"v\">]><x>&a;</x>";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Assert.ThrowsAny<Exception>(() => _x.LoadHardenedDocument(ms));
    }

    [Fact]
    public void AcceptsPlainXml()
    {
        var xml = "<?xml version=\"1.0\"?><root><a>1</a></root>";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var doc = _x.LoadHardenedDocument(ms);
        Assert.Equal("root", doc.DocumentElement?.LocalName);
    }
}
