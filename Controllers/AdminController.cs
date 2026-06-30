using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using CompareD.Models;

namespace CompareD.Controllers
{
    // בקר מוגן לאימות משתמשי Active Directory
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

        // תצוגת מסך הניהול הראשי
        [HttpGet]
        public IActionResult Index()
        {
            if (!IsUserAuthorized())
            {
                // רישום ניסיון גישה לא מורשה ב-Audit Log
                AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "UnauthorizedAdminAccessAttempt", "User tried to access User Management page");
                return RedirectToAction("AccessDenied", "Home");
            }

            var users = UserStore.GetUsers();
            
            // וידוא שמנהל המערכת עצמו רשום במחסן המשתמשים
            var currentUsername = User.Identity?.Name ?? string.Empty;
            if (!string.IsNullOrEmpty(currentUsername))
            {
                UserStore.GetOrCreateUser(currentUsername);
            }
            
            // טעינה עדכנית של רשימת המשתמשים
            users = UserStore.GetUsers();

            return View(users);
        }

        // שינוי סטטוס חסימת משתמש (נקרא באמצעות AJAX מהממשק)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleStatus(string username, bool isBlocked)
        {
            if (!IsUserAuthorized())
            {
                return Json(new { success = false, message = "אין לך הרשאות ניהול לביצוע פעולה זו!" });
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return Json(new { success = false, message = "שם משתמש לא תקין!" });
            }

            var currentUsername = User.Identity?.Name ?? string.Empty;
            
            // הגנה: מניעת חסימה עצמית של מנהל המערכת המחובר
            if (string.Equals(username, currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "שגיאה: אינך יכול לחסום את עצמך מהמערכת!" });
            }

            // ביצוע שינוי הסטטוס
            if (!UserStore.ToggleBlockedStatus(username, isBlocked))
            {
                return Json(new { success = false, message = "המשתמש לא נמצא במערכת." });
            }

            // רישום הפעולה ב-Audit Log המקומי
            var logActionType = isBlocked ? "BlockedUser" : "UnblockedUser";
            AuditLogger.LogAction(currentUsername, logActionType, $"Changed status of user '{username}' to blocked={isBlocked}");

            return Json(new { success = true });
        }
    }
}
