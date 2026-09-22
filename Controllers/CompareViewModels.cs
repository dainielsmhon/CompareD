using System.Collections.Generic;

namespace CompareD.Controllers;

// הגדרת מחלקה לייצוג אובייקט במסד הנתונים (טבלה או תצוגה)
public class DatabaseObject
{
    // שם הטבלה או התצוגה
    public string Name { get; set; } = string.Empty;

    // סוג האובייקט - TABLE או VIEW
    public string Type { get; set; } = string.Empty;
}

// מודל נתונים להצגת ובחירת טבלאות/תצוגות בין SQL Server ל-Oracle
// שמות הספקים לתצוגה.
//
// המשתמש בוחר לכל צד את הספק שלו, ולכן "מקור" אינו בהכרח SQL Server ו"יעד"
// אינו בהכרח Oracle. עד כה השמות היו קבועים בתוך המסכים, וכל השוואה בכיוון
// ההפוך (Oracle כמקור) הציגה לאורך כל המסלול תווית הפוכה מהמציאות.
public static class ProviderDisplay
{
    public static string Name(string? provider) => provider switch
    {
        "SQLServer" => "SQL Server",
        "Oracle" => "Oracle",
        _ => string.IsNullOrWhiteSpace(provider) ? "מסד נתונים" : provider!
    };
}

public class TableSelectionViewModel
{
    // רשימת האובייקטים הזמינים בצד המקור
    public List<DatabaseObject> SourceTables { get; set; } = new();

    // רשימת האובייקטים הזמינים בצד היעד
    public List<DatabaseObject> TargetTables { get; set; } = new();

    // שם הספק של כל צד לתצוגה, לפי בחירת המשתמש במסך החיבור
    public string SourceProviderName { get; set; } = "SQL Server";
    public string TargetProviderName { get; set; } = "Oracle";
}

// מודל נתונים להעברת שמות עמודות לממשק מיפוי השדות
public class FieldMappingViewModel
{
    // שם טבלת המקור ב-SQL Server
    public string SourceTable { get; set; } = string.Empty;

    // שם טבלת היעד ב-Oracle
    public string TargetTable { get; set; } = string.Empty;

    // רשימת העמודות בטבלת המקור (SQL Server)
    public List<string> SourceColumns { get; set; } = new();

    // רשימת העמודות בטבלת היעד (Oracle)
    public List<string> TargetColumns { get; set; } = new();
}

// מודל תוצאות ההשוואה הסופי להצגה בדשבורד ובטבלת התוצאות
public class ComparisonResultViewModel
{
    // שם טבלת המקור (SQL Server)
    public string SourceTable { get; set; } = string.Empty;

    // שם טבלת היעד (Oracle)
    public string TargetTable { get; set; } = string.Empty;

    // כמות הרשומות שנמצאו זהות לחלוטין
    public int TotalMatched { get; set; }

    // כמות הרשומות שבהן נמצאו הבדלי נתונים
    public int TotalDifferences { get; set; }

    // כמות הרשומות שקיימות ב-SQL Server אך חסרות ב-Oracle
    public int TotalMissingInTarget { get; set; }

    // כמות הרשומות שקיימות ב-Oracle אך חסרות ב-SQL Server
    public int TotalMissingInSource { get; set; }

    // רשימת פרטי ההבדלים של השורות הלא תואמות
    public List<ComparisonRowDetail> Details { get; set; } = new();
}

// הגדרת מחלקה לייצוג פרטי השורה הלא תואמת
public class ComparisonRowDetail
{
    // ערך מפתח השורה (או מפתחות מורכבים המחוברים ב-|)
    public string KeyValue { get; set; } = string.Empty;

    // סטטוס אי ההתאמה (Difference, MissingInOracle, MissingInSql)
    public string Status { get; set; } = string.Empty;

    // רשימת ההשוואות ברמת השדה הבודד בשורה זו
    public List<FieldComparisonDetail> Fields { get; set; } = new();
}

// הגדרת מחלקה לייצוג השוואת שדה בודד
public class FieldComparisonDetail
{
    // שם השדה
    public string FieldName { get; set; } = string.Empty;

    // ערך השדה ב-SQL Server
    public string SourceValue { get; set; } = string.Empty;

    // ערך השדה ב-Oracle
    public string TargetValue { get; set; } = string.Empty;

    // האם הערכים זהים
    public bool IsMatch { get; set; }
}

// מודל תצוגה של דף הבית - נשמר מינימלי; לטופס החיבור הדינמי אין מודל בצד השרת
public class HomeViewModel
{
    // הודעת שגיאה אופציונלית להצגה בדף הבית (מתוך TempData)
    public string? ErrorMessage { get; set; }
}

// פרטי חיבור דינמיים המוגשים דרך ממשק המשתמש בזמן ריצה
// מחליף את הפרופילים הסטטיים ב-appsettings כדי למנוע חשיפת פרטי הזדהות על הדיסק
public class DynamicConnectionViewModel
{
    // שם השרת, כתובת ה-IP או ה-DSN עבור החיבור
    public string Server { get; set; } = string.Empty;

    // שם מסד הנתונים (SQL Server) או שם הסכימה/השירות (Oracle)
    public string Database { get; set; } = string.Empty;

    // שם המשתמש להתחברות
    public string Username { get; set; } = string.Empty;

    // סיסמת ההתחברות - לעולם אינה נשמרת על הדיסק
    public string Password { get; set; } = string.Empty;
}

// מחלקה המייצגת עמודה בודדת במסגרת השוואת הסכמה בין SQL Server ל-Oracle
public class ColumnSchemaInfo
{
    // שם העמודה כפי שהוא מופיע במסדי הנתונים (שם משותף או שם מהמקור)
    public string ColumnName { get; set; } = string.Empty;
    
    // טיפוס הנתונים של העמודה במסד SQL Server (למשל: int, nvarchar, datetime)
    public string SourceDataType { get; set; } = string.Empty;
    
    // טיפוס הנתונים של העמודה במסד Oracle (למשל: NUMBER, VARCHAR2, DATE)
    public string TargetDataType { get; set; } = string.Empty;
    
    // טיפוס ההשוואה המוצע לעמודה, נגזר מטיפוסי הנתונים בשני המסדים.
    // "Number", "Date" או "Text". משמש כברירת מחדל בבורר שבמסך המיפוי,
    // כדי שלא יידרש לסמן ידנית כל עמודה בכל השוואה.
    public string SuggestedKind { get; set; } = "Text";

    // האם העמודה קיימת בשני מסדי הנתונים (על פי התאמת שם לא רגישה לרישיות)
    public bool ExistsInBoth { get; set; }
    
    // מקור העמודה: "Both" (בשניהם), "SqlOnly" (ב-SQL Server בלבד), או "OracleOnly" (ב-Oracle בלבד)
    public string Source { get; set; } = "Both";
}

// מודל תצוגה עבור מסך סקירת הסכמה המאגד את כל פרטי ההשוואה של העמודות
public class SchemaReviewViewModel
{
    // שם טבלת המקור שנבחרה ב-SQL Server
    public string SourceTable { get; set; } = string.Empty;
    
    // שם טבלת היעד שנבחרה ב-Oracle
    public string TargetTable { get; set; } = string.Empty;
    
    // האם מבנה הסכמה של שתי הטבלאות זהה לחלוטין (אין עמודות חסרות באף צד)
    public bool IsSchemaIdentical { get; set; }
    
    // רשימה מפורטת של כל העמודות משני הצדדים כולל מידע השוואתי
    public List<ColumnSchemaInfo> Columns { get; set; } = new();
    
    // עמודת המפתח הראשי שהוצעה כברירת מחדל לאחר זיהוי אוטומטי במסדי הנתונים
    public string PrimaryKeyColumn { get; set; } = string.Empty;
    
    // הגבלת כמות הרשומות המקסימלית לטעינה בעת ביצוע ההשוואה (ברירת מחדל 1000)
    // תקרת ביטחון לקבצים וטבלאות גדולים, לא יעד. ברירת המחדל הייתה 1000,
    // וכשטבלה הכילה יותר מזה ההשוואה נחתכה בשקט ודיווחה חוסרים מדומים.
    public int MaxRows { get; set; } = 10000;

    // שם הספק של כל צד לתצוגה, לפי בחירת המשתמש במסך החיבור
    public string SourceProviderName { get; set; } = "SQL Server";
    public string TargetProviderName { get; set; } = "Oracle";
}

// מחלקה לייצוג תבנית של הבדל נתונים מקובץ (Discrepancy Pattern)
public class DiscrepancyPattern
{
    // תיאור התבנית (באילו שדות נמצאו הבדלים, למשל: "NAME, AGE")
    public string PatternDescription { get; set; } = string.Empty;
    
    // סה"כ השורות שנמצאו השייכות לתבנית הבדל זו
    public int Count { get; set; }
    
    // רשימה של עד 4 מפתחות לדוגמה השייכים לתבנית הבדל זו
    public List<string> ExampleKeys { get; set; } = new();

    // עד 4 שורות לדוגמה, כל אחת עם המפתח המלא שלה והערכים השונים שנמצאו בה
    public List<DiscrepancyExample> Examples { get; set; } = new();
    
    // רשימת פרטי ההשוואה של השדות השונים עבור תבנית זו
    public List<FieldComparisonDetail> Fields { get; set; } = new();
}

// ממצא של שורה שקיימת בצד אחד וחסרה בצד השני, כולל אבחון השדה שגרם לאי-ההתאמה.
// בלי האבחון הזה דוח החוסרים מציג מספר דף בלבד, והמשתמש נדרש לחפש ידנית
// בין כל המופעים של אותו דף באיזו שורה בדיוק מדובר.
public class MissingRowDetail
{
    // תיאור המפתח המלא עם שמות העמודות (למשל "DAF=41 | DAF_NOSAF=1 | YOM=2 | HODESH=9 | SHANA=2025")
    public string KeyValue { get; set; } = string.Empty;

    // ערכי רכיבי המפתח לפי הסדר של KeyColumnNames, כל רכיב בנפרד.
    // זה מה שמאפשר להציג את הממצא כטבלה עם עמודה לכל רכיב (שנה, חודש, יום,
    // דף, דף נוסף) במקום מחרוזת אחת ארוכה שצריך לפרק בעיניים.
    public List<string> KeyParts { get; set; } = new();

    // שם רכיב המפתח שנמצא שונה מול השורה הדומה בצד השני. ריק אם לא נמצאה שורה דומה.
    public string DiffFieldName { get; set; } = string.Empty;

    // ערך רכיב המפתח השונה בצד המקור
    public string SourceValue { get; set; } = string.Empty;

    // ערך רכיב המפתח השונה בצד היעד
    public string TargetValue { get; set; } = string.Empty;

    // תיאור המפתח המלא של השורה הדומה שנמצאה בצד השני
    public string CounterpartKeyValue { get; set; } = string.Empty;

    // כמה מרכיבי המפתח תאמו לשורה הקרובה ביותר שנמצאה בצד השני, מתוך כמה.
    // "אין שורה דומה" הוא ממצא חלש: הוא לא מבדיל בין שורה שנבדלת ברכיב אחד
    // לבין שורה שאין לה שום קשר. המספרים האלה הופכים אותו למדד.
    public int KeyPartsMatched { get; set; }
    public int KeyPartsTotal { get; set; }

    // מספר השורה בנתונים שנטענו - מאפשר לאתר את השורה בקובץ המקורי
    public int RowNumber { get; set; }

    // סוג החוסר: KeyAbsent (המפתח אינו קיים כלל בצד השני)
    // או OccurrenceShortfall (המפתח קיים אך במספר מופעים קטן יותר)
    public string MissingKind { get; set; } = "KeyAbsent";

    // הכיתוב בעברית של סוג החוסר
    public string MissingKindTitle { get; set; } = string.Empty;

    // מספר המופעים של המפתח בכל צד
    public int SourceOccurrences { get; set; }
    public int TargetOccurrences { get; set; }

    // הערכים שמבדילים את המופע הזה משאר המופעים של אותו מפתח.
    // ריק כשהמפתח מופיע פעם אחת בלבד, ואז המפתח עצמו מזהה את השורה.
    public string DistinguishingValues { get; set; } = string.Empty;
}

// ממצא בודד ברמת שורה ושדה: השורה קיימת בשני הצדדים, אך שדה מסוים נושא
// ערך שונה. זו היחידה שהמשתמש מבקש בפועל - "באותו יום, תאריך ודף, בשדה
// עובד יש מספר שונה, וזה המספר" - ולכן היא מדווחת כשורה שטוחה אחת ולא
// כדוגמה בתוך תבנית שצריך לפתוח.
public class RowDiffDetail
{
    // ערכי רכיבי המפתח לפי הסדר של KeyColumnNames
    public List<string> KeyParts { get; set; } = new();

    // תיאור המפתח המלא, כנפילה לאחור לייצוא ולחלונות פירוט
    public string KeyValue { get; set; } = string.Empty;

    // שם השדה שנמצא שונה (שם המקור; אם שם היעד שונה הוא מופיע אחריו)
    public string FieldName { get; set; } = string.Empty;

    // הערך בשני הצדדים
    public string SourceValue { get; set; } = string.Empty;
    public string TargetValue { get; set; } = string.Empty;

    // מספרי השורות בנתונים שנטענו, לאיתור בקובץ המקורי
    public int SourceRowNumber { get; set; }
    public int TargetRowNumber { get; set; }

    // האם השדה סווג כהבדל שיטתי (שונה ברוב מוחלט של השורות). מסומן כדי
    // שאפשר יהיה להשתיק את הרעש הזה ולהשאיר את הממצאים האמיתיים.
    public bool IsSystematicField { get; set; }
}

// שורה בודדת לדוגמה בתוך תבנית הבדלי נתונים, עם המפתח המלא שלה
// והערכים שנמצאו שונים בה בפועל (ולא ערכים מהשורה הראשונה בתבנית).
public class DiscrepancyExample
{
    // תיאור המפתח המלא של השורה עם שמות העמודות
    public string KeyValue { get; set; } = string.Empty;

    // השדות שנמצאו שונים בשורה זו
    public List<FieldComparisonDetail> Fields { get; set; } = new();
}

// מחלקה לייצוג רשומת מפתח כפול (בדיקת קדם-השוואה)
public class DuplicateKeyRecord
{
    // ערך המפתח הראשי
    public string KeyValue { get; set; } = string.Empty;

    // ערכי רכיבי המפתח לפי הסדר של KeyColumnNames, עמודה לכל רכיב
    public List<string> KeyParts { get; set; } = new();
    
    // מספר המופעים שנמצאו ב-SQL Server עם מפתח זה
    public int SourceCount { get; set; }
    
    // מספר המופעים שנמצאו ב-Oracle עם מפתח זה
    public int TargetCount { get; set; }

    // סוג הכפילות: IdenticalRowRepeat (שורה זהה ממש) או SameKeyDifferentData
    public string Kind { get; set; } = string.Empty;

    // הכיתוב בעברית של סוג הכפילות, לתצוגה בדוח
    public string KindTitle { get; set; } = string.Empty;

    // חומרת הממצא: Fail או Warning
    public string Severity { get; set; } = string.Empty;

    // האם מספר המופעים זהה בשני הצדדים
    public bool CountsBalanced { get; set; }

    // האם נמצאה שורה זהה לחלוטין שחוזרת באותו צד
    public bool HasIdenticalRepeat { get; set; }

    // מספר השורות השונות בפועל (חתימות ייחודיות) בכל צד
    public int SourceDistinctRows { get; set; }
    public int TargetDistinctRows { get; set; }

    // השדות שנושאים ערכים שונים בין המופעים של אותו מפתח - מה שמאפשר להבדיל ביניהם
    public string DistinguishingFields { get; set; } = string.Empty;
}

// ערך של רכיב מפתח שקיים בצד אחד ואינו קיים כלל בצד השני - למשל יום שלם
// שיוצא בקובץ אחד ולא בשני.
//
// ממצא כזה כבר מופיע בפירוט כעשרות או מאות שורות חסרות, אבל שם הוא נראה
// כרשימה ארוכה של שורות ולא כחור אחד גדול. הסיכום הזה הוא מה שהופך אותו
// לדבר שאי אפשר לפספס.
public class KeyValueGapNote
{
    // שם רכיב המפתח (למשל YOM)
    public string FieldName { get; set; } = string.Empty;

    // הצד שבו הערכים קיימים: "Source" או "Target"
    public string PresentIn { get; set; } = string.Empty;

    // כמה ערכים שונים חד-צדדיים נמצאו ברכיב הזה
    public int ValueCount { get; set; }

    // סך השורות המושפעות
    public int RowCount { get; set; }

    // הערכים עצמם, עד תקרת תצוגה
    public List<string> Values { get; set; } = new();

    // כמה ערכים שונים יש לרכיב הזה בסך הכול, בשני הצדדים יחד.
    // מאפשר להבדיל בין "יום אחד מתוך חמישה חסר" לבין רכיב בעל אלפי ערכים
    // שבו חוסר של ערך בודד אינו חור בנתונים אלא שורה חסרה רגילה.
    public int DistinctValuesInField { get; set; }
}

// QA step result representing the outcome of a single validation step
public class QaStepResult
{
    // Step identifier ("A", "B", "C")
    public string StepName { get; set; } = string.Empty;
    
    // Display title in Hebrew
    public string StepTitle { get; set; } = string.Empty;
    
    // Status: "Pass", "Warning", "Fail"
    public string Status { get; set; } = "Pass";
    
    // Summary message describing the step outcome
    public string Summary { get; set; } = string.Empty;
}

// Data integrity gap: value exists on one side but is zero/null/empty on the other
public class DataIntegrityGap
{
    // Primary key value of the affected row
    public string KeyValue { get; set; } = string.Empty;

    // ערכי רכיבי המפתח לפי הסדר של KeyColumnNames, עמודה לכל רכיב
    public List<string> KeyParts { get; set; } = new();
    
    // Column name where the gap was detected
    public string FieldName { get; set; } = string.Empty;
    
    // Which side has the real value: "Source" or "Target"
    public string PresentSide { get; set; } = string.Empty;
    
    // The actual value found on the present side
    public string PresentValue { get; set; } = string.Empty;
}

// פרופיל הפערים של שדה בודד: בכמה שורות הוא נמצא שונה, ודוגמה.
// הופך "2428 שורות שונות" לתשובה: אילו שדות אשמים, ומי מהם שיטתי.
public class FieldGapProfile
{
    // שם השדה כפי שיוצג
    public string FieldName { get; set; } = string.Empty;

    // מספר השורות שבהן השדה נמצא שונה
    public int DiffCount { get; set; }

    // אחוז מתוך השורות שנמצאו שונות
    public double DiffPercentage { get; set; }

    // דוגמה לערכים משני הצדדים
    public string ExampleSourceValue { get; set; } = string.Empty;
    public string ExampleTargetValue { get; set; } = string.Empty;

    // שדה שנמצא שונה ברוב מוחלט של השורות. הבדל כזה הוא כמעט תמיד
    // הבדל שיטתי בין המסדים (עמודת שירות, פורמט תאריך) ולא פער נתונים
    // אמיתי, ולכן הוא מוצג בנפרד כדי שלא יטביע את הממצאים האמיתיים.
    public bool IsSystematic { get; set; }
}

// מודל תוצאות ההשוואה החכם והסטטיסטי עבור שלב 6
public class SmartComparisonResultViewModel
{
    // מצב הבדיקה שהפיק את הדוח: השוואת מסדים חיה או השוואת קבצים.
    //
    // הדוח משותף לשני המסלולים, ובלי הדגל הזה הוא הציג לשניהם את אותו מחוון
    // שלבים ואת אותו ניסוח - כך שמי שהשווה קבצים ראה "✓ חיבור ✓ בחירת טבלה"
    // על שלבים שלא היו, ומי שהשווה טבלאות קרא על "קבצים".
    public bool IsDatabaseComparison { get; set; }

    // שם צד לתצוגה: "קובץ" בהשוואת קבצים, "טבלה" בהשוואת מסדים
    public string SideNoun => IsDatabaseComparison ? "טבלה" : "קובץ";

    // שם הספק של כל צד, רלוונטי בהשוואת מסדים בלבד
    public string SourceProviderName { get; set; } = string.Empty;
    public string TargetProviderName { get; set; } = string.Empty;

    // שם טבלת/קובץ המקור
    public string SourceTable { get; set; } = string.Empty;
    
    // שם טבלת/קובץ היעד
    public string TargetTable { get; set; } = string.Empty;
    
    // שם עמודת המפתח הראשי המשמשת להתאמת שורות
    public string PrimaryKeyColumn { get; set; } = string.Empty;

    // שמות רכיבי המפתח לפי סדרם. כל ממצא בדוח נושא את הערכים שלו באותו סדר,
    // וכך כל טבלת ממצאים מוצגת עם עמודה לכל רכיב מפתח.
    public List<string> KeyColumnNames { get; set; } = new();
    
    // סה"כ השורות שנטענו מ-SQL Server
    public int TotalRowsInSource { get; set; }
    
    // סה"כ השורות שנטענו מ-Oracle
    public int TotalRowsInTarget { get; set; }
    
    // סה"כ השורות שנמצאו זהות לחלוטין
    public int TotalMatched { get; set; }
    
    // רשימת מפתחות שקיימים ב-SQL אך חסרים ב-Oracle (עד 50 מפתחות לדוגמה)
    public List<string> MissingInTarget { get; set; } = new();
    
    // סה"כ השורות שקיימות ב-SQL וחסרות ב-Oracle
    public int TotalMissingInTarget { get; set; }

    // פירוט השורות החסרות ביעד, כולל אבחון רכיב המפתח שגרם לאי-ההתאמה (עד 50 שורות)
    public List<MissingRowDetail> MissingInTargetDetails { get; set; } = new();
    
    // רשימת מפתחות שקיימים ב-Oracle אך חסרים ב-SQL (עד 50 מפתחות לדוגמה)
    public List<string> MissingInSource { get; set; } = new();
    
    // סה"כ השורות שקיימות ב-Oracle וחסרות ב-SQL
    public int TotalMissingInSource { get; set; }

    // פירוט השורות החסרות במקור, כולל אבחון רכיב המפתח שגרם לאי-ההתאמה (עד 50 שורות)
    public List<MissingRowDetail> MissingInSourceDetails { get; set; } = new();
    
    // רשימה של כפילויות מפתח שנמצאו במסדים
    public List<DuplicateKeyRecord> Duplicates { get; set; } = new();
    
    // מספר ממצאי הכפילות (מפתחות חוזרים). שורות כפולות אינן מוחרגות יותר מההשוואה.
    public int TotalDuplicates { get; set; }

    // מספר ממצאי הכפילות בפועל, גם כשרשימת הפירוט נחתכה
    public int TotalDuplicateFindings { get; set; }

    // סך השורות המושפעות מכפילות, בשני הצדדים יחד
    public int TotalDuplicateRows { get; set; }
    
    // רשימה של תבניות הבדלי הנתונים המקובצות
    public List<DiscrepancyPattern> DiscrepancyPatterns { get; set; } = new();
    
    // סה"כ השורות שבהן נמצאו הבדלי נתונים
    public int TotalDiscrepancyRows { get; set; }

    // פירוט שטוח של כל ההבדלים: שורה, שדה, ערך במקור מול ערך ביעד.
    // התבניות מסכמות "כמה" ו"באילו שדות"; זו הרשימה שעונה "באיזו שורה בדיוק".
    public List<RowDiffDetail> DiscrepancyRows { get; set; } = new();

    // מספר ההבדלים ברמת שדה בפועל, גם כשרשימת הפירוט נחתכה
    public int TotalDiscrepancyFindings { get; set; }

    // תקרת הפירוט שהופעלה על הרשימות בדוח. מוצגת למשתמש כשהיא נחצתה,
    // כדי שלא ייווצר רושם שהרשימה שלמה.
    public int DetailRowCap { get; set; }

    // === QA Workflow Properties ===
    
    // Ordered list of QA step results (A, B, C)
    public List<QaStepResult> QaSteps { get; set; } = new();
    
    // Quick flag: true if source and target row counts differ (Step A)
    public bool HasRowCountMismatch { get; set; }
    
    // Summary message for row count comparison
    public string RowCountSummary { get; set; } = string.Empty;

    // === מדדי התאמת מופעים ===

    // שורות שכל רכיבי המפתח שלהן ריקים, שהוחרגו מההשוואה.
    // נובעות ממחיקה ידנית של שורות בגיליון או מייצוא שמשאיר שורות מעוצבות וריקות.
    public int SourceEmptyKeyRows { get; set; }
    public int TargetEmptyKeyRows { get; set; }

    // ערכי מפתח שלמים שקיימים בצד אחד ואינם קיימים כלל בשני (יום שלם, חודש שלם).
    // מוצגים כהערה בראש הדוח, כי זה הממצא שקל ביותר לפספס בתוך רשימת שורות.
    public List<KeyValueGapNote> KeyValueGaps { get; set; } = new();

    // שורות שהוסתרו בגיליון (ידנית או בסינון אוטומטי) ובכל זאת נכללו בהשוואה.
    // הסתרה אינה מחיקה: השורה נשארת בקובץ וכל קורא xlsx מחזיר אותה. בלי
    // הדיווח הזה, קובץ שהוסתר בו יום שלם מייצר עשרות "שורות חסרות" מדומות
    // מול הקובץ השני, בלי שום רמז לסיבה.
    public int SourceHiddenRows { get; set; }
    public int TargetHiddenRows { get; set; }

    // האם המשתמש ביקש להחריג את השורות המוסתרות מההשוואה
    public bool HiddenRowsExcluded { get; set; }

    // קבוצות מפתח שבהן הזיווג בין המופעים בוצע לפי סדר במקום לפי דמיון,
    // בגלל ריבוי חריג. מדווח כדי שלא ייווצר רושם של דיוק שאינו קיים.
    public int PairingDegradedGroups { get; set; }

    // זוגות מופעים שהותאמו בין שני הצדדים - סכום min(מקור, יעד) לכל מפתח.
    // שווה ל-TotalMatched + TotalDiscrepancyRows.
    public int TotalPairedOccurrences { get; set; }

    // מפתחות שאינם קיימים כלל בצד השני (בשונה ממפתח שקיים אך במספר מופעים קטן יותר)
    public int KeysMissingInTarget { get; set; }
    public int KeysMissingInSource { get; set; }

    // מופעים חסרים במפתחות שכן קיימים בשני הצדדים
    public int OccurrenceShortfallInTarget { get; set; }
    public int OccurrenceShortfallInSource { get; set; }

    // אחוזים מחושבים בשרת פעם אחת, כדי שהמסך והייצוא לא יחשבו כל אחד לחוד ויסתרו
    public double MatchPercentage { get; set; }
    public double CoveragePercentage { get; set; }

    // שורת מאזן: האם סך השורות מתפרק בדיוק לזוגות זהים, זוגות שונים ועודפים
    public bool IsInternallyConsistent { get; set; }
    public string ReconciliationLine { get; set; } = string.Empty;

    // === שורה תחתונה ===
    // סיכום קצר בראש הדוח. בלעדיו הדוח היה 2000 שורות של פירוט בלי תשובה,
    // והמשתמש נדרש לקרוא הכל כדי להבין אם המיגרציה תקינה.
    public string BottomLine { get; set; } = string.Empty;

    // פירוק ההבדלים לפי שדה, מהשכיח לנדיר
    public List<FieldGapProfile> FieldGapProfiles { get; set; } = new();

    // מספר השדות שנמצאו שונים ברוב מוחלט של השורות (הבדל שיטתי)
    public int SystematicFieldCount { get; set; }

    // === חיתוך מספר שורות ===
    // כשההשוואה נחתכה, ממצאי החוסר והעודף אינם חד-משמעיים: שורה יכולה להיראות
    // חסרה רק מפני שנפלה מחוץ לחלון. עד כה הדוח לא אמר זאת, והתוצאה הייתה
    // דיווח סימטרי של מאות חוסרים מדומים.
    public int MaxRowsRequested { get; set; }
    public bool SourceTruncated { get; set; }
    public bool TargetTruncated { get; set; }
    public int SourceTotalRows { get; set; }
    public int TargetTotalRows { get; set; }
    public string TruncationWarning { get; set; } = string.Empty;

    // === QA Step A Detailed Metrics ===
    public int SourceRawCount { get; set; }
    public int TargetRawCount { get; set; }
    public int SourceUniqueValidKeys { get; set; }
    public int TargetUniqueValidKeys { get; set; }
    public int SourceDuplicateKeysCount { get; set; }
    public int TargetDuplicateKeysCount { get; set; }
    public int SourceMissingKeysCount { get; set; }
    public int TargetMissingKeysCount { get; set; }
    public List<string> SourceDuplicateKeysList { get; set; } = new();
    public List<string> TargetDuplicateKeysList { get; set; } = new();
    public List<string> SourceMissingKeysList { get; set; } = new();
    public List<string> TargetMissingKeysList { get; set; } = new();
    
    // List of data integrity gaps (value vs zero/null/empty)
    public List<DataIntegrityGap> DataIntegrityGaps { get; set; } = new();

    // חוסר התאמה בכמויות בין הצדדים מדווח כיום ברמת המופע, בשדות
    // OccurrenceShortfallInTarget / InSource ובפירוט המפתחות החוזרים.
    // המבנה הקודם היה מקובע לעמודות DAF ו-DAF_NOSAF, חושב בכל הרצה בשני
    // מעברי GroupBy על כל הנתונים, ולא הוצג באף מקום - ולכן הוסר.
}

// מבנה לייצוג פער בריבוי עבור צמד שדות (DAF, DAF_NOSAF)
// (היסטורית) מחלקת פערי ריבוי הוסרה - ניהול ריבויים נעשה במקום אחר במערכת

// הגדרת שדה מחושב (מורכב) המאחד מספר עמודות לערך אחד בר-השוואה.
// נדרש כאשר מבנה הנתונים אינו סימטרי בין שני המסדים - למשל כאשר צד אחד
// שומר שנה, חודש ויום בשלושה שדות נפרדים והצד השני שומר שדה תאריך אחד.
// ללא זה כל השורות היו מדווחות כחסרות, כי המפתחות אינם ניתנים להשוואה.
public class CompositeFieldDefinition
{
    // שם השדה הלוגי כפי שיוצג בדוח התוצאות (למשל "תאריך")
    public string LogicalName { get; set; } = string.Empty;

    // העמודות בצד המקור המרכיבות את הערך (עמודה אחת, או שלוש בסדר שנה/חודש/יום)
    public List<string> SourceColumns { get; set; } = new();

    // אופן ההרכבה בצד המקור: DateParts (שנה/חודש/יום), Date (שדה תאריך אחד), Concat (שרשור)
    public string SourceKind { get; set; } = "DateParts";

    // העמודות בצד היעד המרכיבות את הערך
    public List<string> TargetColumns { get; set; } = new();

    // אופן ההרכבה בצד היעד
    public string TargetKind { get; set; } = "Date";

    // תפקיד השדה המחושב בהשוואה: Key (מפתח התאמה) או Compare (שדה להשוואה)
    public string Role { get; set; } = "Key";
}

// מודל תצוגה עבור סקירה ומיפוי של קובצי CSV/Excel
public class FilesSchemaReviewViewModel
{
    public string SourceFileName { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = string.Empty;

    // שורות שהוסתרו בגיליון. מוצגות במסך המיפוי כדי שההחלטה אם להחריג אותן
    // תתקבל מראש, ולא אחרי דוח שמלא בחוסרים שמקורם בהן.
    public int SourceHiddenRows { get; set; }
    public int TargetHiddenRows { get; set; }
    public List<string> SourceColumns { get; set; } = new();
    public List<string> TargetColumns { get; set; } = new();

    // טיפוס ההשוואה המוצע לכל עמודת מקור, לפי מדגם ערכים מהקובץ.
    // לקובץ אין סכימה, ולכן הטיפוס נלמד מהנתונים עצמם ולא ממטא-דאטה.
    public Dictionary<string, string> SuggestedKinds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // מדגם הערכים של עמודות המקור, מיושר לפי שורות, לבדיקת ייחודיות
    // צירוף המפתח עוד במסך המיפוי. מפתח שאינו ייחודי מייצר דוח שכולו
    // "הבדלים" מדומים, וללא המדגם הזה הוא התגלה רק אחרי הרצה מלאה.
    public Dictionary<string, List<string>> SourceSampleValues { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // מספר השורות שנכללו במדגם - הכיתוב במסך אומר על כמה שורות נבדק
    public int SourceSampleRows { get; set; }
}



