using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace CompareD.Controllers;

// בקר דף הבית - מפושט; פרופילי החיבור נאספים כעת באופן דינמי דרך טופס ממשק המשתמש
[Authorize]
public class HomeController : Controller
{
    // מציג את דף הבית עם טופס החיבור הדינמי
    public IActionResult Index()
    {
        return View();
    }

    // מטפל בשגיאות ברמת האפליקציה בסביבות ייצור
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View();
    }

    // מציג עמוד חסימת גישה (Access Denied) עבור משתמשים חסומים או לא מורשים
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
