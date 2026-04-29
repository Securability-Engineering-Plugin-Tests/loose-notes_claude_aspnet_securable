using System.Text;
using System.Text.Encodings.Web;

namespace LooseNotes.Web.Services;

public interface IHtmlSanitizationService
{
    // Returns a safe HTML fragment derived from user-supplied note content.
    // The implementation is intentionally restrictive: every byte that could be
    // interpreted as markup is HTML-encoded, and only line breaks are
    // re-introduced as <br/>. Notes therefore behave as plain text with
    // preserved line breaks, eliminating an entire class of XSS issues and
    // removing the need for an HTML allowlist parser.
    string SanitizeRichText(string? input);
}

// FIASSE Dependency Hygiene (S4.6): we previously used a third-party HTML
// sanitizer here. Removing the dependency in favor of the built-in
// HtmlEncoder shrinks the attack surface, removes a CVE-flagged package, and
// lets us reason about the sanitization contract with one well-known
// primitive. The trade-off is that rich-text formatting (bold, lists, etc.)
// is no longer rendered — see README "Securability Decisions".
public sealed class HtmlSanitizationService : IHtmlSanitizationService
{
    private const int MaxLength = 50_000;

    public string SanitizeRichText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var clipped = input.Length > MaxLength ? input.Substring(0, MaxLength) : input;
        var normalized = clipped.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var enc = HtmlEncoder.Default;
        var sb = new StringBuilder(clipped.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append("<br/>");
            sb.Append(enc.Encode(lines[i]));
        }
        return sb.ToString();
    }
}
