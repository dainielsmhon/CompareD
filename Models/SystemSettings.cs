using System;
using System.IO;
using System.Text.Json;

namespace CompareD.Models
{
    // =====================================================================
    // מודל הגדרות המערכת הדינמיות - ניתן לעדכן ב-Runtime ללא שינוי קבצי תצורה
    // =====================================================================
    public class SystemSettings
    {
        // מגבלת גודל קובץ העלאה מקסימלית (מגה-בייט) - ברירת מחדל: 50
        public int MaxFileUploadMb { get; set; } = 50;

        // האם רישום פעולות Audit Log מופעל
        public bool IsAuditLoggingEnabled { get; set; } = true;

        // האם רישום שגיאות מערכת מופעל
        public bool IsErrorLoggingEnabled { get; set; } = true;

        // האם ניטור בריאות המערכת מופעל
        public bool IsHealthMonitorEnabled { get; set; } = true;

        // הודעת אזהרה מותאמת אישית שתוצג למשתמשים (ריקה = ללא הודעה)
        public string SystemAlertMessage { get; set; } = string.Empty;

        // שם מנהל המערכת לתצוגה בדף יצירת קשר
        public string AdminContactName { get; set; } = "דניאל שמחון";

        // תאריך ושעת עדכון אחרון של ההגדרות
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        // שם המשתמש שביצע את העדכון האחרון
        public string LastUpdatedBy { get; set; } = string.Empty;

        // גרסת המערכת הניתנת לעריכה ממסך הניהול (ללא צורך בקימפול מחדש)
        public string AppVersion { get; set; } = "2.0.0";
    }

    // =====================================================================
    // מחלקת ניהול הגדרות המערכת - שמירה ושליפה מקובץ JSON מקומי
    // =====================================================================
    public static class SystemSettingsStore
    {
        private static readonly string DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        private static readonly string SettingsPath = Path.Combine(DataDirectory, "system_settings.json");
        private static readonly object SettingsLock = new object();

        // שליפת הגדרות המערכת הנוכחיות
        public static SystemSettings Get()
        {
            lock (SettingsLock)
            {
                try
                {
                    if (!File.Exists(SettingsPath))
                        return new SystemSettings();

                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<SystemSettings>(json) ?? new SystemSettings();
                }
                catch
                {
                    // אם הקובץ פגום - מחזירים ברירת מחדל
                    return new SystemSettings();
                }
            }
        }

        // שמירת הגדרות מעודכנות לקובץ
        public static bool Save(SystemSettings settings)
        {
            lock (SettingsLock)
            {
                try
                {
                    // יצירת תיקיית data אם לא קיימת
                    if (!Directory.Exists(DataDirectory))
                        Directory.CreateDirectory(DataDirectory);

                    settings.LastUpdated = DateTime.Now;
                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(SettingsPath, json);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    // =====================================================================
    // ViewModel לדאשבורד האנליטיקה של לוח הניהול
    // =====================================================================
    public class AdminDashboardViewModel
    {
        // אחוז ההצלחה של השוואות במערכת
        public double SuccessRate { get; set; }

        // מספר הפעולות הכולל שנרשם
        public int TotalActions { get; set; }

        // מספר פעולות מוצלחות
        public int SuccessCount { get; set; }

        // מספר פעולות שנכשלו
        public int FailedCount { get; set; }

        // רשימת המשתמשים הפעילים לאחרונה
        public System.Collections.Generic.List<string> RecentActiveUsers { get; set; } = new();

        // זמן עיבוד ממוצע של השוואות (מילישניות)
        public double AvgProcessingTimeMs { get; set; }

        // כמות מדידות הבריאות שנרשמו
        public int HealthSamplesCount { get; set; }

        // רשימת המשתמשים הרשומים
        public System.Collections.Generic.List<UserInfo> Users { get; set; } = new();

        // הגדרות מערכת נוכחיות
        public SystemSettings Settings { get; set; } = new();

        // גרסת האפליקציה (נלקחת מהגדרות המערכת הדינמיות)
        public string AppVersion { get; set; } = string.Empty;
    }
}
