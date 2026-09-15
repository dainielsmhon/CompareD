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

        // תרגום לעברית של הודעת השגיאה הטכנית עצמה, ולא רק של קוד ה-HTTP.
        // הודעות מ-SQL Server, מאורקל ומ-NET. מגיעות באנגלית, והן מה שבאמת
        // מסביר את התקלה - קוד 500 לבדו אינו אומר דבר על הסיבה.
        // ריק כשלא זוהתה תבנית מוכרת; במקרה כזה ההודעה המקורית היא המידע.
        public string HebrewErrorMessage { get; set; } = string.Empty;

        // המלצת פעולה מעשית למנהל המערכת, כשניתן לגזור אותה מסוג התקלה
        public string SuggestedAction { get; set; } = string.Empty;
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
    // מנוע תרגום הודעות שגיאה טכניות לעברית - OFFLINE, ללא API חיצוני
    //
    // קוד HTTP לבדו אינו מסביר תקלה: 500 יכול להיות סיסמה שגויה לאורקל,
    // קובץ נעול, או טבלה שאינה קיימת. ההודעה הטכנית היא מה שמסביר, והיא
    // מגיעה באנגלית מ-SQL Server, מאורקל ומ-NET. - ולכן היא מתורגמת כאן.
    //
    // הזיהוי הוא לפי תבנית בתוך ההודעה ולא לפי התאמה מדויקת, מפני שההודעות
    // מכילות שמות שרתים, מספרי פורטים ונתיבים שמשתנים בכל מופע.
    // =====================================================================
    public static class ErrorMessageTranslator
    {
        // כל רשומה: תבנית לזיהוי, הסבר בעברית, והמלצת פעולה.
        // הסדר קובע - התבנית הראשונה שמתאימה היא הקובעת, ולכן
        // תבניות ספציפיות מופיעות לפני תבניות כלליות.
        private static readonly (string Pattern, string Hebrew, string Action)[] Patterns = new[]
        {
            // --- אורקל ---
            ("ORA-12541", "אורקל: אין מאזין (Listener) בכתובת ובפורט שצוינו.",
                "בדוק שה-Listener פועל על שרת אורקל ושהפורט נכון (ברירת מחדל 1521)."),
            ("ORA-12154", "אורקל: לא ניתן לפענח את שם השירות שצוין.",
                "בדוק את שם ה-SID או ה-Service Name בטופס החיבור."),
            ("ORA-01017", "אורקל: שם משתמש או סיסמה שגויים.",
                "בדוק את פרטי ההזדהות. שים לב שסיסמה באורקל עשויה להיות תלוית רישיות."),
            ("ORA-28000", "אורקל: חשבון המשתמש נעול.",
                "יש לבקש מ-DBA לשחרר את החשבון."),
            ("ORA-00942", "אורקל: הטבלה או התצוגה אינה קיימת, או שאין למשתמש הרשאה אליה.",
                "בדוק את שם הטבלה ואת הרשאות הקריאה של המשתמש."),
            ("ORA-12170", "אורקל: תם הזמן הקצוב ליצירת החיבור.",
                "בדוק קישוריות רשת וחומת אש בין השרת לשרת אורקל."),
            ("ORA-", "אורקל החזיר שגיאה. הקוד המדויק מופיע בהודעה המקורית.",
                "חפש את קוד ה-ORA בתיעוד אורקל או פנה ל-DBA."),

            // --- SQL Server ---
            ("Login failed for user", "SQL Server: ההתחברות נדחתה - שם משתמש או סיסמה שגויים.",
                "בדוק את פרטי ההזדהות ואת הרשאות המשתמש על מסד הנתונים."),
            ("A network-related or instance-specific error",
                "SQL Server: לא ניתן להגיע לשרת. השרת אינו נמצא, אינו זמין, או שהחיבור נחסם.",
                "בדוק את שם השרת, שהשירות פועל, ושחומת האש מאפשרת את הפורט."),
            ("Cannot open database", "SQL Server: אין הרשאה לפתוח את מסד הנתונים המבוקש.",
                "בדוק את שם מסד הנתונים ואת הרשאות המשתמש עליו."),
            ("Invalid object name", "SQL Server: שם הטבלה או התצוגה אינו קיים.",
                "בדוק את שם הטבלה, כולל הסכימה (למשל dbo.TableName)."),
            ("Execution Timeout Expired", "תם הזמן הקצוב לביצוע השאילתה.",
                "צמצם את מספר השורות בהשוואה, או הוסף סינון כדי לקצר את השאילתה."),
            ("Timeout expired", "תם הזמן הקצוב לפעולה מול מסד הנתונים.",
                "בדוק עומס על השרת, וצמצם את היקף ההשוואה."),

            // --- קבצים והרשאות ---
            ("being used by another process", "הקובץ נעול על ידי תהליך אחר.",
                "סגור את הקובץ בכל יישום שפתח אותו, או עצור את השרת אם הוא מחזיק אותו."),
            ("Access to the path", "אין הרשאת גישה לנתיב שצוין.",
                "הענק הרשאת Modify לזהות ה-Application Pool על התיקייה."),
            ("Could not find file", "הקובץ המבוקש אינו קיים בנתיב שצוין.",
                "בדוק שהקובץ הועלה בהצלחה ושתוקף הסשן לא פג."),
            ("The process cannot access the file", "לא ניתן לגשת לקובץ - הוא נעול.",
                "בדוק אם השרת או תהליך אחר מחזיקים את הקובץ."),

            // --- אבטחה וסשן ---
            ("antiforgery", "אסימון ההגנה מפני CSRF פג או אינו תקין.",
                "רענן את הדף והתחל את הפעולה מחדש. אם זה חוזר - בדוק שהאתר מוגש ב-HTTPS."),
            ("not an SSL request", "הפעולה נדחתה מפני שהבקשה אינה מוצפנת.",
                "האתר חייב Binding של HTTPS ב-IIS. ב-HTTP כל טופס במערכת ייכשל."),

            // --- כלליות ---
            ("Object reference not set", "שגיאת תכנה: ניסיון לגשת לערך שאינו קיים.",
                "רשום את השעה ואת הפעולה שבוצעה, ופנה לצוות הפיתוח."),
            ("The operation was canceled", "הפעולה בוטלה לפני שהסתיימה.",
                "ייתכן שהמשתמש עזב את הדף או שתם הזמן הקצוב. נסה שוב."),
            ("Out of memory", "השרת נגמר לו הזיכרון בזמן העיבוד.",
                "צמצם את מספר השורות בהשוואה או את גודל הקבצים המועלים.")
        };

        // תרגום הודעת שגיאה טכנית. מחזיר מחרוזות ריקות כשלא זוהתה תבנית מוכרת -
        // במקרה כזה ההודעה המקורית באנגלית היא המידע, ואין להמציא הסבר.
        public static (string Hebrew, string Action) Translate(string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage)) return (string.Empty, string.Empty);

            foreach (var (pattern, hebrew, action) in Patterns)
            {
                if (errorMessage.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return (hebrew, action);
            }

            return (string.Empty, string.Empty);
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
                    // תרגום ההודעה הטכנית עצמה, בנוסף לתרגום קוד ה-HTTP.
                    // קוד 500 לבדו אינו מסביר תקלה; ההודעה היא מה שמסביר.
                    var (hebrewMessage, suggestedAction) = ErrorMessageTranslator.Translate(errorMessage);

                    entries.Insert(0, new SystemLogEntry
                    {
                        Timestamp = DateTime.Now,
                        HttpStatusCode = statusCode,
                        ErrorMessage = errorMessage,
                        RequestPath = requestPath,
                        Username = username,
                        HebrewExplanation = HttpErrorTranslator.Translate(statusCode),
                        HebrewErrorMessage = hebrewMessage,
                        SuggestedAction = suggestedAction
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
