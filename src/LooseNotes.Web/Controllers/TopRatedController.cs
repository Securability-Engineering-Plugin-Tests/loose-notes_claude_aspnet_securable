using System.Security.Claims;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Web.Controllers;

public class TopRatedController : Controller
{
    private readonly INoteSearchService _search;

    public TopRatedController(INoteSearchService search) => _search = search;

    [HttpGet("/TopRated")]
    public async Task<IActionResult> Index([FromQuery] string? topic, CancellationToken ct)
    {
        var viewerId = User?.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        // Topic value is allowlist-checked inside the service. PRD §17 mandated
        // string concatenation with no validation — rejected.
        var notes = await _search.TopRatedAsync(viewerId, topic, ct);
        return View(notes);
    }
}
