using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CompareD.Filters
{
    // =====================================================================
    // מקור אמת יחיד להגדרה "מי מנהל מערכת".
    // עד לריכוז כאן הבדיקה הייתה משוכפלת בין AdminController ל-UserActivityFilter,
    // ושכפול של החלטת הרשאה הוא בדיוק הדרך שבה שני המקומות מתחילים להתנהג שונה.
    // =====================================================================
    public static class AdminAuthorization
    {
        // מפתח הקונפיגורציה שמחזיק את רשימת המנהלים המורשים
        private const string AuthorizedUsersKey = "AdminSettings:AuthorizedUsers";

        // בדיקה האם שם המשתמש הנתון מוגדר כמנהל מורשה.
        // מחמיר כברירת מחדל: שם משתמש ריק, קונפיגורציה חסרה או רשימה ריקה - אינם מנהל.
        public static bool IsAdmin(IConfiguration? configuration, string? username)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            var authorizedUsers = configuration.GetSection(AuthorizedUsersKey).Get<List<string>>();
            if (authorizedUsers == null || authorizedUsers.Count == 0)
            {
                return false;
            }

            return authorizedUsers.Any(u => string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
        }
    }
}
