using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CompareD.Models
{
    // מודל המייצג פרטי משתמש במערכת
    public class UserInfo
    {
        public string Username { get; set; } = string.Empty;
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
                    user = new UserInfo
                    {
                        Username = username,
                        LastLogin = DateTime.Now,
                        IsBlocked = false
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
                        LastLogin = DateTime.Now,
                        IsBlocked = false
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
    }

    // מחלקת עזר לרישום Audit Log לקובץ טקסט מקומי בשרת ללא פגיעה בביצועי ה-DB
    public static class AuditLogger
    {
        private static readonly string LogsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string FilePath = Path.Combine(LogsDirectory, "audit.log");
        private static readonly object LogLock = new object();

        // רישום שורת לוג מאובטחת
        public static void LogAction(string username, string action, string details)
        {
            lock (LogLock)
            {
                try
                {
                    if (!Directory.Exists(LogsDirectory))
                    {
                        Directory.CreateDirectory(LogsDirectory);
                    }

                    var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] User: {username} | Action: {action} | Details: {details}{Environment.NewLine}";
                    File.AppendAllText(FilePath, logLine);
                }
                catch
                {
                    // כשל ברישום לוג לקובץ
                }
            }
        }
    }

    // מודל המייצג את מגבלות הקבצים מתוך הגדרות המערכת
    public class FileLimits
    {
        public int MaxPerFileMb { get; set; } = 50;
        public int MaxTotalRequestMb { get; set; } = 105;
    }
}
