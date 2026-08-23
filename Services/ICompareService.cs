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

    // ביצוע השוואת הנתונים בפועל והחזרת מודל התוצאות המלא
    Task<ComparisonResultViewModel> CompareDataAsync(
        string sourceConnectionString,
        string sourceProvider,
        string targetConnectionString,
        string targetProvider,
        string sourceTable,
        string targetTable,
        string mappingMode,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        int maxRows);

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
        List<string> filterColumn = null,
        List<string> filterOperator = null,
        List<string> filterValue = null);

    // ביצוע השוואה בזיכרון של שני סטים של נתונים (תמיכה בהשוואת קבצים ובדיקות דמי)
    SmartComparisonResultViewModel CompareInMemoryDatasets(
        List<Dictionary<string, object>> sourceRawData,
        List<Dictionary<string, object>> targetRawData,
        string sourceTable,
        string targetTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles);
}
