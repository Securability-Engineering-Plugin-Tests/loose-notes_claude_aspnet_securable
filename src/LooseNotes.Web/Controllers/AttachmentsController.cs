using System.Security.Claims;
using System.Text.Encodings.Web;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Web.Controllers;

[Authorize]
public class AttachmentsController : Controller
{
    private readonly IAttachmentStorageService _attachments;

    public AttachmentsController(IAttachmentStorageService attachments) => _attachments = attachments;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool CurrentUserIsAdmin => User.IsInRole(Seeder.AdminRole);

    [HttpGet("Attachments/Download/{id:int}")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var record = await _attachments.OpenAsync(id, CurrentUserId, CurrentUserIsAdmin, ct);
        if (record is null)
            return NotFoundView("That file isn't available.");

        var (stream, contentType, displayName) = record.Value;
        // Force-download with a sanitized filename. Content-Disposition uses
        // RFC 5987 encoding so the original name (which is user-supplied) cannot
        // inject header continuations.
        var safeFallback = "attachment";
        var encoded = HtmlEncoder.Default.Encode(displayName);
        Response.Headers.Append("Content-Disposition",
            $"attachment; filename=\"{safeFallback}\"; filename*=UTF-8''{Uri.EscapeDataString(displayName)}");
        return new FileStreamResult(stream, contentType)
        {
            FileDownloadName = displayName
        };
    }

    private IActionResult NotFoundView(string message)
    {
        // The "file missing" page in PRD §23 mandated reflecting the user-supplied
        // filename without encoding. We do not echo the value at all — the message
        // is fixed text. If a future caller wants to surface a value here, they
        // must use Razor (which HTML-encodes by default) or call HtmlEncoder.
        ViewData["Message"] = message;
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("FileNotFound");
    }
}
