using LooseNotes.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace LooseNotes.Controllers;

public sealed class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ApplicationDbContext db, ILogger<HomeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var publicNotes = await _db.Notes
            .Where(n => n.IsPublic)
            .OrderByDescending(n => n.UpdatedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync();

        return View(publicNotes);
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        // Do not expose RequestId or stack traces in production
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        // Log the actual error; display only the request ID to the user
        _logger.LogError("Unhandled error on request {RequestId}", requestId);
        ViewData["RequestId"] = requestId;
        return View();
    }
}
