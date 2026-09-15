using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace CompareD.Controllers;

// בקר דף הבית - מפושט; פרופילי החיבור נאספים כעת באופן דינמי דרך טופס ממשק המשתמש
[Authorize]
public class HomeController : Controller
{
    // מציג את דף הבית עם טופס החיבור הדינמי
    [HttpGet]
    public IActionResult Index()
    {
        // ניקוי פרטי חיבור שנותרו מזרימת עבודה קודמת, כשהמשתמש מתחיל מחדש מדף הבית.
        // שמות המפתחות כאן היו שמות legacy שאף קוד לא כתב אליהם יותר
        // (SqlConnectionString / OracleConnectionString), ולכן הניקוי לא ניקה דבר
        // ופרטי החיבור המוצפנים נשארו בסשן עד לתפוגת חצי השעה.
        HttpContext.Session.Remove("SourceConnectionString");
        HttpContext.Session.Remove("TargetConnectionString");
        HttpContext.Session.Remove("SourceProvider");
        HttpContext.Session.Remove("TargetProvider");
        HttpContext.Session.Remove("SelectedSourceTable");
        HttpContext.Session.Remove("SelectedTargetTable");
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
