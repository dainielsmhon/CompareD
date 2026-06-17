using System.Collections.Generic;
using System.Threading.Tasks;
using CompareD.Controllers;

namespace CompareD.Services;

// ממשק המגדיר את שירותי ההשוואה והתקשורת מול מסדי הנתונים
public interface ICompareService
{
    // הבאת טבלאות ותצוגות מ-SQL Server
    Task<List<DatabaseObject>> GetSqlObjectsAsync(string connectionString);

    // הבאת טבלאות ותצוגות מ-Oracle
    Task<List<DatabaseObject>> GetOracleObjectsAsync(string connectionString);

    // שליפת עמודות מ-SQL Server
    Task<List<string>> GetSqlColumnsAsync(string connectionString, string tableName);

    // שליפת עמודות מ-Oracle
    Task<List<string>> GetOracleColumnsAsync(string connectionString, string tableName);

    // ביצוע השוואת הנתונים בפועל והחזרת מודל התוצאות המלא
    Task<ComparisonResultViewModel> CompareDataAsync(
        string sqlConnectionString,
        string oracleConnectionString,
        string sqlTable,
        string oracleTable,
        string mappingMode,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        int maxRows);

    // שליפת רשימת העמודות עם טיפוסי הנתונים שלהן מ-SQL Server
    Task<List<(string ColumnName, string DataType)>> GetSqlColumnsWithTypesAsync(
        string connectionString, 
        string tableName);

    // שליפת רשימת העמודות עם טיפוסי הנתונים שלהן מ-Oracle
    Task<List<(string ColumnName, string DataType)>> GetOracleColumnsWithTypesAsync(
        string connectionString, 
        string tableName);

    // ביצוע השוואת סכמה בין שתי הטבלאות ובניית מודל סקירת הסכמה למסך
    Task<SchemaReviewViewModel> CompareSchemaAsync(
        string sqlConnectionString, 
        string oracleConnectionString, 
        string sqlTable, 
        string oracleTable);
}

