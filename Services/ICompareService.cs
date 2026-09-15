using System.Collections.Generic;
using System.Threading.Tasks;
using CompareD.Controllers;

namespace CompareD.Services;

// ממשק המגדיר את שירותי ההשוואה והתקשורת מול מסדי הנתונים בצורה גנרית
public interface ICompareService
{
    // הבאת טבלאות ותצוגות ממסד נתונים לפי ספק
    Task<List<DatabaseObject>> GetDatabaseObjectsAsync(string connectionString, string provider);

    // שליפת עמודות ממסד נתונים לפי ספק
    Task<List<string>> GetColumnsAsync(string connectionString, string provider, string tableName);

    // שליפת רשימת העמודות עם טיפוסי הנתונים שלהן לפי ספק
    Task<List<(string ColumnName, string DataType)>> GetColumnsWithTypesAsync(
        string connectionString, 
        string provider, 
        string tableName);

    // ביצוע השוואת סכמה בין שתי הטבלאות ובניית מודל סקירת הסכמה למסך
    Task<SchemaReviewViewModel> CompareSchemaAsync(
        string sourceConnectionString,
        string sourceProvider,
        string targetConnectionString,
        string targetProvider,
        string sourceTable,
        string targetTable);

    // שליפת נתוני תצוגה מקדימה מסוננת משני המסדים, כולל נתוני אבחון לאיתור פערי סינון
    Task<Dictionary<string, object>> GetPreviewDataAsync(
        string sourceConnectionString, string sourceProvider,
        string targetConnectionString, string targetProvider,
        string sourceTable, string targetTable,
        List<string>? sourceFields, List<string>? targetFields, List<string>? fieldRoles,
        List<string>? filterColumn, List<string>? filterOperator, List<string>? filterValue);

    // מנוע ההשוואה החכם - ביצוע השוואת הנתונים בפועל, זיהוי כפילויות, הבדלים וקיבוצם לתבניות
    Task<SmartComparisonResultViewModel> SmartCompareAsync(
        string sourceConnectionString,
        string sourceProvider,
        string targetConnectionString,
        string targetProvider,
        string sourceTable,
        string targetTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        int maxRows,
        string filterActive = "false",
        List<string>? filterColumn = null,
        List<string>? filterOperator = null,
        List<string>? filterValue = null,
        List<string>? valueKinds = null,
        List<CompositeFieldDefinition>? compositeFields = null);

    // ביצוע השוואה בזיכרון של שני סטים של נתונים (תמיכה בהשוואת קבצים ובדיקות דמי).
    // valueKinds ו-compositeFields אופציונליים ומאפשרים נרמול לפי טיפוס
    // והרכבת שדות מחושבים כאשר מבנה הנתונים אינו סימטרי בין שני הצדדים.
    SmartComparisonResultViewModel CompareInMemoryDatasets(
        List<Dictionary<string, object>> sourceRawData,
        List<Dictionary<string, object>> targetRawData,
        string sourceTable,
        string targetTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        List<string>? valueKinds = null,
        List<CompositeFieldDefinition>? compositeFields = null,
        // תקרת השורות שהתבקשה וסך השורות שהיו לפני החיתוך.
        // נדרשים כדי לדווח במפורש שההשוואה נחתכה, מפני שבמצב כזה ממצאי
        // החוסר והעודף אינם חד-משמעיים.
        int maxRowsRequested = 0,
        int sourceTotalRows = 0,
        int targetTotalRows = 0,
        // ידיעה ודאית על חיתוך, כשהקורא יודע שנחתך אך לא כמה שורות יש בסך הכול
        // (מסלול המסד שולף שורת גישוש אחת מעבר לתקרה). null = אין ידיעה כזו.
        bool? sourceTruncatedKnown = null,
        bool? targetTruncatedKnown = null);
}
