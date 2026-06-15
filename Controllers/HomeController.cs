using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Linq;

namespace CompareD.Controllers;

// הגדרת בקר הבית הראשי של האפליקציה היורש מבקר הבסיס של ה-MVC
public class HomeController : Controller
{
    private readonly DatabaseProfilesOptions _profiles;

    // בנאי הבקר המקבל את פרופילי החיבור בהזרקה מההגדרות (Dependency Injection)
    public HomeController(IOptions<DatabaseProfilesOptions> profiles)
    {
        _profiles = profiles.Value;
    }

    // פעולה (Action) המציגה את דף הבית הראשי של מערכת ההשוואה
    public IActionResult Index()
    {
        // בניית מודל תצוגה עם שמות הפרופילים מתוך הגדרות השרת
        var viewModel = new HomeViewModel
        {
            SqlProfileNames = _profiles.SqlProfiles.Select(p => p.Name).ToList(),
            OracleProfileNames = _profiles.OracleProfiles.Select(p => p.Name).ToList(),
            SelectedSqlProfile = TempData["SelectedSqlProfile"] as string,
            SelectedOracleProfile = TempData["SelectedOracleProfile"] as string
        };

        // החזרת התצוגה התואמת (Index.cshtml) אל המשתמש עם מודל הנתונים
        return View(viewModel);
    }

    // פעולה לטיפול בשגיאות המערכת בסביבת ייצור (Production)
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        // החזרת תצוגת השגיאה הגנרית והמאובטחת
        return View();
    }
}
