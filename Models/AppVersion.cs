namespace CompareD.Models
{
    // =====================================================================
    // מחלקת גרסת האפליקציה - Dynamic Versioning System
    // =====================================================================
    // כיצד לעדכן גרסה לפני פרסום (Publish):
    //   - Major (X.0.0): שינוי ארכיטקטוני גדול - עדכן Major ואפס Minor ו-Patch
    //   - Minor (1.X.0): תכונה חדשה שאחורה תואמת - הגדל Minor ואפס Patch
    //   - Patch (1.1.X): תיקון באג קטן בלבד - הגדל רק Patch
    //
    // דוגמה מעשית לפני Publish:
    //   הוספת תכונה:  שנה Minor מ-1 ל-2 => "v1.2.0"
    //   תיקון באג:    שנה Patch מ-0 ל-1 => "v1.1.1"
    // =====================================================================
    public static class AppVersion
    {
        // מחרוזת הגרסה המלאה לתצוגה ב-Footer ובלוח הניהול (נשאבת מההגדרות הדינמיות)
        public static string FullVersion => $"v{SystemSettingsStore.Get().AppVersion}";

        // שם קוד של הגרסה לתצוגה שיווקית
        public static string Codename => "Aurora";
    }
}
