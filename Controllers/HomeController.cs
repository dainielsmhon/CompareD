using Microsoft.AspNetCore.Mvc;

namespace CompareD.Controllers;

// Home page controller — simplified; connection profiles are now collected dynamically via the UI form
public class HomeController : Controller
{
    // Displays the home page with the dynamic connection form
    public IActionResult Index()
    {
        return View();
    }

    // Handles application-level errors in production environments
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View();
    }
}
