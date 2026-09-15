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
    // =====================================================================
    // פסק הדין של לוח הבקרה: שורה אחת שאומרת מה דורש תשומת לב.
    //
    // הסיבה שזה מחושב בשרת ולא נגזר במסך: המסך פתח בארבעה מספרים והמנהל
    // נדרש להסיק מהם בעצמו מה בוער. שלב שנכשל אצל יותר מעובד אחד הוא
    // הממצא שקודם לכל השאר, ולכן הוא זה שעולה לכותרת.
    // =====================================================================
    public class DashboardVerdict
    {
        // Ok / Warning / Critical - קובע את צבע הפס ואת הנקודה
        public string Status { get; set; } = "Ok";

        // הכותרת: מה קרה, בשורה אחת
        public string Headline { get; set; } = string.Empty;

        // השורה השנייה: הפירוט שמאפשר לפעול
        public string Detail { get; set; } = string.Empty;
    }

    // =====================================================================
    // פעילות של יום בודד, לגרף 14 הימים בלוח הבקרה.
    // הגרף מראה שבועיים במבט אחד - מה שרשימה כרונולוגית אינה מראה.
    // =====================================================================
    public class DailyActivity
    {
        // התאריך בתצוגה מקוצרת, למשל 08.09
        public string Label { get; set; } = string.Empty;

        public DateTime Date { get; set; }

        // פעולות שהצליחו ופעולות שנכשלו באותו יום
        public int Successes { get; set; }
        public int Failures { get; set; }

        public int Total => Successes + Failures;
    }

    // =====================================================================
    // שינוי מול השבוע שעבר. מספר בודד אינו אומר אם המצב משתפר או מתדרדר,
    // ולכן כל מדד בלוח הבקרה מוצג יחד עם הכיוון שלו.
    // =====================================================================
    public class WeeklyTrend
    {
        // מספר ההשוואות בשבוע האחרון ובשבוע שלפניו
        public int RunsThisWeek { get; set; }
        public int RunsLastWeek { get; set; }
        public int RunsDelta => RunsThisWeek - RunsLastWeek;

        // אחוז ההצלחה בשבוע האחרון ובשבוע שלפניו
        public double RateThisWeek { get; set; }
        public double RateLastWeek { get; set; }
        public double RateDelta => Math.Round(RateThisWeek - RateLastWeek, 1);

        // האם בשבוע שלפני האחרון נרשמה פעילות בכלל.
        //
        // בלי הדגל הזה הלוח הציג "+91.7 נקודות" בשבוע הראשון של השימוש -
        // מה שנקרא כשיפור דרמטי, כשבפועל פשוט לא היה עם מה להשוות.
        // דלתא מוצגת רק כשיש בסיס השוואה אמיתי.
        public bool HasPreviousWeek { get; set; }
    }

    // =====================================================================
    // סטטיסטיקת פעילות למשתמש בודד, לצורך מסך הניהול
    // =====================================================================
    public class UserActivityStats
    {
        public string Username { get; set; } = string.Empty;

        // סך הפעולות שהמשתמש ביצע במערכת
        public int TotalActions { get; set; }
        public int SuccessfulActions { get; set; }
        public int FailedActions { get; set; }

        // אחוז ההצלחה של המשתמש הזה בלבד
        public double SuccessRate { get; set; }

        // מספר ההשוואות שהמשתמש הריץ בהצלחה - המדד המעשי לשימוש בכלי
        public int ComparisonRuns { get; set; }

        // מתי היה פעיל לאחרונה
        public DateTime LastActivity { get; set; }

        // הפעולה שנכשלה אצלו הכי הרבה פעמים - השלב שבו הוא נתקע
        public string MostFrequentFailure { get; set; } = string.Empty;
        public int MostFrequentFailureCount { get; set; }
    }

    // =====================================================================
    // פירוט כשלים לפי שלב: איפה נופלים, כמה פעמים, וכמה משתמשים שונים
    // =====================================================================
    public class FailureBreakdown
    {
        public string Action { get; set; } = string.Empty;

        // שם הפעולה בעברית, לתצוגה למנהל שאינו מכיר את שמות הפעולות בקוד
        public string HebrewAction { get; set; } = string.Empty;

        public int Count { get; set; }

        // מספר המשתמשים השונים שנתקעו בשלב הזה. אחד = תקלה נקודתית,
        // כמה = בעיה במערכת או בהדרכה.
        public int DistinctUsers { get; set; }

        public DateTime LastOccurrence { get; set; }
        public string LastDetails { get; set; } = string.Empty;
    }

    // =====================================================================
    // תרגום שמות הפעולות בלוג הביקורת לעברית.
    // שמות הפעולות נכתבים באנגלית בקוד (כמו כל מזהה בפרויקט), אך מנהל
    // המערכת אינו אמור להכיר אותם - ולכן הם מתורגמים בתצוגה.
    // =====================================================================
    public static class AuditActionTranslator
    {
        private static readonly Dictionary<string, string> Translations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "DatabaseConnect", "התחברות למסד נתונים" },
            { "DatabaseCompare", "השוואה מול מסד נתונים" },
            { "DatabaseCompareError", "כשל בהשוואה מול מסד נתונים" },
            { "SchemaReviewError", "כשל בטעינת סכימת הטבלאות" },
            { "FilesUpload", "העלאת קבצים להשוואה" },
            { "FileUploadError", "כשל בהעלאת קבצים" },
            { "FilesCompare", "השוואת קבצים" },
            { "FilesCompareError", "כשל בהשוואת קבצים" },
            { "BlockedAccessAttempt", "ניסיון כניסה של משתמש חסום" },
            { "AddApprovedUser", "הוספת משתמש מאושר" },
            { "UpdatedSystemSettings", "עדכון הגדרות מערכת" },
            { "NoKeySelected", "לא נבחר שדה מפתח" },
            { "UnmappedKeyColumn", "עמודת מפתח שלא מופתה לעמודת יעד" },
            { "SessionExpired", "פג תוקף הקבצים שהועלו" },
            { "EmptyFile", "אחד הקבצים ריק" },
            { "FileTooLarge", "הקובץ גדול מהמותר לעיבוד בזיכרון" },
            { "NoColumnsFound", "לא נמצאו עמודות בקבצים" },
            { "UnauthorizedAdminAccessAttempt", "ניסיון גישה לא מורשה לפעולה ניהולית" },
            { "TempFileCleanup", "ניקוי קבצים זמניים" },
            { "BlockedUser", "חסימת משתמש" },
            { "UnblockedUser", "שחרור חסימת משתמש" },
            { "PreviewData", "תצוגה מקדימה לנתונים" },
            { "DownloadReport", "הורדת דוח" },
            { "SwapFiles", "החלפת כיוון ההשוואה" },
            { "SchemaReview", "סקירת סכימת טבלאות" },
            { "FilesSchemaReview", "סקירת סכימת הקבצים שהועלו" }
        };

        public static string Translate(string? action)
        {
            if (string.IsNullOrWhiteSpace(action)) return string.Empty;
            return Translations.TryGetValue(action, out var hebrew) ? hebrew : action;
        }
    }

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

                    // כתיבה דרך קובץ זמני והחלפה, ולא כתיבה ישירה.
                    //
                    // File.WriteAllText קודם מקצר את הקובץ לאורך אפס ואז
                    // כותב. תקלה בין שני הצעדים - קריסה, הפסקת חשמל, אתחול
                    // של IIS באמצע הכתיבה - הותירה קובץ JSON חתוך.
                    // GetAll תופס את שגיאת הפרסור ומחזיר רשימה ריקה, ולכן
                    // הרישום הבא היה כותב קובץ עם רשומה אחת: כל מסלול
                    // הביקורת נמחק בשקט, בלי שאף אחד יודע.
                    //
                    // בשיטה הזו הקובץ המקורי נשאר שלם עד שהחדש נכתב
                    // במלואו, וההחלפה עצמה היא פעולה אחת של מערכת הקבצים.
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    var tempPath = AuditJsonPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, AuditJsonPath, overwrite: true);
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
                    // קובץ שאינו נקרא נשמר בצד ואינו נדרס.
                    //
                    // בלי זה, קובץ פגום היה מוחזר כרשימה ריקה והרישום הבא
                    // היה כותב עליו - כלומר המקרה היחיד שבו יש חשד לתקלה
                    // הוא בדיוק המקרה שבו העדות נמחקת. עכשיו הקובץ הפגום
                    // נשמר בשם אחר וניתן לבדוק אותו.
                    try
                    {
                        if (File.Exists(AuditJsonPath))
                        {
                            var quarantine = AuditJsonPath + ".corrupt-"
                                + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                            if (!File.Exists(quarantine))
                                File.Move(AuditJsonPath, quarantine);
                        }
                    }
                    catch
                    {
                        // גם שמירת הקובץ הפגום נכשלה - אין מה לעשות מעבר לכך
                    }

                    return new List<AuditLogEntry>();
                }
            }
        }

        // שליפת N רשומות אחרונות בלבד (לדאשבורד)
        public static List<AuditLogEntry> GetRecent(int count = 20)
        {
            return GetAll().Take(count).ToList();
        }

        // כל פונקציות החישוב מקבלות את הרשומות כפרמטר אופציונלי.
        //
        // הסיבה: טעינת לוח הבקרה קראה את קובץ הביקורת מהדיסק שמונה פעמים
        // ופרסרה אותו מ-JSON בכל פעם, מפני שכל חישוב שלף את הרשומות בעצמו.
        // כשהבקר טוען פעם אחת ומעביר את הרשימה, נשארת קריאה אחת.
        // הפרמטר אופציונלי כדי שקריאות קיימות מקוד אחר ימשיכו לעבוד.

        // חישוב אחוז הצלחה מתוך כל הרשומות
        public static double GetSuccessRate(List<AuditLogEntry>? entries = null)
        {
            var all = entries ?? GetAll();
            if (all.Count == 0) return 0;

            // אותו כלל בכל המערכת: הצלחה היא בדיוק "Success", כל השאר כשל.
            // בלי אחידות, אחוז ההצלחה הגלובלי היה יכול לסתור את הפירוט לפי עובד.
            var successCount = all.Count(e =>
                string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase));
            return Math.Round((double)successCount / all.Count * 100, 1);
        }

        // שליפת רשימת המשתמשים הפעילים לאחרונה (ייחודיים)
        // סטטיסטיקה לכל משתמש: כמה פעולות ביצע, כמה הצליחו, ומתי היה פעיל לאחרונה.
        //
        // אחוז ההצלחה הגלובלי לבדו אינו מספיק כשהמערכת נמסרת לעובדים: הוא אינו
        // מבדיל בין עובד שמריץ בהצלחה לעובד שנתקע שוב ושוב באותו שלב.
        // הפירוט לפי משתמש הוא מה שמראה למי צריך הדרכה ובמה.
        public static List<UserActivityStats> GetPerUserStats(List<AuditLogEntry>? entries = null)
        {
            var all = entries ?? GetAll();

            return all
                .Where(e => !string.IsNullOrWhiteSpace(e.Username))
                .GroupBy(e => e.Username, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    int total = g.Count();
                    int failures = g.Count(e => !string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase));

                    // הפעולה שנכשלה אצל המשתמש הזה הכי הרבה פעמים - זה השלב
                    // שבו הוא נתקע, ולכן זה מה שכדאי להראות למנהל
                    var topFailure = g
                        .Where(e => !string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(e => e.Action, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(x => x.Count())
                        .FirstOrDefault();

                    return new UserActivityStats
                    {
                        Username = g.Key,
                        TotalActions = total,
                        SuccessfulActions = total - failures,
                        FailedActions = failures,
                        SuccessRate = total > 0 ? Math.Round((double)(total - failures) / total * 100, 1) : 0,
                        LastActivity = g.Max(e => e.Timestamp),
                        ComparisonRuns = g.Count(e =>
                            e.Action.Contains("Compare", StringComparison.OrdinalIgnoreCase) &&
                            !e.Action.Contains("Error", StringComparison.OrdinalIgnoreCase)),
                        MostFrequentFailure = topFailure?.Key ?? string.Empty,
                        MostFrequentFailureCount = topFailure?.Count() ?? 0
                    };
                })
                .OrderByDescending(s => s.TotalActions)
                .ToList();
        }

        // פירוט הכשלים לפי סוג הפעולה: באיזה שלב נופלים הכי הרבה, וכמה משתמשים
        // שונים נתקעו בו. שלב שנכשל אצל משתמש אחד הוא תקלה נקודתית;
        // שלב שנכשל אצל חמישה הוא בעיה במערכת או בהדרכה.
        // מי נחשב עובד. שם ריק, "Unknown" ו-"SYSTEM" אינם עובדים:
        // הראשון הוא בקשה בלי משתמש מזוהה, והשלישי הוא האפליקציה עצמה
        // (ניקוי קבצים זמניים). ספירתם כעובדים עיוותה גם את מספר
        // "העובדים השונים" בכל שלב כשל וגם את פסק הדין של הלוח.
        private static bool IsRealUser(string? username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            if (string.Equals(username, "Unknown", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(username, "SYSTEM", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static List<FailureBreakdown> GetFailureBreakdown(List<AuditLogEntry>? entries = null)
        {
            return (entries ?? GetAll())
                .Where(e => !string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Action, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FailureBreakdown
                {
                    Action = g.Key,
                    HebrewAction = AuditActionTranslator.Translate(g.Key),
                    Count = g.Count(),

                    // רק שמות משתמש אמיתיים נספרים כ"עובדים שונים".
                    //
                    // "SYSTEM" הוא ניקוי הקבצים הזמניים שהאפליקציה מריצה
                    // בעצמה, ו-"Unknown" הוא בקשה שלא נשא אותה משתמש
                    // מזוהה. שניהם נספרו כעובדים, ולכן כשל של המערכת
                    // עצמה יכול היה להיקרא "נכשל אצל 2 עובדים" ולעלות
                    // לפסק הדין במקום בעיה אמיתית של עובד.
                    DistinctUsers = g.Select(e => e.Username)
                        .Where(IsRealUser)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    LastOccurrence = g.Max(e => e.Timestamp),
                    LastDetails = g.OrderByDescending(e => e.Timestamp).First().Details
                })
                .OrderByDescending(f => f.Count)
                .ToList();
        }

        // חישוב פסק הדין. סדר העדיפויות מגלם את מה שבאמת חשוב:
        // שלב שנכשל אצל כמה עובדים קודם לשלב שנכשל אצל אחד, ושניהם
        // קודמים ל"הכל תקין" - כדי שהמסך לא יפתח בהרגעה כשיש בעיה.
        public static DashboardVerdict GetVerdict(List<AuditLogEntry>? entries = null)
        {
            var failures = GetFailureBreakdown(entries);

            if (failures.Count == 0)
            {
                return new DashboardVerdict
                {
                    Status = "Ok",
                    Headline = "לא נרשמו כשלים",
                    Detail = "כל הפעולות שנרשמו הסתיימו בהצלחה."
                };
            }

            // שלב שנכשל אצל יותר מעובד אחד אינו תקלה נקודתית
            var shared = failures.Where(f => f.DistinctUsers > 1)
                .OrderByDescending(f => f.DistinctUsers).ThenByDescending(f => f.Count).FirstOrDefault();

            if (shared != null)
            {
                return new DashboardVerdict
                {
                    Status = "Critical",
                    Headline = $"{shared.DistinctUsers} עובדים נתקעו באותו שלב",
                    Detail = $"{shared.HebrewAction} — {shared.Count} כשלים. " +
                             "שלב שנכשל אצל יותר מעובד אחד מצביע על בעיה במערכת או בהדרכה, ולא על תקלה נקודתית."
                };
            }

            var top = failures[0];
            int totalFailures = failures.Sum(f => f.Count);

            return new DashboardVerdict
            {
                Status = "Warning",
                Headline = $"{totalFailures} כשלים נרשמו, כולם אצל עובד בודד בכל שלב",
                Detail = $"השכיח ביותר: {top.HebrewAction} — {top.Count} פעמים."
            };
        }

        // פעילות יומית לגרף 14 הימים. ימים בלי פעילות מוחזרים כאפס ולא נדלגים,
        // כדי שהציר יהיה רציף וסקאלת הגבהים תהיה אחת לכל העמודות.
        public static List<DailyActivity> GetDailyActivity(int days = 14, List<AuditLogEntry>? entries = null)
        {
            var all = entries ?? GetAll();
            var today = DateTime.Today;

            // קיבוץ אחד לפי תאריך, ולא מעבר על כל הרשימה עבור כל אחד מ-14 הימים.
            // עם 1000 רשומות זה 14 מעברים מיותרים בכל טעינת דף.
            var byDate = all
                .GroupBy(e => e.Timestamp.Date)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Successes: g.Count(e => string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase)),
                        Failures: g.Count(e => !string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase))
                    ));

            var result = new List<DailyActivity>();
            for (int offset = days - 1; offset >= 0; offset--)
            {
                var day = today.AddDays(-offset);
                byDate.TryGetValue(day, out var counts);

                result.Add(new DailyActivity
                {
                    Date = day,
                    Label = day.ToString("dd.MM"),
                    Successes = counts.Successes,
                    Failures = counts.Failures
                });
            }

            return result;
        }

        // השוואת השבוע האחרון לשבוע שלפניו. זה מה שהופך מספר בודד לכיוון.
        public static WeeklyTrend GetWeeklyTrend(List<AuditLogEntry>? entries = null)
        {
            var all = entries ?? GetAll();
            var now = DateTime.Now;
            var weekAgo = now.AddDays(-7);
            var twoWeeksAgo = now.AddDays(-14);

            bool IsRun(AuditLogEntry e) =>
                e.Action.Contains("Compare", StringComparison.OrdinalIgnoreCase) &&
                !e.Action.Contains("Error", StringComparison.OrdinalIgnoreCase);

            double RateOf(List<AuditLogEntry> window)
            {
                if (window.Count == 0) return 0;
                int ok = window.Count(e => string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase));
                return Math.Round((double)ok / window.Count * 100, 1);
            }

            var thisWeek = all.Where(e => e.Timestamp >= weekAgo).ToList();
            var lastWeek = all.Where(e => e.Timestamp >= twoWeeksAgo && e.Timestamp < weekAgo).ToList();

            return new WeeklyTrend
            {
                RunsThisWeek = thisWeek.Count(IsRun),
                RunsLastWeek = lastWeek.Count(IsRun),
                RateThisWeek = RateOf(thisWeek),
                RateLastWeek = RateOf(lastWeek),
                HasPreviousWeek = lastWeek.Count > 0
            };
        }

        public static List<string> GetRecentActiveUsers(int count = 10, List<AuditLogEntry>? entries = null)
        {
            return (entries ?? GetAll())
                .Select(e => e.Username)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .ToList();
        }

        // =================================================================
        // מפת חום שנתית: יום אחד = ריבוע אחד, עוצמת הצבע לפי מספר הפעולות.
        //
        // גרף 14 הימים עונה על "מה קרה השבועיים האחרונים". מפת החום עונה
        // על שאלה אחרת - האם השימוש במערכת נמשך או שהוא התרכז בשבוע אחד -
        // ותופסת פחות מקום מ-365 עמודות.
        //
        // מיושר לתחילת שבוע כדי שכל שורה בגריד תהיה אותו יום בשבוע.
        // =================================================================
        public static List<DailyActivity> GetYearActivity(int weeks = 26, List<AuditLogEntry>? entries = null)
        {
            var all = entries ?? GetAll();

            // קיבוץ אחד לפי תאריך, כמו ב-GetDailyActivity ומאותה סיבה:
            // מעבר נפרד על כל הרשימה עבור כל יום היה 182 מעברים בטעינת דף.
            var byDate = all
                .GroupBy(e => e.Timestamp.Date)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Successes: g.Count(e => string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase)),
                        Failures: g.Count(e => !string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase))
                    ));

            // היום הראשון הוא תחילת השבוע (ראשון) שלפני weeks שבועות,
            // כדי שהגריד יתחיל בשורה שלמה ולא באמצע שבוע.
            var today = DateTime.Today;
            var startOfThisWeek = today.AddDays(-(int)today.DayOfWeek);
            var start = startOfThisWeek.AddDays(-7 * (weeks - 1));

            var result = new List<DailyActivity>();
            for (var day = start; day <= today; day = day.AddDays(1))
            {
                byDate.TryGetValue(day, out var counts);
                result.Add(new DailyActivity
                {
                    Date = day,
                    Label = day.ToString("dd.MM.yyyy"),
                    Successes = counts.Successes,
                    Failures = counts.Failures
                });
            }

            return result;
        }

        // =================================================================
        // משפך השלבים: באיזה שלב העובדים נושרים.
        //
        // אחוז ההצלחה הגלובלי אומר כמה פעולות הצליחו, לא כמה עובדים הגיעו
        // עד הדוח. עובד שהתחבר, מיפה, ואז נטש לפני ההשוואה אינו נספר כאף
        // כשל - ובכל זאת הוא בדיוק המקרה שצריך לטפל בו.
        //
        // המשפך מאחד את שני מסלולי העבודה (מסד נתונים וקבצי אקסל), מפני
        // שהשאלה "מי הגיע לדוח" זהה בשניהם.
        // =================================================================
        private static readonly (string Title, string Detail, string[] Actions)[] FunnelDefinition =
        {
            ("כניסה למערכת", "כל משתמש שנרשמה לו פעולה", new string[0]),
            ("חיבור או העלאת קבצים", "התחברות למסד או העלאת שני קבצים",
                new[] { "DatabaseConnect", "FilesUpload" }),
            ("סקירת סכימה ומיפוי", "טעינת העמודות ובחירת שדות המפתח",
                new[] { "SchemaReview", "FilesSchemaReview", "PreviewData" }),
            ("הרצת השוואה", "השוואה שהסתיימה והפיקה דוח",
                new[] { "DatabaseCompare", "FilesCompare" }),
            ("הורדת דוח", "ייצוא הדוח לוורד או לאקסל",
                new[] { "DownloadReport" })
        };

        public static List<FunnelStage> GetFunnel(List<AuditLogEntry>? entries = null)
        {
            var all = entries ?? GetAll();
            var result = new List<FunnelStage>();

            // הבסיס להשוואה הוא השלב הראשון. בלעדיו אחוז ההגעה חסר משמעות.
            // נספרים רק עובדים אמיתיים: SYSTEM הוא האפליקציה עצמה, ולכן
            // הוא היה מנפח את הבסיס ומוריד את אחוז ההגעה של כל השלבים.
            int baseUsers = all
                .Select(e => e.Username)
                .Where(IsRealUser)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            foreach (var stage in FunnelDefinition)
            {
                // השלב הראשון מוגדר כ"כל פעולה", ולכן רשימת הפעולות שלו ריקה
                var ofStage = stage.Actions.Length == 0
                    ? all
                    : all.Where(e => stage.Actions.Contains(e.Action, StringComparer.OrdinalIgnoreCase)).ToList();

                var succeeded = ofStage
                    .Where(e => string.Equals(e.Status, "Success", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                result.Add(new FunnelStage
                {
                    Title = stage.Title,
                    Detail = stage.Detail,
                    Actions = stage.Actions.ToList(),

                    // "הגיע לשלב" נמדד במשתמשים ייחודיים שהשלב הצליח להם.
                    // ספירת פעולות הייתה נותנת יתרון לעובד שהריץ עשר פעמים.
                    Users = succeeded
                        .Select(e => e.Username)
                        .Where(IsRealUser)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    Runs = succeeded.Count,
                    Failures = ofStage.Count - succeeded.Count,
                    BaseUsers = baseUsers
                });
            }

            return result;
        }

        // =================================================================
        // הרשומות שנשלחות לדפדפן לצורך הצגת פירוט בלחיצה.
        //
        // רק חלון של הימים האחרונים ורק השדות שמוצגים בחלון הפירוט, כדי
        // שדף הניהול לא יישא את כל לוג הביקורת ב-JSON.
        // =================================================================
        public static List<AuditLogEntry> GetDrillEntries(int days = 26 * 7, List<AuditLogEntry>? entries = null)
        {
            var cutoff = DateTime.Today.AddDays(-days);
            return (entries ?? GetAll())
                .Where(e => e.Timestamp.Date >= cutoff)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }

    // =====================================================================
    // שלב אחד במשפך השלבים
    // =====================================================================
    public class FunnelStage
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;

        // שמות הפעולות שמרכיבות את השלב. נשלחות לדפדפן כדי שלחיצה על
        // השלב תוכל לסנן את רשומות הביקורת בדיוק לפי אותן פעולות.
        public List<string> Actions { get; set; } = new List<string>();

        // משתמשים ייחודיים שהשלב הצליח להם, מול הבסיס של השלב הראשון
        public int Users { get; set; }
        public int BaseUsers { get; set; }

        // מספר הפעולות שהצליחו ומספר אלה שנכשלו בשלב
        public int Runs { get; set; }
        public int Failures { get; set; }

        public double ReachPercentage =>
            BaseUsers > 0 ? Math.Round((double)Users / BaseUsers * 100, 1) : 0;
    }
}
