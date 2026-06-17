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

