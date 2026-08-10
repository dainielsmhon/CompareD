using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using CompareD.Models;

namespace CompareD.Controllers
{
    // =====================================================================
    // בקר ניהול מורחב - מוגן בהרשאות Admin
    // כולל: Audit Log, System Logs, Analytics Dashboard, Health Monitor, Settings
    // =====================================================================
    [Authorize]
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // פונקציית עזר לבדיקה האם המשתמש הנוכחי מוגדר כמנהל מורשה
        private bool IsUserAuthorized()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return false;

            var authorizedUsers = _configuration.GetSection("AdminSettings:AuthorizedUsers").Get<List<string>>();
            if (authorizedUsers == null || authorizedUsers.Count == 0) return false;

            return authorizedUsers.Any(u => string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
        }

        // =====================================================================
        // Feature 1+2+5+6+7: לוח בקרה ראשי משולב עם Analytics, Health, Settings
        // =====================================================================
        [HttpGet]
        public IActionResult Index()
        {
            if (!IsUserAuthorized())
            {
                // רישום ניסיון גישה לא מורשה ב-Audit Log עם סטטוס Failed
                AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "UnauthorizedAdminAccessAttempt",
                    "ניסיון גישה לא מורשה לפאנל הניהול", "Failed");
                return RedirectToAction("AccessDenied", "Home");
            }

            // טעינת כל הנתונים הדרושים ל-ViewModel המשולב
            var currentUsername = User.Identity?.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(currentUsername))
                UserStore.GetOrCreateUser(currentUsername);

            var allAuditEntries = AuditLogStore.GetAll();
            var successCount = allAuditEntries.Count(e => e.Status == "Success");
            var failedCount = allAuditEntries.Count(e => e.Status == "Failed");

            // בניית ViewModel עשיר לדאשבורד הניהול
            var viewModel = new AdminDashboardViewModel
            {
                SuccessRate = AuditLogStore.GetSuccessRate(),
                TotalActions = allAuditEntries.Count,
                SuccessCount = successCount,
                FailedCount = failedCount,
                RecentActiveUsers = AuditLogStore.GetRecentActiveUsers(10),
                AvgProcessingTimeMs = HealthMonitor.GetAverageProcessingMs(),
                HealthSamplesCount = HealthMonitor.GetAll().Count,
                Users = UserStore.GetUsers(),
                Settings = SystemSettingsStore.Get(),
                AppVersion = AppVersion.FullVersion
            };

            return View(viewModel);
        }

        // =====================================================================
        // Feature 2: Audit Log - תצוגת טבלת פעולות משתמשים מלאה
        // =====================================================================
        [HttpGet]
        public IActionResult AuditLog()
        {
            if (!IsUserAuthorized())
                return RedirectToAction("AccessDenied", "Home");

            var entries = AuditLogStore.GetAll();
            return View(entries);
        }

        // =====================================================================
        // Feature 3: System Logs - תצוגת לוג שגיאות מערכת עם תרגום עברי
        // =====================================================================
        [HttpGet]
        public IActionResult SystemLogs()
        {
            if (!IsUserAuthorized())
                return RedirectToAction("AccessDenied", "Home");

            var logs = SystemLogger.GetAll();
            return View(logs);
        }

        // =====================================================================
        // Feature 7: שמירת הגדרות מערכת דינמיות
        // =====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSettings(SystemSettings settings)
        {
            if (!IsUserAuthorized())
                return Json(new { success = false, message = "אין לך הרשאות לבצע פעולה זו!" });

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
            if (!IsUserAuthorized())
                return Json(new { success = false, message = "אין לך הרשאות ניהול לביצוע פעולה זו!" });

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
    }
}
