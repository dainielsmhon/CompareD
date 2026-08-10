using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using CompareD.Models;

namespace CompareD.Middleware
{
    // =====================================================================
    // Middleware לתפיסה אוטומטית של שגיאות HTTP (500, 504 וכדומה)
    // ומיקומן בלוג מערכת לקובץ פיזי מקומי
    // =====================================================================
    public class ErrorLoggingMiddleware
    {
        // שמירת הקישור לשלב הבא בצינור העבודה (Pipeline)
        private readonly RequestDelegate _next;

        public ErrorLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // המשך צינור העבודה לשלב הבא
                await _next(context);

                // בדיקה לאחר ביצוע הבקשה: האם הסטטוס מצביע על שגיאה?
                // הערה: נתעלם משגיאת 401 Unauthorized מכיוון שזו חלק מלחיצת היד השגרתית של Windows Authentication
                var statusCode = context.Response.StatusCode;
                if (statusCode >= 400 && statusCode != 401 && SystemSettingsStore.Get().IsErrorLoggingEnabled)
                {
                    // שליפת שם המשתמש מה-Context
                    var username = context.User?.Identity?.Name ?? "Anonymous";
                    var path = context.Request.Path.ToString();

                    // רישום השגיאה לקובץ JSON מקומי עם תרגום עברי אוטומטי
                    SystemLogger.LogError(statusCode, $"HTTP {statusCode}", path, username);
                }
            }
            catch (Exception ex)
            {
                // תפיסת חריגות לא מטופלות שעלולות לגרום לשגיאת 500
                var username = context.User?.Identity?.Name ?? "Anonymous";
                var path = context.Request.Path.ToString();

                // רישום שגיאת 500 עם הודעת החריגה
                SystemLogger.LogError(500, ex.Message, path, username);

                // העברת החריגה לטיפול ה-Exception Handler הרגיל
                throw;
            }
        }
    }
}
