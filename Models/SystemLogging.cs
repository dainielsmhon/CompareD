using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CompareD.Models
{
    // =====================================================================
    // מודל שגיאת מערכת - מייצג רשומת שגיאה אחת בלוג המערכת
    // =====================================================================
    public class SystemLogEntry
    {
        // תאריך ושעת השגיאה
        public DateTime Timestamp { get; set; }

        // קוד שגיאת HTTP (לדוגמה: 500, 504)
        public int HttpStatusCode { get; set; }

        // הודעת השגיאה הטכנית המקורית מהשרת
        public string ErrorMessage { get; set; } = string.Empty;

        // נתיב הבקשה שגרמה לשגיאה
        public string RequestPath { get; set; } = string.Empty;

        // שם המשתמש שחווה את השגיאה
        public string Username { get; set; } = string.Empty;

        // הסבר בעברית נגישה למנהל המערכת (מתורגם אוטומטית)
        public string HebrewExplanation { get; set; } = string.Empty;
    }

    // =====================================================================
    // מנוע תרגום שגיאות HTTP לעברית - OFFLINE ולא תלוי ב-API חיצוני
    // =====================================================================
    public static class HttpErrorTranslator
    {
        // מילון הסבר בעברית לשגיאות HTTP נפוצות
        private static readonly Dictionary<int, string> Translations = new Dictionary<int, string>
        {
            { 400, "בקשה שגויה: הדפדפן שלח נתונים שהשרת אינו מבין. ייתכן שהטופס לא מולא כהלכה." },
            { 401, "נדרשת התחברות: המשתמש לא מאומת. יש להתחבר מחדש למערכת." },
            { 403, "גישה נדחתה: למשתמש אין הרשאות מתאימות לגשת לדף זה." },
            { 404, "דף לא נמצא: הכתובת שנדרשה אינה קיימת בשרת. ייתכן שמדובר בקישור שבור." },
            { 408, "תם הזמן הקצוב לבקשה: הלקוח לא סיים לשלוח את הבקשה בזמן. בדוק את חיבור הרשת." },
            { 413, "קובץ גדול מדי: הקובץ שנשלח חורג ממגבלת הגודל המוגדרת במערכת." },
            { 429, "יותר מדי בקשות: המשתמש שלח בקשות רבות מדי בפרק זמן קצר. נסה שוב מאוחר יותר." },
            { 500, "שגיאה פנימית בשרת: אירעה תקלה בלתי צפויה בשרת. פנה למנהל המערכת ורשום את השעה והפעולה." },
            { 502, "שגיאת Gateway: השרת קיבל תשובה שגויה משרת ביניים. בדוק שכל שירותי ה-IIS פועלים." },
            { 503, "שירות אינו זמין: השרת עמוס מדי או בתחזוקה. נסה שוב בעוד מספר דקות." },
            { 504, "תם הזמן הקצוב ל-Gateway: השרת לא קיבל תשובה מהשרת הפנימי בזמן. ייתכן שהשאילתה ארוכה מדי או שיש בעיית רשת." }
        };

        // תרגום קוד שגיאה לעברית (מחזיר הסבר ברירת מחדל אם לא נמצא)
        public static string Translate(int httpStatusCode)
        {
            return Translations.TryGetValue(httpStatusCode, out var explanation)
                ? explanation
                : $"שגיאת מערכת לא מוכרת (קוד: {httpStatusCode}). פנה למנהל המערכת עם פרטים אלו.";
        }
    }

    // =====================================================================
    // מחלקת כתיבת לוג שגיאות מערכת לקובץ פיזי מקומי
    // =====================================================================
    public static class SystemLogger
    {
        private static readonly string LogsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string SystemLogPath = Path.Combine(LogsDirectory, "system_errors.json");
        private static readonly object SysLogLock = new object();

        // רישום שגיאת מערכת כולל תרגום אוטומטי לעברית
        public static void LogError(int statusCode, string errorMessage, string requestPath, string username)
        {
            lock (SysLogLock)
            {
                try
                {
                    if (!Directory.Exists(LogsDirectory))
                        Directory.CreateDirectory(LogsDirectory);

                    var entries = GetAll();

                    // הוספת רשומה חדשה עם תרגום אוטומטי לעברית
                    entries.Insert(0, new SystemLogEntry
                    {
                        Timestamp = DateTime.Now,
                        HttpStatusCode = statusCode,
                        ErrorMessage = errorMessage,
                        RequestPath = requestPath,
                        Username = username,
                        HebrewExplanation = HttpErrorTranslator.Translate(statusCode)
                    });

                    // שמירת עד 500 שגיאות אחרונות בלבד
                    if (entries.Count > 500)
                        entries = entries.Take(500).ToList();

                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SystemLogPath, json);
                }
                catch
                {
                    // כישלון בכתיבה - לא מונע פעולת המערכת
                }
            }
        }

        // שליפת כל רשומות לוג השגיאות
        public static List<SystemLogEntry> GetAll()
        {
            lock (SysLogLock)
            {
                try
                {
                    if (!File.Exists(SystemLogPath))
                        return new List<SystemLogEntry>();

                    var json = File.ReadAllText(SystemLogPath);
                    return JsonSerializer.Deserialize<List<SystemLogEntry>>(json) ?? new List<SystemLogEntry>();
                }
                catch
                {
                    return new List<SystemLogEntry>();
                }
            }
        }
    }

    // =====================================================================
    // מודל לניטור בריאות המערכת (System Health Monitor)
    // =====================================================================
    public class HealthSnapshot
    {
        // זמן ביצוע (מילישניות) של פעולת השוואה אחת
        public double ProcessingTimeMs { get; set; }

        // תאריך ושעת המדידה
        public DateTime MeasuredAt { get; set; }

        // שם המשתמש שביצע את הפעולה
        public string Username { get; set; } = string.Empty;
    }

    // מחלקת שמירה ושליפת נתוני בריאות המערכת
    public static class HealthMonitor
    {
        private static readonly string LogsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string HealthLogPath = Path.Combine(LogsDirectory, "health_metrics.json");
        private static readonly object HealthLock = new object();

        // רישום מדידת זמן עיבוד פעולת השוואה
        public static void RecordProcessing(double milliseconds, string username)
        {
            lock (HealthLock)
            {
                try
                {
                    if (!Directory.Exists(LogsDirectory))
                        Directory.CreateDirectory(LogsDirectory);

                    var entries = GetAll();
                    entries.Insert(0, new HealthSnapshot
                    {
                        ProcessingTimeMs = milliseconds,
                        MeasuredAt = DateTime.Now,
                        Username = username
                    });

                    // שמירת 200 מדידות אחרונות בלבד
                    if (entries.Count > 200)
                        entries = entries.Take(200).ToList();

                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(HealthLogPath, json);
                }
                catch { }
            }
        }

        // חישוב זמן עיבוד ממוצע (במילישניות)
        public static double GetAverageProcessingMs()
        {
            var all = GetAll();
            if (all.Count == 0) return 0;
            return Math.Round(all.Average(h => h.ProcessingTimeMs), 1);
        }

        // שליפת כל המדידות
        public static List<HealthSnapshot> GetAll()
        {
            lock (HealthLock)
            {
                try
                {
                    if (!File.Exists(HealthLogPath))
                        return new List<HealthSnapshot>();

                    var json = File.ReadAllText(HealthLogPath);
                    return JsonSerializer.Deserialize<List<HealthSnapshot>>(json) ?? new List<HealthSnapshot>();
                }
                catch
                {
                    return new List<HealthSnapshot>();
                }
            }
        }
    }
}
