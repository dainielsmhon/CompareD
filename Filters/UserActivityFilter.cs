using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using CompareD.Models;

namespace CompareD.Filters
{
    // מסנן פעולות גלובלי המנטר את פעילות המשתמשים ומבצע חסימות בזמן אמת
    public class UserActivityFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;

            // דילוג על בדיקה במידה והמשתמש אינו מאומת
            if (httpContext.User?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var username = httpContext.User.Identity.Name;
            if (string.IsNullOrEmpty(username))
            {
                return;
            }

            // דילוג על בדיקה במידה וכבר נמצאים בעמוד חסימת גישה למניעת לולאת הפניה אינסופית
            var controllerName = context.RouteData.Values["controller"]?.ToString();
            var actionName = context.RouteData.Values["action"]?.ToString();
            if (string.Equals(controllerName, "Home", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(actionName, "AccessDenied", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // עדכון זמן כניסה אחרון של המשתמש ב-JSON
            UserStore.UpdateLastLogin(username);

            // בדיקת סטטוס חסימה
            var user = UserStore.GetOrCreateUser(username);
            if (user.IsBlocked)
            {
                // רישום ניסיון כניסה של משתמש חסום ב-Audit Log
                AuditLogger.LogAction(username, "BlockedAccessAttempt", $"Blocked user tried to access: {controllerName}/{actionName}");

                // הפנייה לעמוד חסימת גישה
                context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // לא נדרש קוד לאחר הרצת הפעולה
        }
    }
}
