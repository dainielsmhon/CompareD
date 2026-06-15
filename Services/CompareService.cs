using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompareD.Controllers;

namespace CompareD.Services;

// מימוש של שירות ההשוואה המרכז את כל הלוגיקה העסקית והחיבורים למסדי הנתונים
public class CompareService : ICompareService
{
    // הבאת טבלאות ותצוגות מ-SQL Server באמצעות ADO.NET נקי ובשאילתה מאובטחת
    public async Task<List<DatabaseObject>> GetSqlObjectsAsync(string connectionString)
    {
        var objects = new List<DatabaseObject>();
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW') ORDER BY TABLE_NAME";
            using (var command = new SqlCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string rawType = reader.GetString(1);
                    string normalizedType = rawType == "VIEW" ? "VIEW" : "TABLE";
                    
                    objects.Add(new DatabaseObject
                    {
                        Name = reader.GetString(0),
                        Type = normalizedType
                    });
                }
            }
        }
        return objects;
    }

    // הבאת טבלאות ותצוגות מ-Oracle באמצעות ADO.NET נקי ובשאילתה מאובטחת
    public async Task<List<DatabaseObject>> GetOracleObjectsAsync(string connectionString)
    {
        var objects = new List<DatabaseObject>();
        using (var connection = new OracleConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT TABLE_NAME, 'TABLE' AS TABLE_TYPE FROM USER_TABLES UNION ALL SELECT VIEW_NAME AS TABLE_NAME, 'VIEW' AS TABLE_TYPE FROM USER_VIEWS ORDER BY TABLE_NAME";
            using (var command = new OracleCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    objects.Add(new DatabaseObject
                    {
                        Name = reader.GetString(0),
                        Type = reader.GetString(1)
                    });
                }
            }
        }
        return objects;
    }

    // שליפת עמודות מ-SQL Server באמצעות שאילתה מבוססת פרמטרים ומאובטחת
    public async Task<List<string>> GetSqlColumnsAsync(string connectionString, string tableName)
    {
        var columns = new List<string>();
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName ORDER BY COLUMN_NAME";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@tableName", tableName);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        return columns;
    }

    // שליפת עמודות מ-Oracle באמצעות שאילתה מבוססת פרמטרים ומאובטחת
    public async Task<List<string>> GetOracleColumnsAsync(string connectionString, string tableName)
    {
        var columns = new List<string>();
        using (var connection = new OracleConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :tableName ORDER BY COLUMN_NAME";
            using (var command = new OracleCommand(query, connection))
            {
                command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        return columns;
    }

    // ביצוע השוואת הנתונים בפועל והרצת האלגוריתם בזיכרון
    public async Task<ComparisonResultViewModel> CompareDataAsync(
        string sqlConnectionString,
        string oracleConnectionString,
        string sqlTable,
        string oracleTable,
        string mappingMode,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        int maxRows)
    {
        // אימות אבטחה מבוסס קטלוג (Whitelisting) עבור שמות הטבלאות למניעת SQL Injection
        if (!await IsSqlTableValidAsync(sqlConnectionString, sqlTable))
        {
            throw new ArgumentException("שם טבלת המקור (SQL Server) אינו תקין או שאינו קיים במערכת.");
        }
        if (!await IsOracleTableValidAsync(oracleConnectionString, oracleTable))
        {
            throw new ArgumentException("שם טבלת היעד (Oracle) אינו תקין או שאינו קיים במערכת.");
        }

        // שליפת עמודות מאומתות מהקטלוג למניעת הזרקת קוד בשמות שדות
        var validSqlCols = await GetSqlColumnsAsync(sqlConnectionString, sqlTable);
        var validOracleCols = await GetOracleColumnsAsync(oracleConnectionString, oracleTable);

        // הגבלת כמות השורות המקסימלית ומניעת ערכים שליליים כחלק מהגנה מפני עומס יתר (DoS)
        if (maxRows <= 0)
        {
            maxRows = 1000; // ברירת מחדל מאובטחת
        }
        else if (maxRows > 10000)
        {
            maxRows = 10000; // גבול עליון קשיח למניעת נפילת שרת
        }

        var keys = new List<(string SqlField, string OracleField)>();
        var compares = new List<(string SqlField, string OracleField)>();

        // טיפול במקרה של השוואה אוטומטית מלאה
        if (mappingMode == "Auto")
        {
            var matchedCols = new List<string>();
            foreach (var sc in validSqlCols)
            {
                var oc = validOracleCols.FirstOrDefault(c => string.Equals(c, sc, StringComparison.OrdinalIgnoreCase));
                if (oc != null)
                {
                    matchedCols.Add(sc);
                }
            }

            if (matchedCols.Count == 0)
            {
                throw new Exception("לא נמצאו עמודות בעלות שם זהה להשוואה אוטומטית.");
            }

            keys.Add((matchedCols[0], matchedCols[0]));
            for (int i = 1; i < matchedCols.Count; i++)
            {
                compares.Add((matchedCols[i], matchedCols[i]));
            }
        }
        else
        {
            // טיפול במקרה של מיפוי ידני
            if (sourceFields == null || targetFields == null || fieldRoles == null ||
                sourceFields.Count != targetFields.Count || sourceFields.Count != fieldRoles.Count)
            {
                throw new Exception("נתוני המיפוי הידני אינם תקינים או חסרים.");
            }

            // אימות אבטחה שכל השדות המבוקשים קיימים בקטלוג המערכת למניעת הזרקת עמודות בשאילתות
            foreach (var field in sourceFields)
            {
                if (!validSqlCols.Contains(field))
                {
                    throw new ArgumentException($"שם עמודת המקור '{field}' אינו חוקי או אינו קיים בטבלת המקור.");
                }
            }
            foreach (var field in targetFields)
            {
                if (!validOracleCols.Contains(field))
                {
                    throw new ArgumentException($"שם עמודת היעד '{field}' אינו חוקי או אינו קיים בטבלת היעד.");
                }
            }

            for (int i = 0; i < sourceFields.Count; i++)
            {
                if (fieldRoles[i] == "Key")
                {
                    keys.Add((sourceFields[i], targetFields[i]));
                }
                else
                {
                    compares.Add((sourceFields[i], targetFields[i]));
                }
            }

            if (keys.Count == 0)
            {
                throw new Exception("חובה להגדיר לפחות שדה מפתח אחד לביצוע השוואה ידנית.");
            }
        }

        // בניית שאילתת SQL Server מאובטחת
        var sqlColsToSelect = keys.Select(k => k.SqlField).Union(compares.Select(c => c.SqlField)).Distinct().ToList();
        string sqlSelectString = string.Join(", ", sqlColsToSelect.Select(c => $"[{c}]"));
        string sqlQuery = $"SELECT TOP ({maxRows}) {sqlSelectString} FROM [{sqlTable}]";

        // בניית שאילתת Oracle מאובטחת
        var oracleColsToSelect = keys.Select(k => k.OracleField).Union(compares.Select(c => c.OracleField)).Distinct().ToList();
        string oracleSelectString = string.Join(", ", oracleColsToSelect.Select(c => $"\"{c}\""));
        string oracleQuery = $"SELECT {oracleSelectString} FROM \"{oracleTable}\" FETCH FIRST {maxRows} ROWS ONLY";

        // שליפת הרשומות מ-SQL Server
        var sqlData = new Dictionary<string, Dictionary<string, object>>();
        using (var connection = new SqlConnection(sqlConnectionString))
        {
            await connection.OpenAsync();
            using (var command = new SqlCommand(sqlQuery, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }

                    var keyParts = keys.Select(k => row[k.SqlField]?.ToString()?.Trim() ?? "NULL");
                    string compositeKey = string.Join("|", keyParts);
                    sqlData[compositeKey] = row;
                }
            }
        }

        // שליפת הרשומות מ-Oracle
        var oracleData = new Dictionary<string, Dictionary<string, object>>();
        using (var connection = new OracleConnection(oracleConnectionString))
        {
            await connection.OpenAsync();
            using (var command = new OracleCommand(oracleQuery, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }

                    var keyParts = keys.Select(k => row[k.OracleField]?.ToString()?.Trim() ?? "NULL");
                    string compositeKey = string.Join("|", keyParts);
                    oracleData[compositeKey] = row;
                }
            }
        }

        // הרצת אלגוריתם ההשוואה בזיכרון
        int matchedCount = 0;
        int diffCount = 0;
        int missingInOracleCount = 0;
        int missingInSqlCount = 0;
        var details = new List<ComparisonRowDetail>();

        foreach (var sqlKvp in sqlData)
        {
            string compositeKey = sqlKvp.Key;
            var sqlRow = sqlKvp.Value;

            if (oracleData.TryGetValue(compositeKey, out var oracleRow))
            {
                bool isRowMatch = true;
                var fieldsList = new List<FieldComparisonDetail>();

                foreach (var c in compares)
                {
                    sqlRow.TryGetValue(c.SqlField, out var sqlVal);
                    oracleRow.TryGetValue(c.OracleField, out var oracleVal);

                    string sqlValStr = sqlVal?.ToString()?.Trim() ?? "NULL";
                    string oracleValStr = oracleVal?.ToString()?.Trim() ?? "NULL";
                    bool isFieldMatch = string.Equals(sqlValStr, oracleValStr, StringComparison.OrdinalIgnoreCase);

                    if (!isFieldMatch)
                    {
                        isRowMatch = false;
                    }

                    fieldsList.Add(new FieldComparisonDetail
                    {
                        FieldName = $"{c.SqlField} / {c.OracleField}",
                        SqlValue = sqlValStr,
                        OracleValue = oracleValStr,
                        IsMatch = isFieldMatch
                    });
                }

                if (isRowMatch)
                {
                    matchedCount++;
                }
                else
                {
                    diffCount++;
                    details.Add(new ComparisonRowDetail
                    {
                        KeyValue = compositeKey,
                        Status = "Difference",
                        Fields = fieldsList
                    });
                }
            }
            else
            {
                missingInOracleCount++;
                var fieldsList = compares.Select(c => new FieldComparisonDetail
                {
                    FieldName = $"{c.SqlField} / {c.OracleField}",
                    SqlValue = sqlRow[c.SqlField]?.ToString()?.Trim() ?? "NULL",
                    OracleValue = "חסר ביעד",
                    IsMatch = false
                }).ToList();

                details.Add(new ComparisonRowDetail
                {
                    KeyValue = compositeKey,
                    Status = "MissingInOracle",
                    Fields = fieldsList
                });
            }
        }

        foreach (var oracleKvp in oracleData)
        {
            string compositeKey = oracleKvp.Key;
            var oracleRow = oracleKvp.Value;

            if (!sqlData.ContainsKey(compositeKey))
            {
                missingInSqlCount++;
                var fieldsList = compares.Select(c => new FieldComparisonDetail
                {
                    FieldName = $"{c.SqlField} / {c.OracleField}",
                    SqlValue = "חסר במקור",
                    OracleValue = oracleRow[c.OracleField]?.ToString()?.Trim() ?? "NULL",
                    IsMatch = false
                }).ToList();

                details.Add(new ComparisonRowDetail
                {
                    KeyValue = compositeKey,
                    Status = "MissingInSql",
                    Fields = fieldsList
                });
            }
        }

        return new ComparisonResultViewModel
        {
            SqlTable = sqlTable,
            OracleTable = oracleTable,
            TotalMatched = matchedCount,
            TotalDifferences = diffCount,
            TotalMissingInOracle = missingInOracleCount,
            TotalMissingInSql = missingInSqlCount,
            Details = details
        };
    }

    // שיטת עזר לאימות קיום ושפיות שם טבלה/תצוגה ב-SQL Server למניעת הזרקות קוד
    private async Task<bool> IsSqlTableValidAsync(string connectionString, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return false;
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@tableName", tableName);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
        }
    }

    // שיטת עזר לאימות קיום ושפיות שם טבלה/תצוגה ב-Oracle למניעת הזרקות קוד
    private async Task<bool> IsOracleTableValidAsync(string connectionString, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return false;
        using (var connection = new OracleConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT COUNT(*) FROM (SELECT TABLE_NAME FROM USER_TABLES UNION ALL SELECT VIEW_NAME AS TABLE_NAME FROM USER_VIEWS) WHERE UPPER(TABLE_NAME) = :tableName";
            using (var command = new OracleCommand(query, connection))
            {
                command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
        }
    }
}
