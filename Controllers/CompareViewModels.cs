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
public class TableSelectionViewModel
{
    // רשימת האובייקטים הזמינים ב-SQL Server
    public List<DatabaseObject> SqlTables { get; set; } = new();

    // רשימת האובייקטים הזמינים ב-Oracle
    public List<DatabaseObject> OracleTables { get; set; } = new();
}

// מודל נתונים להעברת שמות עמודות לממשק מיפוי השדות
public class FieldMappingViewModel
{
    // שם טבלת המקור ב-SQL Server
    public string SqlTable { get; set; } = string.Empty;

    // שם טבלת היעד ב-Oracle
    public string OracleTable { get; set; } = string.Empty;

    // רשימת העמודות בטבלת המקור (SQL Server)
    public List<string> SqlColumns { get; set; } = new();

    // רשימת העמודות בטבלת היעד (Oracle)
    public List<string> OracleColumns { get; set; } = new();
}

// מודל תוצאות ההשוואה הסופי להצגה בדשבורד ובטבלת התוצאות
public class ComparisonResultViewModel
{
    // שם טבלת המקור (SQL Server)
    public string SqlTable { get; set; } = string.Empty;

    // שם טבלת היעד (Oracle)
    public string OracleTable { get; set; } = string.Empty;

    // כמות הרשומות שנמצאו זהות לחלוטין
    public int TotalMatched { get; set; }

    // כמות הרשומות שבהן נמצאו הבדלי נתונים
    public int TotalDifferences { get; set; }

    // כמות הרשומות שקיימות ב-SQL Server אך חסרות ב-Oracle
    public int TotalMissingInOracle { get; set; }

    // כמות הרשומות שקיימות ב-Oracle אך חסרות ב-SQL Server
    public int TotalMissingInSql { get; set; }

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
    public string SqlValue { get; set; } = string.Empty;

    // ערך השדה ב-Oracle
    public string OracleValue { get; set; } = string.Empty;

    // האם הערכים זהים
    public bool IsMatch { get; set; }
}

// מודל תצוגה עבור דף הבית המציג את רשימות פרופילי החיבור הזמינים
public class HomeViewModel
{
    // רשימת שמות הפרופילים הזמינים עבור SQL Server
    public List<string> SqlProfileNames { get; set; } = new();

    // רשימת שמות הפרופילים הזמינים עבור Oracle
    public List<string> OracleProfileNames { get; set; } = new();

    // שם הפרופיל שנבחר עבור SQL Server (לשחזור קלט במקרה של שגיאה)
    public string? SelectedSqlProfile { get; set; }

    // שם הפרופיל שנבחר עבור Oracle (לשחזור קלט במקרה של שגיאה)
    public string? SelectedOracleProfile { get; set; }
}

// הגדרת מחלקת אפשרויות למיפוי הגדרות החיבורים מתוך appsettings.json
public class DatabaseProfilesOptions
{
    // רשימת פרופילי החיבור עבור SQL Server
    public List<DatabaseProfile> SqlProfiles { get; set; } = new();

    // רשימת פרופילי החיבור עבור Oracle
    public List<DatabaseProfile> OracleProfiles { get; set; } = new();
}

// הגדרת מחלקה לייצוג פרופיל חיבור בודד
public class DatabaseProfile
{
    // שם הפרופיל (למשל SQL-Prod או Oracle-Test)
    public string Name { get; set; } = string.Empty;

    // מחרוזת החיבור (ConnectionString)
    public string ConnectionString { get; set; } = string.Empty;
}

// מחלקה המייצגת עמודה בודדת במסגרת השוואת הסכמה בין SQL Server ל-Oracle
public class ColumnSchemaInfo
{
    // שם העמודה כפי שהוא מופיע במסדי הנתונים (שם משותף או שם מהמקור)
    public string ColumnName { get; set; } = string.Empty;
    
    // טיפוס הנתונים של העמודה במסד SQL Server (למשל: int, nvarchar, datetime)
    public string SqlDataType { get; set; } = string.Empty;
    
    // טיפוס הנתונים של העמודה במסד Oracle (למשל: NUMBER, VARCHAR2, DATE)
    public string OracleDataType { get; set; } = string.Empty;
    
    // האם העמודה קיימת בשני מסדי הנתונים (על פי התאמת שם לא רגישה לרישיות)
    public bool ExistsInBoth { get; set; }
    
    // מקור העמודה: "Both" (בשניהם), "SqlOnly" (ב-SQL Server בלבד), או "OracleOnly" (ב-Oracle בלבד)
    public string Source { get; set; } = "Both";
}

// מודל תצוגה עבור מסך סקירת הסכמה המאגד את כל פרטי ההשוואה של העמודות
public class SchemaReviewViewModel
{
    // שם טבלת המקור שנבחרה ב-SQL Server
    public string SqlTable { get; set; } = string.Empty;
    
    // שם טבלת היעד שנבחרה ב-Oracle
    public string OracleTable { get; set; } = string.Empty;
    
    // האם מבנה הסכמה של שתי הטבלאות זהה לחלוטין (אין עמודות חסרות באף צד)
    public bool IsSchemaIdentical { get; set; }
    
    // רשימה מפורטת של כל העמודות משני הצדדים כולל מידע השוואתי
    public List<ColumnSchemaInfo> Columns { get; set; } = new();
    
    // עמודת המפתח הראשי שהוצעה כברירת מחדל לאחר זיהוי אוטומטי במסדי הנתונים
    public string PrimaryKeyColumn { get; set; } = string.Empty;
    
    // הגבלת כמות הרשומות המקסימלית לטעינה בעת ביצוע ההשוואה (ברירת מחדל 1000)
    public int MaxRows { get; set; } = 1000;
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
    
    // רשימת פרטי ההשוואה של השדות השונים עבור תבנית זו
    public List<FieldComparisonDetail> Fields { get; set; } = new();
}

// מחלקה לייצוג רשומת מפתח כפול (בדיקת קדם-השוואה)
public class DuplicateKeyRecord
{
    // ערך המפתח הראשי
    public string KeyValue { get; set; } = string.Empty;
    
    // מספר המופעים שנמצאו ב-SQL Server עם מפתח זה
    public int SqlCount { get; set; }
    
    // מספר המופעים שנמצאו ב-Oracle עם מפתח זה
    public int OracleCount { get; set; }
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
    
    // Column name where the gap was detected
    public string FieldName { get; set; } = string.Empty;
    
    // Which side has the real value: "Source" or "Target"
    public string PresentSide { get; set; } = string.Empty;
    
    // The actual value found on the present side
    public string PresentValue { get; set; } = string.Empty;
}

// מודל תוצאות ההשוואה החכם והסטטיסטי עבור שלב 6
public class SmartComparisonResultViewModel
{
    // שם טבלת המקור (SQL Server)
    public string SqlTable { get; set; } = string.Empty;
    
    // שם טבלת היעד (Oracle)
    public string OracleTable { get; set; } = string.Empty;
    
    // שם עמודת המפתח הראשי המשמשת להתאמת שורות
    public string PrimaryKeyColumn { get; set; } = string.Empty;
    
    // סה"כ השורות שנטענו מ-SQL Server
    public int TotalRowsInSql { get; set; }
    
    // סה"כ השורות שנטענו מ-Oracle
    public int TotalRowsInOracle { get; set; }
    
    // סה"כ השורות שנמצאו זהות לחלוטין
    public int TotalMatched { get; set; }
    
    // רשימת מפתחות שקיימים ב-SQL אך חסרים ב-Oracle (עד 50 מפתחות לדוגמה)
    public List<string> MissingInOracle { get; set; } = new();
    
    // סה"כ השורות שקיימות ב-SQL וחסרות ב-Oracle
    public int TotalMissingInOracle { get; set; }
    
    // רשימת מפתחות שקיימים ב-Oracle אך חסרים ב-SQL (עד 50 מפתחות לדוגמה)
    public List<string> MissingInSql { get; set; } = new();
    
    // סה"כ השורות שקיימות ב-Oracle וחסרות ב-SQL
    public int TotalMissingInSql { get; set; }
    
    // רשימה של כפילויות מפתח שנמצאו במסדים
    public List<DuplicateKeyRecord> Duplicates { get; set; } = new();
    
    // סה"כ השורות הכפולות שהוחרגו מההשוואה
    public int TotalDuplicates { get; set; }
    
    // רשימה של תבניות הבדלי הנתונים המקובצות
    public List<DiscrepancyPattern> DiscrepancyPatterns { get; set; } = new();
    
    // סה"כ השורות שבהן נמצאו הבדלי נתונים
    public int TotalDiscrepancyRows { get; set; }

    // === QA Workflow Properties ===
    
    // Ordered list of QA step results (A, B, C)
    public List<QaStepResult> QaSteps { get; set; } = new();
    
    // Quick flag: true if source and target row counts differ (Step A)
    public bool HasRowCountMismatch { get; set; }
    
    // Summary message for row count comparison
    public string RowCountSummary { get; set; } = string.Empty;

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
}

// מודל תצוגה עבור סקירה ומיפוי של קובצי CSV/Excel
public class FilesSchemaReviewViewModel
{
    public string SourceFileName { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = string.Empty;
    public List<string> SourceColumns { get; set; } = new();
    public List<string> TargetColumns { get; set; } = new();
}



