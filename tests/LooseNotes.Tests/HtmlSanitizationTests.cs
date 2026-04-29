using LooseNotes.Web.Services;
using Xunit;

namespace LooseNotes.Tests;

public class HtmlSanitizationTests
{
    private readonly IHtmlSanitizationService _s = new HtmlSanitizationService();

    [Fact]
    public void EncodesScriptTagBodyAsText()
    {
        var output = _s.SanitizeRichText("<p>hi</p><script>alert(1)</script>");
        Assert.DoesNotContain("<script", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<p>", output);
        Assert.Contains("&lt;p&gt;", output);
    }

    [Fact]
    public void EncodesEventHandlersAsText()
    {
        var output = _s.SanitizeRichText("<a href='https://x' onclick='steal()'>click</a>");
        // No live tag survives — every angle bracket is encoded.
        Assert.DoesNotContain("<a ", output);
        Assert.Contains("&lt;a", output);
    }

    [Fact]
    public void EncodesJavascriptScheme()
    {
        var output = _s.SanitizeRichText("<a href='javascript:alert(1)'>x</a>");
        // The output is plain text — the angle brackets are encoded so no
        // active anchor tag ever reaches the renderer.
        Assert.DoesNotContain("<a ", output);
        Assert.Contains("&lt;", output);
    }

    [Fact]
    public void NewlinesBecomeBrTags()
    {
        var output = _s.SanitizeRichText("line1\nline2\r\nline3");
        Assert.Equal("line1<br/>line2<br/>line3", output);
    }

    [Fact]
    public void EmptyInputIsEmpty()
    {
        Assert.Equal(string.Empty, _s.SanitizeRichText(null));
        Assert.Equal(string.Empty, _s.SanitizeRichText(""));
    }
}
