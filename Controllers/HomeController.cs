using Microsoft.AspNetCore.Mvc;

namespace CompareD.Controllers;

// הגדרת בקר הבית הראשי של האפליקציה היורש מבקר הבסיס של ה-MVC
public class HomeController : Controller
{
    // פעולה (Action) המציגה את דף הבית הראשי של מערכת ההשוואה
    public IActionResult Index()
    {
        // החזרת התצוגה התואמת (Index.cshtml) אל המשתמש
        return View();
    }
}
