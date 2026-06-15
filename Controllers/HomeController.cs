using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

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

    // פעולה לטיפול בשגיאות המערכת בסביבת ייצור (Production)
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        // החזרת תצוגת השגיאה הגנרית והמאובטחת
        return View();
    }
}
