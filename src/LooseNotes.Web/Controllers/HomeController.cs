using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Web.Controllers;

public class HomeController : Controller
{
    private readonly INoteSearchService _search;

    public HomeController(INoteSearchService search) => _search = search;

    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var viewerId = User?.Identity?.IsAuthenticated == true
            ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            : null;
        var notes = await _search.SearchAsync(q, viewerId, ct);
        ViewData["Query"] = q;
        return View(notes);
    }

    public IActionResult Privacy() => View();

    [Route("/Home/Error")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error() => View();
}
