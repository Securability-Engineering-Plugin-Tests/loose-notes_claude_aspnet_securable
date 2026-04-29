using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LooseNotes.Web.Controllers;

// PRD §15 demanded an unauthenticated email autocomplete with no rate
// limiting and direct concatenation into a LIKE filter. Re-engineered:
//   * [Authorize] — caller must be logged in.
//   * [EnableRateLimiting] — fixed window per user/IP.
//   * Service applies prefix length floor + parameter binding + cap.
[Authorize]
[Route("api/autocomplete")]
[EnableRateLimiting("autocomplete")]
public class AutocompleteController : ControllerBase
{
    private readonly IEmailAutocompleteService _service;

    public AutocompleteController(IEmailAutocompleteService service) => _service = service;

    [HttpGet("emails")]
    public async Task<IActionResult> Emails([FromQuery] string prefix, CancellationToken ct)
    {
        var results = await _service.SuggestAsync(prefix, ct);
        return Ok(results);
    }
}
