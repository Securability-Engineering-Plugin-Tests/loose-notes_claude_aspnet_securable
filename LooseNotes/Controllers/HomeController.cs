using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Controllers;

public class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Notes");

        return View();
    }
}
