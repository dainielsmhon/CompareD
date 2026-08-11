using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CompareD.Models
{
    public class UserInfo
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AuthType { get; set; } = "Active Directory";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastLogin { get; set; }
        public bool IsBlocked { get; set; }
    }

    // מחלקת עזר לניהול משתמשים בקובץ JSON מקומי (Thread-Safe)
    public static class UserStore
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "users.json");
        private static readonly object FileLock = new object();

        // שליפת רשימת המשתמשים
        public static List<UserInfo> GetUsers()
        {
            lock (FileLock)
            {
                if (!File.Exists(FilePath))
                {
                    return new List<UserInfo>();
                }

                try
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<List<UserInfo>>(json) ?? new List<UserInfo>();
                }
                catch
                {
                    return new List<UserInfo>();
                }
            }
        }

        // שמירת רשימת המשתמשים לתוך הקובץ
        public static void SaveUsers(List<UserInfo> users)
        {
            lock (FileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(FilePath, json);
                }
                catch
                {
                    // שגיאת כתיבה זמנית
                }
            }
        }

        // שליפת משתמש או יצירתו במידה ואינו קיים
        public static UserInfo GetOrCreateUser(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return new UserInfo();

            lock (FileLock)
            {
                var users = GetUsers();
                var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    // אבטחה מחמירה (Strict Auth): כל משתמש חדש שמנסה להתחבר נוצר כחסום אוטומטית!
                    // רק מנהל יכול לאשר אותו או להוסיף אותו מראש
                    user = new UserInfo
                    {
                        Username = username,
                        CreatedAt = DateTime.Now,
                        LastLogin = DateTime.Now,
                        IsBlocked = true, // חסום כברירת מחדל!
                        AuthType = "Active Directory"
                    };
                    users.Add(user);
                    SaveUsers(users);
                }
                return user;
            }
        }

        // עדכון זמן כניסה אחרון של משתמש
        public static void UpdateLastLogin(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;

            lock (FileLock)
            {
                var users = GetUsers();
                var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    user = new UserInfo
                    {
                        Username = username,
                        CreatedAt = DateTime.Now,
                        LastLogin = DateTime.Now,
                        IsBlocked = true, // חסום כברירת מחדל!
                        AuthType = "Active Directory"
                    };
                    users.Add(user);
                }
                else
                {
                    user.LastLogin = DateTime.Now;
                }
                SaveUsers(users);
            }
        }

        // Toggle blocked status; returns false when the user record was not found
        public static bool ToggleBlockedStatus(string username, bool isBlocked)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            lock (FileLock)
            {
                var users = GetUsers();
                var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    return false;
                }

                user.IsBlocked = isBlocked;
                SaveUsers(users);
                return true;
            }
        }

        // יצירת משתמש שאושר מראש (Pre-Approved) על ידי המנהל מהממשק
        public static bool AddApprovedUser(string username, string fullName, string email, string authType)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            lock (FileLock)
            {
                var users = GetUsers();
                var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                
                if (user != null)
                {
                    // משתמש כבר קיים - רק נעדכן פרטים ונאשר אותו
                    user.FullName = fullName;
                    user.Email = email;
                    user.AuthType = authType;
                    user.IsBlocked = false; // נאשר אותו
                }
                else
                {
                    // הוספת משתמש חדש כמאושר
                    users.Add(new UserInfo
                    {
                        Username = username,
                        FullName = fullName,
                        Email = email,
                        AuthType = authType,
                        CreatedAt = DateTime.Now,
                        LastLogin = DateTime.MinValue, // טרם התחבר
                        IsBlocked = false // מאושר מראש!
                    });
                }
                
                SaveUsers(users);
                return true;
            }
        }
    }

    // =====================================================================
    // מחלקת גשר (Bridge) - שומרת על תאימות אחורה עם קריאות ישנות
    // ומפנה אותן למנגנון הרישום המשודרג עם סטטוס מלא
    // =====================================================================
    public static class AuditLogger
    {
        // רישום שורת לוג - ממשק תואם לקוד הקיים (Status ברירת מחדל: Success)
        public static void LogAction(string username, string action, string details, string status = "Success")
        {
            // קריאה למחלקת ה-AuditLogStore המשודרגת שכותבת JSON מובנה
            AuditLogStore.Log(username, action, details, status);
        }
    }

    // מודל המייצג את מגבלות הקבצים מתוך הגדרות המערכת
    public class FileLimits
    {
        public int MaxPerFileMb { get; set; } = 50;
        public int MaxTotalRequestMb { get; set; } = 105;
    }
}
