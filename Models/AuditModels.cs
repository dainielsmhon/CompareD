using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CompareD.Models
{
    // =====================================================================
    // מודל רשומת ה-Audit Log - מייצג פעולת משתמש אחת במערכת
    // =====================================================================
    public class AuditLogEntry
    {
        // תאריך ושעת ביצוע הפעולה
        public DateTime Timestamp { get; set; }

        // שם המשתמש/מחשב שביצע את הפעולה (מ-Active Directory)
        public string Username { get; set; } = string.Empty;

        // תיאור קצר של הפעולה שבוצעה (לדוגמה: "Login", "Compare")
        public string Action { get; set; } = string.Empty;

        // פרטים נוספים על הפעולה
        public string Details { get; set; } = string.Empty;

        // סטטוס הפעולה: Success (הצלחה) או Failed (כישלון)
        public string Status { get; set; } = "Success";
    }

    // =====================================================================
    // מחלקת Audit Logger המשודרגת - כותבת ל-JSON לתצוגה בממשק הניהול
    // =====================================================================
    public static class AuditLogStore
    {
        private static readonly string LogsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string AuditJsonPath = Path.Combine(LogsDirectory, "audit_structured.json");
        private static readonly object AuditLock = new object();

        // רישום פעולה עם סטטוס - הפונקציה המרכזית
        public static void Log(string username, string action, string details, string status = "Success")
        {
            lock (AuditLock)
            {
                try
                {
                    // יצירת תיקיית logs אם לא קיימת
                    if (!Directory.Exists(LogsDirectory))
                        Directory.CreateDirectory(LogsDirectory);

                    // שליפת הרשומות הקיימות
                    var entries = GetAll();

                    // הוספת הרשומה החדשה בראש הרשימה (מהחדש לישן)
                    entries.Insert(0, new AuditLogEntry
                    {
                        Timestamp = DateTime.Now,
                        Username = username,
                        Action = action,
                        Details = details,
                        Status = status
                    });

                    // שמירת עד 1000 רשומות אחרונות בלבד למניעת צמיחת קובץ בלתי מוגבלת
                    if (entries.Count > 1000)
                        entries = entries.Take(1000).ToList();

                    // כתיבה לקובץ JSON
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(AuditJsonPath, json);
                }
                catch
                {
                    // כישלון בכתיבה - המערכת ממשיכה לפעול ללא הפרעה
                }
            }
        }

        // שליפת כל רשומות ה-Audit Log (ממוינות מהחדש לישן)
        public static List<AuditLogEntry> GetAll()
        {
            lock (AuditLock)
            {
                try
                {
                    if (!File.Exists(AuditJsonPath))
                        return new List<AuditLogEntry>();

                    var json = File.ReadAllText(AuditJsonPath);
                    return JsonSerializer.Deserialize<List<AuditLogEntry>>(json) ?? new List<AuditLogEntry>();
                }
                catch
                {
                    return new List<AuditLogEntry>();
                }
            }
        }

        // שליפת N רשומות אחרונות בלבד (לדאשבורד)
        public static List<AuditLogEntry> GetRecent(int count = 20)
        {
            return GetAll().Take(count).ToList();
        }

        // חישוב אחוז הצלחה מתוך כל הרשומות
        public static double GetSuccessRate()
        {
            var all = GetAll();
            if (all.Count == 0) return 0;
            var successCount = all.Count(e => e.Status == "Success");
            return Math.Round((double)successCount / all.Count * 100, 1);
        }

        // שליפת רשימת המשתמשים הפעילים לאחרונה (ייחודיים)
        public static List<string> GetRecentActiveUsers(int count = 10)
        {
            return GetAll()
                .Select(e => e.Username)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .ToList();
        }
    }
}
