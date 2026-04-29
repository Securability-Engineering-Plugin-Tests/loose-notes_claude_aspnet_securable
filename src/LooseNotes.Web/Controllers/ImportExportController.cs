using System.Security.Claims;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Web.Controllers;

[Authorize]
[Route("ImportExport")]
public class ImportExportController : Controller
{
    private readonly IExportImportService _service;
    private readonly ILogger<ImportExportController> _log;

    public ImportExportController(IExportImportService service, ILogger<ImportExportController> log)
    {
        _service = service; _log = log;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpPost("Export"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Export([FromForm] int[] noteIds, CancellationToken ct)
    {
        var ids = (noteIds ?? Array.Empty<int>()).Distinct().Take(500).ToArray();
        var bytes = await _service.ExportAsync(CurrentUserId, ids, ct);
        return File(bytes, "application/zip", $"loosenotes-export-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.zip");
    }

    [HttpPost("Import"), ValidateAntiForgeryToken]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile archive, CancellationToken ct)
    {
        if (archive is null || archive.Length == 0)
        {
            TempData["ImportError"] = "Please choose a ZIP archive to upload.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            await using var s = archive.OpenReadStream();
            var imported = await _service.ImportAsync(CurrentUserId, s, ct);
            TempData["ImportSuccess"] = $"Imported {imported} note(s).";
        }
        catch (InvalidImportException ex)
        {
            _log.LogWarning("import.rejected reason={Reason}", ex.Message);
            TempData["ImportError"] = "The archive was rejected. " + ex.Message;
        }
        catch (PathTraversalException ex)
        {
            _log.LogWarning("import.rejected.path_traversal reason={Reason}", ex.Message);
            TempData["ImportError"] = "The archive was rejected.";
        }
        return RedirectToAction(nameof(Index));
    }
}
