using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using CompareD.Models;

namespace CompareD.Filters
{
    // =====================================================================
    // דורש הרשאת מנהל מערכת, ברמת המחלקה או הפעולה.
    //
    // הסיבה שזו תכונה הצהרתית ולא בדיקה בגוף הפעולה: כשהבדיקה נכתבת בכל פעולה
    // בנפרד, הפעולה שתתווסף בעתיד תהיה חשופה לכל משתמש דומיין מאומת אם מי
    // שיכתוב אותה ישכח את השורה. עם תכונה על המחלקה, פעולה חדשה מוגנת מעצם
    // מקומה, וחשיפה מחייבת הצהרה מפורשת.
    //
    // התכונה היא IAuthorizationFilter ולכן היא רצה לפני שהפעולה מתחילה בכלל,
    // כך שגוף הפעולה אינו מורץ עבור מי שאינו מורשה.
    // =====================================================================
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AdminOnlyAttribute : Attribute, IAuthorizationFilter
    {
        // הודעה אחידה: אינה מגלה אם המשתמש קיים, אם הוא חסום או מה נדרש כדי לעבור
        private const string DeniedMessage = "אין לך הרשאות ניהול לביצוע פעולה זו!";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var httpContext = context.HttpContext;
            var username = httpContext.User?.Identity?.Name;

            var configuration = httpContext.RequestServices.GetService<IConfiguration>();
            if (AdminAuthorization.IsAdmin(configuration, username))
            {
                return;
            }

            // רישום ניסיון הגישה הלא מורשה עם הנתיב שאליו נעשה הניסיון.
            // הנתיב חשוב: הוא מבדיל בין הקלדה מוטעית לבין סריקה של כתובות ניהוליות.
            var attemptedPath = $"{httpContext.Request.Method} {httpContext.Request.Path}";
            AuditLogger.LogAction(
                string.IsNullOrWhiteSpace(username) ? "Unknown" : username,
                "UnauthorizedAdminAccessAttempt",
                $"ניסיון גישה לא מורשה לפעולה ניהולית: {attemptedPath}",
                "Failed");

            // בקשת ניווט רגילה מהדפדפן מקבלת הפניה לעמוד חסימת גישה,
            // בעוד קריאת fetch/AJAX מקבלת JSON - אחרת הלקוח מנסה לפרסר HTML של הפניה.
            context.Result = ExpectsJson(httpContext.Request)
                ? new JsonResult(new { success = false, message = DeniedMessage })
                : new RedirectToActionResult("AccessDenied", "Home", null);
        }

        // זיהוי בקשה שמצפה ל-JSON.
        // ניווט בדפדפן שולח תמיד Accept הכולל text/html; קריאת fetch ללא כותרות
        // שולחת */* בלבד. בנוסף מזוהות במפורש קריאות AJAX לפי הכותרות המקובלות.
        private static bool ExpectsJson(HttpRequest request)
        {
            if (request.Headers.ContainsKey("X-Requested-With") ||
                request.Headers.ContainsKey("X-Csrf-Token"))
            {
                return true;
            }

            var accept = request.Headers["Accept"].ToString();
            return !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
