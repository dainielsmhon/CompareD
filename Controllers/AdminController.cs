using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.DirectoryServices.AccountManagement;
using CompareD.Models;

namespace CompareD.Controllers
{
    // =====================================================================
    // בקר ניהול מורחב - מוגן בהרשאות Admin
    // כולל: Audit Log, System Logs, Analytics Dashboard, Health Monitor, Settings
    // =====================================================================
    [Authorize]
    [CompareD.Filters.AdminOnly]
    public class AdminController : Controller
    {
        // =====================================================================
        // Feature 1+2+5+6+7: לוח בקרה ראשי משולב עם Analytics, Health, Settings
        // =====================================================================
        [HttpGet]
        public IActionResult Index()
        {
            // טעינת כל הנתונים הדרושים ל-ViewModel המשולב
            var currentUsername = User.Identity?.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(currentUsername))
                UserStore.GetOrCreateUser(currentUsername);

            var allAuditEntries = AuditLogStore.GetAll();

            // הצלחה היא Status שהוא בדיוק "Success"; כל השאר נספר ככשל.
            //
            // קודם נספרו כאן "Success" ו-"Failed" בהשוואה מדויקת, בעוד שהחישובים
            // ב-AuditLogStore סופרים "כל מה שאינו Success" ככשל. סטטוס שאינו אחד
            // מהשניים היה נספר כאן כלא-כלום וכשם ככשל, וכרטיסי המדד היו סותרים
            // את הטבלאות. שתי השיטות זהות כעת, וגם מובטח ש-
            // successCount + failedCount שווה בדיוק לסך הפעולות.
            var successCount = allAuditEntries.Count(e =>
                string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase));
            var failedCount = allAuditEntries.Count - successCount;

            // בניית ViewModel עשיר לדאשבורד הניהול
            var viewModel = new AdminDashboardViewModel
            {
                // כל החישובים מקבלים את allAuditEntries שנטען למעלה, ולא שולפים
                // בעצמם. קודם כל חישוב קרא את קובץ הביקורת מהדיסק בנפרד, ולכן
                // טעינת הדף פרסרה את אותו JSON שמונה פעמים.
                SuccessRate = AuditLogStore.GetSuccessRate(allAuditEntries),
                TotalActions = allAuditEntries.Count,
                SuccessCount = successCount,
                FailedCount = failedCount,
                RecentActiveUsers = AuditLogStore.GetRecentActiveUsers(10, allAuditEntries),
                AvgProcessingTimeMs = HealthMonitor.GetAverageProcessingMs(),
                HealthSamplesCount = HealthMonitor.GetAll().Count,
                Users = UserStore.GetUsers(),
                Settings = SystemSettingsStore.Get(),
                AppVersion = AppVersion.FullVersion,

                // פירוט לפי משתמש ולפי שלב כשל. אחוז ההצלחה הגלובלי אינו
                // מבדיל בין עובד שמריץ בהצלחה לעובד שנתקע שוב ושוב באותו שלב,
                // וזה מה שצריך לדעת כשהמערכת נמסרת לעובדים.
                PerUserStats = AuditLogStore.GetPerUserStats(allAuditEntries),
                FailureBreakdown = AuditLogStore.GetFailureBreakdown(allAuditEntries),

                // פסק הדין, מגמת השבוע וגרף 14 הימים - כולם נגזרים מלוג
                // הביקורת הקיים ואינם דורשים איסוף נתונים נוסף.
                Verdict = AuditLogStore.GetVerdict(allAuditEntries),
                DailyActivity = AuditLogStore.GetDailyActivity(14, allAuditEntries),
                Trend = AuditLogStore.GetWeeklyTrend(allAuditEntries),

                // מפת החום, המשפך ורשומות הפירוט - כולם נגזרים מאותה
                // רשימה שנטענה פעם אחת למעלה, בלי קריאה נוספת מהדיסק.
                YearActivity = AuditLogStore.GetYearActivity(26, allAuditEntries),
                Funnel = AuditLogStore.GetFunnel(allAuditEntries),
                DrillEntries = AuditLogStore.GetDrillEntries(26 * 7, allAuditEntries)
            };

            return View(viewModel);
        }

        // =====================================================================
        // הערה: פעולות AuditLog ו-SystemLogs הוסרו.
        // הן החזירו View שלא היה קיים בתיקיית Views/Admin ולכן כל גישה אליהן
        // הסתיימה בשגיאת 500. שני הלוגים מוצגים במלואם, כולל חיפוש,
        // בטאבים "Audit Log" ו-"System Logs" שבתוך לוח הבקרה (Index).
        // =====================================================================

        // =====================================================================
        // Feature 7: שמירת הגדרות מערכת דינמיות
        // =====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSettings(SystemSettings settings)
        {
            var currentUser = User.Identity?.Name ?? "Unknown";
            settings.LastUpdatedBy = currentUser;

            var saved = SystemSettingsStore.Save(settings);

            if (saved)
            {
                // רישום שינוי הגדרות ב-Audit Log
                AuditLogger.LogAction(currentUser, "UpdatedSystemSettings",
                    $"עדכן הגדרות מערכת: MaxFileUploadMb={settings.MaxFileUploadMb}, AuditLog={settings.IsAuditLoggingEnabled}");
                return Json(new { success = true, message = "ההגדרות נשמרו בהצלחה!" });
            }

            return Json(new { success = false, message = "שגיאה בשמירת ההגדרות. נסה שנית." });
        }

        // =====================================================================
        // שינוי סטטוס חסימת משתמש (נקרא באמצעות AJAX מהממשק)
        // =====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleStatus(string username, bool isBlocked)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { success = false, message = "שם משתמש לא תקין!" });

            var currentUsername = User.Identity?.Name ?? string.Empty;

            // הגנה: מניעת חסימה עצמית של מנהל המערכת המחובר
            if (string.Equals(username, currentUsername, StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "שגיאה: אינך יכול לחסום את עצמך מהמערכת!" });

            // ביצוע שינוי הסטטוס
            if (!UserStore.ToggleBlockedStatus(username, isBlocked))
                return Json(new { success = false, message = "המשתמש לא נמצא במערכת." });

            // רישום הפעולה ב-Audit Log
            var logActionType = isBlocked ? "BlockedUser" : "UnblockedUser";
            AuditLogger.LogAction(currentUsername, logActionType,
                $"שינה סטטוס של משתמש '{username}' לחסום={isBlocked}");

            return Json(new { success = true });
        }

        // =====================================================================
        // Feature 8: חיפוש משתמשים ב-Active Directory 
        // =====================================================================
        [HttpGet]
        [SupportedOSPlatform("windows")]
        public IActionResult SearchAD(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return Json(new { success = true, results = new List<object>() });

            var results = new List<object>();
            try
            {
                // ניסיון התחברות לדומיין הנוכחי
                using (var context = new PrincipalContext(ContextType.Domain))
                using (var searcher = new UserPrincipal(context))
                {
                    searcher.SamAccountName = $"*{query}*";
                    // אפשר גם לחפש לפי שם מלא: searcher.DisplayName = $"*{query}*";
                    
                    using (var search = new PrincipalSearcher(searcher))
                    {
                        foreach (var result in search.FindAll().Take(10))
                        {
                            var user = result as UserPrincipal;
                            if (user != null)
                            {
                                results.Add(new {
                                    displayName = user.DisplayName ?? user.SamAccountName,
                                    samAccountName = user.SamAccountName,
                                    email = user.EmailAddress ?? "",
                                    domain = context.Name
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // במקרה של שגיאת התחברות ל-AD (למשל סביבה מנותקת)
                SystemLogger.LogError(500, $"שגיאת AD: {ex.Message}", "SearchAD", User.Identity?.Name ?? "Unknown");
                return Json(new { success = false, message = "לא ניתן להתחבר לשרת ה-Active Directory כעת." });
            }

            return Json(new { success = true, results });
        }

        // =====================================================================
        // הוספת משתמש שאושר מראש (Pre-Approved User) - AD או ידני
        // =====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddUser(string username, string fullName, string email, string authType = "Active Directory")
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { success = false, message = "שם משתמש חובה." });

            if (string.IsNullOrWhiteSpace(authType)) 
                authType = "Active Directory";

            bool added = UserStore.AddApprovedUser(username, fullName ?? string.Empty, email ?? string.Empty, authType);
            
            if (added)
            {
                AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "AddApprovedUser",
                    $"הוסיף מראש משתמש מאושר ({authType}): {username}");
                return Json(new { success = true, message = "המשתמש נוסף ואושר בהצלחה!" });
            }

            return Json(new { success = false, message = "שגיאה בהוספת המשתמש." });
        }
    }
}
