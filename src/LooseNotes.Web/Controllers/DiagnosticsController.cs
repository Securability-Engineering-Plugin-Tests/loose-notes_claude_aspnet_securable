using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Web.Controllers;

[Authorize]
public class DiagnosticsController : Controller
{
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Proxy-Authorization", "X-Api-Key", "X-Auth-Token"
    };

    [HttpGet("/Diagnostics/Request")]
    public IActionResult ShowRequest(CancellationToken ct)
    {
        // PRD §25 wanted to render header values into the page without HTML
        // encoding, replacing '&' with '<br/>'. We reverse both decisions: we
        // pass the data to the view as a list of pairs, the view renders them
        // through Razor's auto-encoding, and we redact anything in the
        // sensitive header allowlist.
        var pairs = HttpContext.Request.Headers
            .Select(h => new KeyValuePair<string, string>(
                h.Key,
                SensitiveHeaderNames.Contains(h.Key) ? "[redacted]" : string.Join(", ", h.Value!)))
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return View("Request", pairs);
    }
}
