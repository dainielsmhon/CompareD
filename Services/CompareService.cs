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
        if (connectionString == "MockConnectionString")
        {
            return new List<DatabaseObject>
            {
                new DatabaseObject { Name = "USERS", Type = "TABLE" },
                new DatabaseObject { Name = "ORDERS", Type = "TABLE" },
                new DatabaseObject { Name = "PRODUCTS", Type = "VIEW" }
            };
        }

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
        if (connectionString == "MockConnectionString")
        {
            return new List<DatabaseObject>
            {
                new DatabaseObject { Name = "USERS", Type = "TABLE" },
                new DatabaseObject { Name = "ORDERS", Type = "TABLE" },
                new DatabaseObject { Name = "PRODUCTS", Type = "VIEW" }
            };
        }

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
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "ID", "NAME", "EMAIL", "AGE", "CREATED_AT" };
            }
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "ORDER_ID", "USER_ID", "AMOUNT", "STATUS" };
            }
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "PRODUCT_ID", "NAME", "PRICE" };
            }
            return new List<string> { "ID", "NAME" };
        }

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
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "ID", "NAME", "EMAIL", "AGE", "CREATED_AT" };
            }
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "ORDER_ID", "USER_ID", "AMOUNT", "STATUS" };
            }
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "PRODUCT_ID", "NAME", "PRICE" };
            }
            return new List<string> { "ID", "NAME" };
        }

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
        if (sqlConnectionString == "MockConnectionString")
        {
            var mockRaw = CompareMockData.GetMockData(sqlTable, "SQL");
            foreach (var row in mockRaw)
            {
                var keyParts = keys.Select(k => row.TryGetValue(k.SqlField, out var v) ? v?.ToString()?.Trim() ?? "NULL" : "NULL");
                string compositeKey = string.Join("|", keyParts);
                sqlData[compositeKey] = row;
            }
        }
        else
        {
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
        }

        // שליפת הרשומות מ-Oracle
        var oracleData = new Dictionary<string, Dictionary<string, object>>();
        if (oracleConnectionString == "MockConnectionString")
        {
            var mockRaw = CompareMockData.GetMockData(oracleTable, "Oracle");
            foreach (var row in mockRaw)
            {
                var keyParts = keys.Select(k => row.TryGetValue(k.OracleField, out var v) ? v?.ToString()?.Trim() ?? "NULL" : "NULL");
                string compositeKey = string.Join("|", keyParts);
                oracleData[compositeKey] = row;
            }
        }
        else
        {
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
        if (connectionString == "MockConnectionString")
        {
            return string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase);
        }

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
        if (connectionString == "MockConnectionString")
        {
            return string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase);
        }

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

    // שליפת עמודות וטיפוסי הנתונים שלהן מ-SQL Server בצורה מאובטחת ומבוססת פרמטרים
    public async Task<List<(string ColumnName, string DataType)>> GetSqlColumnsWithTypesAsync(string connectionString, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<(string ColumnName, string DataType)>
                {
                    ("ID", "int"),
                    ("NAME", "nvarchar"),
                    ("EMAIL", "nvarchar"),
                    ("AGE", "int"),
                    ("CREATED_AT", "datetime")
                };
            }
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<(string ColumnName, string DataType)>
                {
                    ("ORDER_ID", "int"),
                    ("USER_ID", "int"),
                    ("AMOUNT", "decimal"),
                    ("STATUS", "nvarchar")
                };
            }
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<(string ColumnName, string DataType)>
                {
                    ("PRODUCT_ID", "int"),
                    ("NAME", "nvarchar"),
                    ("PRICE", "decimal")
                };
            }
            return new List<(string ColumnName, string DataType)> { ("ID", "int"), ("NAME", "nvarchar") };
        }

        // יצירת רשימה המכילה צמדים של שם עמודה וטיפוס נתונים
        var columns = new List<(string ColumnName, string DataType)>();
        // יצירת חיבור מנוהל ל-SQL Server בתוך בלוק using לשחרור משאבים אוטומטי
        using (var connection = new SqlConnection(connectionString))
        {
            // פתיחת החיבור למסד הנתונים באופן אסינכרוני
            await connection.OpenAsync();
            // שאילתת SQL קטלוגית השולפת את שם העמודה וטיפוסה באופן ממוקד וממוין
            string query = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName ORDER BY COLUMN_NAME";
            // בניית פקודת ההרצה
            using (var command = new SqlCommand(query, connection))
            {
                // הוספת פרמטר שם הטבלה למניעת הזרקת קוד זדוני (SQL Injection)
                command.Parameters.AddWithValue("@tableName", tableName);
                // הרצת השאילתה וקבלת Reader לקריאת הנתונים בצורה אסינכרונית
                using (var reader = await command.ExecuteReaderAsync())
                {
                    // קריאה שורה אחר שורה של התוצאות
                    while (await reader.ReadAsync())
                    {
                        // הוספת העמודה והטיפוס שלה כצמד לרשימה
                        columns.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }
            }
        }
        // החזרת רשימת העמודות עם טיפוסי הנתונים
        return columns;
    }

    // שליפת עמודות וטיפוסי הנתונים שלהן מ-Oracle בצורה מאובטחת ומבוססת פרמטרים
    public async Task<List<(string ColumnName, string DataType)>> GetOracleColumnsWithTypesAsync(string connectionString, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<(string ColumnName, string DataType)>
                {
                    ("ID", "NUMBER"),
                    ("NAME", "VARCHAR2"),
                    ("EMAIL", "VARCHAR2"),
                    ("AGE", "NUMBER"),
                    ("CREATED_AT", "DATE")
                };
            }
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<(string ColumnName, string DataType)>
                {
                    ("ORDER_ID", "NUMBER"),
                    ("USER_ID", "NUMBER"),
                    ("AMOUNT", "NUMBER"),
                    ("STATUS", "VARCHAR2")
                };
            }
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase))
            {
                return new List<(string ColumnName, string DataType)>
                {
                    ("PRODUCT_ID", "NUMBER"),
                    ("NAME", "VARCHAR2"),
                    ("PRICE", "NUMBER")
                };
            }
            return new List<(string ColumnName, string DataType)> { ("ID", "NUMBER"), ("NAME", "VARCHAR2") };
        }

        // יצירת רשימה לאחסון צמדי שם עמודה וטיפוס נתונים עבור Oracle
        var columns = new List<(string ColumnName, string DataType)>();
        // פתיחת חיבור מנוהל ל-Oracle בתוך בלוק using
        using (var connection = new OracleConnection(connectionString))
        {
            // פתיחת החיבור בצורה אסינכרונית
            await connection.OpenAsync();
            // שאילתת SQL לטבלת הקטלוג של Oracle לשליפת שם העמודה וטיפוס הנתונים שלה
            string query = "SELECT COLUMN_NAME, DATA_TYPE FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :tableName ORDER BY COLUMN_NAME";
            // בניית פקודת ההרצה מול Oracle
            using (var command = new OracleCommand(query, connection))
            {
                // הוספת פרמטר שם הטבלה באותיות גדולות (Uppercase) כנדרש בקטלוג של Oracle
                command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                // הרצת השאילתה וקבלת ה-Reader
                using (var reader = await command.ExecuteReaderAsync())
                {
                    // לולאת קריאה של כל עמודה
                    while (await reader.ReadAsync())
                    {
                        // שמירת שם העמודה וסוג הנתונים כצמד
                        columns.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }
            }
        }
        // החזרת רשימת העמודות שנסרקו
        return columns;
    }

    // ביצוע השוואת סכמה מקיפה ובניית מודל סקירת הסכמה
    public async Task<SchemaReviewViewModel> CompareSchemaAsync(
        string sqlConnectionString, 
        string oracleConnectionString, 
        string sqlTable, 
        string oracleTable)
    {
        // אימות תקינות שם טבלת SQL Server בקטלוג למניעת SQL Injection
        if (!await IsSqlTableValidAsync(sqlConnectionString, sqlTable))
        {
            throw new ArgumentException("שם טבלת המקור (SQL Server) אינו תקין או שאינו קיים במערכת.");
        }
        // אימות תקינות שם טבלת Oracle בקטלוג למניעת SQL Injection
        if (!await IsOracleTableValidAsync(oracleConnectionString, oracleTable))
        {
            throw new ArgumentException("שם טבלת היעד (Oracle) אינו תקין או שאינו קיים במערכת.");
        }

        // יצירת משתנים לאחסון זמני של הנתונים שיישלפו
        var sqlCols = new List<(string Name, string Type)>();
        var oracleCols = new List<(string Name, string Type)>();
        string sqlPk = string.Empty;
        string oraclePk = string.Empty;

        // שליפה אסינכרונית ומקבילית של כלל נתוני הסכמה והמפתחות משני מסדי הנתונים לחיסכון בזמן
        await Task.WhenAll(
            Task.Run(async () => { sqlCols = await GetSqlColumnsWithTypesAsync(sqlConnectionString, sqlTable); }),
            Task.Run(async () => { oracleCols = await GetOracleColumnsWithTypesAsync(oracleConnectionString, oracleTable); }),
            Task.Run(async () => { sqlPk = await GetSqlPrimaryKeyAsync(sqlConnectionString, sqlTable); }),
            Task.Run(async () => { oraclePk = await GetOraclePrimaryKeyAsync(oracleConnectionString, oracleTable); })
        );

        // אתחול מודל סקירת הסכמה
        var model = new SchemaReviewViewModel
        {
            SqlTable = sqlTable,
            OracleTable = oracleTable,
            Columns = new List<ColumnSchemaInfo>()
        };

        // לולאת מעבר על כל עמודות SQL Server להתאמה מול Oracle
        foreach (var sqlCol in sqlCols)
        {
            // חיפוש עמודה ב-Oracle בעלת שם זהה (ללא הבדלי רישיות)
            var match = oracleCols.FirstOrDefault(oc => string.Equals(oc.Name, sqlCol.Name, StringComparison.OrdinalIgnoreCase));
            
            if (match.Name != null)
            {
                // אם העמודה קיימת בשני מסדי הנתונים
                model.Columns.Add(new ColumnSchemaInfo
                {
                    ColumnName = sqlCol.Name,
                    SqlDataType = sqlCol.Type,
                    OracleDataType = match.Type,
                    ExistsInBoth = true,
                    Source = "Both"
                });
            }
            else
            {
                // אם העמודה קיימת ב-SQL Server בלבד וחסרה ב-Oracle
                model.Columns.Add(new ColumnSchemaInfo
                {
                    ColumnName = sqlCol.Name,
                    SqlDataType = sqlCol.Type,
                    OracleDataType = "חסר ביעד",
                    ExistsInBoth = false,
                    Source = "SqlOnly"
                });
            }
        }

        // לולאת מעבר על עמודות Oracle לאיתור עמודות שאינן קיימות ב-SQL Server
        foreach (var oracleCol in oracleCols)
        {
            // בדיקה האם העמודה כבר מופתה בשלב הקודם
            var hasCol = sqlCols.Any(sc => string.Equals(sc.Name, oracleCol.Name, StringComparison.OrdinalIgnoreCase));
            if (!hasCol)
            {
                // הוספת העמודה כקיימת ב-Oracle בלבד
                model.Columns.Add(new ColumnSchemaInfo
                {
                    ColumnName = oracleCol.Name,
                    SqlDataType = "חסר במקור",
                    OracleDataType = oracleCol.Type,
                    ExistsInBoth = false,
                    Source = "OracleOnly"
                });
            }
        }

        // קביעה האם מבנה הסכמה זהה לחלוטין (כל העמודות קיימות בשני הצדדים)
        model.IsSchemaIdentical = model.Columns.All(c => c.ExistsInBoth);

        // בחירת מפתח ראשי מוצע כברירת מחדל:
        // נבדוק תחילה האם המפתח הראשי של SQL Server קיים ומשותף
        if (!string.IsNullOrEmpty(sqlPk) && model.Columns.Any(c => string.Equals(c.ColumnName, sqlPk, StringComparison.OrdinalIgnoreCase) && c.ExistsInBoth))
        {
            model.PrimaryKeyColumn = sqlPk;
        }
        // אם לא נמצא, נבדוק האם המפתח הראשי של Oracle קיים ומשותף
        else if (!string.IsNullOrEmpty(oraclePk) && model.Columns.Any(c => string.Equals(c.ColumnName, oraclePk, StringComparison.OrdinalIgnoreCase) && c.ExistsInBoth))
        {
            // שימוש בשם העמודה המדויק מתוך המודל
            var matchCol = model.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, oraclePk, StringComparison.OrdinalIgnoreCase));
            model.PrimaryKeyColumn = matchCol?.ColumnName ?? oraclePk;
        }
        // ברירת מחדל אחרונה - נציע את העמודה המשותפת הראשונה שקיימת בשני הצדדים
        else
        {
            var firstCommon = model.Columns.FirstOrDefault(c => c.ExistsInBoth);
            if (firstCommon != null)
            {
                model.PrimaryKeyColumn = firstCommon.ColumnName;
            }
        }

        // קביעת ערך ברירת מחדל לכמות שורות מקסימלית לטעינה להשוואה
        model.MaxRows = 1000;

        // החזרת המודל המוכן
        return model;
    }

    // שליפת עמודת המפתח הראשי של טבלה ב-SQL Server מקטלוג המערכת בצורה מאובטחת
    private async Task<string> GetSqlPrimaryKeyAsync(string connectionString, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase)) return "ID";
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase)) return "ORDER_ID";
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase)) return "PRODUCT_ID";
            return "ID";
        }

        // פתיחת חיבור למסד SQL Server
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            // שאילתה לאחזור עמודת המפתח הראשי המוגדרת על הטבלה
            string query = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
                WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1 
                AND TABLE_NAME = @tableName";
            using (var command = new SqlCommand(query, connection))
            {
                // מניעת הזרקת קוד
                command.Parameters.AddWithValue("@tableName", tableName);
                // הרצה וקבלת תוצאה בודדת
                var result = await command.ExecuteScalarAsync();
                // החזרת שם העמודה או מחרוזת ריקה אם לא נמצא
                return result?.ToString() ?? string.Empty;
            }
        }
    }

    // שליפת עמודת המפתח הראשי של טבלה ב-Oracle מקטלוג המערכת בצורה מאובטחת
    private async Task<string> GetOraclePrimaryKeyAsync(string connectionString, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase)) return "ID";
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase)) return "ORDER_ID";
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase)) return "PRODUCT_ID";
            return "ID";
        }

        // פתיחת חיבור למסד Oracle
        using (var connection = new OracleConnection(connectionString))
        {
            await connection.OpenAsync();
            // שאילתה המשלבת Constraints וחלוקת עמודות כדי לאתר את המפתח הראשוני
            string query = @"
                SELECT cols.column_name 
                FROM all_constraints cons, all_cons_columns cols 
                WHERE cons.constraint_type = 'P' 
                AND cons.constraint_name = cols.constraint_name 
                AND cons.owner = cols.owner 
                AND UPPER(cons.table_name) = :tableName";
            using (var command = new OracleCommand(query, connection))
            {
                // העברת שם הטבלה באותיות גדולות ומניעת הזרקות קוד
                command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                // הרצה וקבלת התוצאה
                var result = await command.ExecuteScalarAsync();
                // החזרת התוצאה
                return result?.ToString() ?? string.Empty;
            }
        }
    }

    // פונקציית עזר להשוואת ערכים כללית המנרמלת ערכים ריקים
    private bool AreValuesEqual(object? sqlVal, object? oracleVal)
    {
        // נרמול שני הערכים למחרוזות נקיות
        string s1 = NormalizeValue(sqlVal);
        string s2 = NormalizeValue(oracleVal);

        return s1 == s2;
    }

    // פונקציית עזר לנרמול ערכים למחרוזת אחידה לצורך השוואה
    private string NormalizeValue(object? val)
    {
        // אם הערך null או DBNull, נחזיר מחרוזת ריקה
        if (val == null || val == DBNull.Value)
        {
            return string.Empty;
        }

        // ניקוי רווחים מיותרים מהערך הטקסטואלי
        string str = val.ToString()?.Trim() ?? string.Empty;

        // התייחסות למחרוזת "NULL" כערך ריק
        if (string.Equals(str, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return str;
    }

    // מנוע ההשוואה החכם - ביצוע השוואת נתונים, זיהוי כפילויות, חוסרים וקיבוץ לפי תבניות
    public async Task<SmartComparisonResultViewModel> SmartCompareAsync(
        string sqlConnectionString,
        string oracleConnectionString,
        string sqlTable,
        string oracleTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        int maxRows)
    {


        // אימות אבטחה של שם טבלת SQL Server בקטלוג
        if (!await IsSqlTableValidAsync(sqlConnectionString, sqlTable))
        {
            throw new ArgumentException("שם טבלת המקור (SQL Server) אינו תקין או שאינו קיים במערכת.");
        }
        // אימות אבטחה של שם טבלת Oracle בקטלוג
        if (!await IsOracleTableValidAsync(oracleConnectionString, oracleTable))
        {
            throw new ArgumentException("שם טבלת היעד (Oracle) אינו תקין או שאינו קיים במערכת.");
        }

        // שליפת עמודות מאומתות מהקטלוג למניעת הזרקת קוד בשמות שדות בשאילתות
        var validSqlCols = await GetSqlColumnsAsync(sqlConnectionString, sqlTable);
        var validOracleCols = await GetOracleColumnsAsync(oracleConnectionString, oracleTable);

        // אימות שכל השדות המבוקשים קיימים בקטלוג המערכת
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

        // הגבלת כמות השורות המקסימלית ומניעת ערכים שליליים
        if (maxRows <= 0)
        {
            maxRows = 1000; // ברירת מחדל
        }
        else if (maxRows > 10000)
        {
            maxRows = 10000; // גבול עליון קשיח
        }

        // פיצול השדות לשדות מפתח ושדות להשוואה
        var keys = new List<(string SqlField, string OracleField)>();
        var compares = new List<(string SqlField, string OracleField)>();

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
            throw new Exception("חובה להגדיר לפחות שדה מפתח אחד לביצוע ההשוואה.");
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
        var sqlRawData = new List<Dictionary<string, object>>();
        if (sqlConnectionString == "MockConnectionString")
        {
            sqlRawData = CompareMockData.GetMockData(sqlTable, "SQL");
        }
        else
        {
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
                        sqlRawData.Add(row);
                    }
                }
            }
        }

        // שליפת הרשומות מ-Oracle
        var oracleRawData = new List<Dictionary<string, object>>();
        if (oracleConnectionString == "MockConnectionString")
        {
            oracleRawData = CompareMockData.GetMockData(oracleTable, "Oracle");
        }
        else
        {
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
                        oracleRawData.Add(row);
                    }
                }
            }
        }

        return CompareInMemoryDatasets(sqlRawData, oracleRawData, sqlTable, oracleTable, sourceFields, targetFields, fieldRoles);
        // No-op placeholder comment to ensure this patch touches the file without changing behavior.
    }

    // QA Workflow comparison engine — executes sequential validation steps (A → B → C)
    public SmartComparisonResultViewModel CompareInMemoryDatasets(
        List<Dictionary<string, object>> sqlRawData,
        List<Dictionary<string, object>> oracleRawData,
        string sqlTable,
        string oracleTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles)
    {
        // Split fields into key fields and compare fields
        var keys = new List<(string SqlField, string OracleField)>();
        var compares = new List<(string SqlField, string OracleField)>();

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

        // Initialize result model with basic statistics
        var result = new SmartComparisonResultViewModel
        {
            SqlTable = sqlTable,
            OracleTable = oracleTable,
            PrimaryKeyColumn = string.Join(", ", keys.Select(k => k.SqlField)),
            TotalRowsInSql = sqlRawData.Count,
            TotalRowsInOracle = oracleRawData.Count
        };

        // Build key index for source
        var sqlKeyCounts = new Dictionary<string, int>();
        var sqlDataFiltered = new Dictionary<string, Dictionary<string, object>>();

        foreach (var row in sqlRawData)
        {
            var keyParts = keys.Select(k => row.TryGetValue(k.SqlField, out var v) ? v?.ToString()?.Trim() ?? "NULL" : "NULL");
            string compositeKey = string.Join("|", keyParts);

            if (sqlKeyCounts.ContainsKey(compositeKey))
            {
                sqlKeyCounts[compositeKey]++;
            }
            else
            {
                sqlKeyCounts[compositeKey] = 1;
                sqlDataFiltered[compositeKey] = row;
            }
        }

        // Build key index for target
        var oracleKeyCounts = new Dictionary<string, int>();
        var oracleDataFiltered = new Dictionary<string, Dictionary<string, object>>();

        foreach (var row in oracleRawData)
        {
            var keyParts = keys.Select(k => row.TryGetValue(k.OracleField, out var v) ? v?.ToString()?.Trim() ?? "NULL" : "NULL");
            string compositeKey = string.Join("|", keyParts);

            if (oracleKeyCounts.ContainsKey(compositeKey))
            {
                oracleKeyCounts[compositeKey]++;
            }
            else
            {
                oracleKeyCounts[compositeKey] = 1;
                oracleDataFiltered[compositeKey] = row;
            }
        }

        // --- STEP A Calculation based on Key Groupings ---
        result.SourceRawCount = sqlRawData.Count;
        result.TargetRawCount = oracleRawData.Count;

        // Calculate unique valid keys, duplicate keys count and build list of duplicate keys
        foreach (var kvp in sqlKeyCounts)
        {
            if (kvp.Value == 1)
            {
                result.SourceUniqueValidKeys++;
            }
            else
            {
                result.SourceDuplicateKeysCount += kvp.Value;
                result.SourceDuplicateKeysList.Add(kvp.Key);
            }
        }

        foreach (var kvp in oracleKeyCounts)
        {
            if (kvp.Value == 1)
            {
                result.TargetUniqueValidKeys++;
            }
            else
            {
                result.TargetDuplicateKeysCount += kvp.Value;
                result.TargetDuplicateKeysList.Add(kvp.Key);
            }
        }

        // Calculate missing keys and build lists
        // Source missing keys = exists in target keys, but not in source keys
        foreach (var k in oracleKeyCounts.Keys)
        {
            if (!sqlKeyCounts.ContainsKey(k))
            {
                result.SourceMissingKeysCount++;
                if (result.SourceMissingKeysList.Count < 50)
                {
                    result.SourceMissingKeysList.Add(k);
                }
            }
        }

        // Target missing keys = exists in source keys, but not in target keys
        foreach (var k in sqlKeyCounts.Keys)
        {
            if (!oracleKeyCounts.ContainsKey(k))
            {
                result.TargetMissingKeysCount++;
                if (result.TargetMissingKeysList.Count < 50)
                {
                    result.TargetMissingKeysList.Add(k);
                }
            }
        }

        // Initialize Step A
        var stepA = new QaStepResult
        {
            StepName = "A",
            StepTitle = "בדיקת שלמות שורות"
        };

        // Determine Step A status and summary message
        bool hasDupes = result.SourceDuplicateKeysList.Count > 0 || result.TargetDuplicateKeysList.Count > 0;
        bool hasMissing = result.SourceMissingKeysCount > 0 || result.TargetMissingKeysCount > 0;

        if (sqlRawData.Count == oracleRawData.Count)
        {
            if (hasMissing)
            {
                stepA.Status = "Warning";
                stepA.Summary = "ספירת שורות זהה, אך המפתחות בפועל שונים (מפתחות חסרים לעומת עודפים).";
                result.HasRowCountMismatch = true;
            }
            else if (hasDupes)
            {
                stepA.Status = "Warning";
                var dupeParts = new List<string>();
                if (result.SourceDuplicateKeysList.Count > 0)
                    dupeParts.Add($"מקור מכיל {result.SourceDuplicateKeysList.Count} מפתחות כפולים");
                if (result.TargetDuplicateKeysList.Count > 0)
                    dupeParts.Add($"יעד מכיל {result.TargetDuplicateKeysList.Count} מפתחות כפולים");
                stepA.Summary = string.Join(" | ", dupeParts);
                result.HasRowCountMismatch = false;
            }
            else
            {
                stepA.Status = "Pass";
                stepA.Summary = $"ספירת השורות והמפתחות זהה בשני הצדדים: {sqlRawData.Count} שורות.";
                result.HasRowCountMismatch = false;
            }
        }
        else
        {
            stepA.Status = "Warning";
            var summaryParts = new List<string>
            {
                $"פער בספירת שורות: מקור ({sqlTable}) מכיל {sqlRawData.Count} שורות, יעד ({oracleTable}) מכיל {oracleRawData.Count} שורות. הפרש: {Math.Abs(sqlRawData.Count - oracleRawData.Count)} שורות."
            };
            if (hasDupes)
            {
                var dupeParts = new List<string>();
                if (result.SourceDuplicateKeysList.Count > 0)
                    dupeParts.Add($"מקור מכיל {result.SourceDuplicateKeysList.Count} מפתחות כפולים");
                if (result.TargetDuplicateKeysList.Count > 0)
                    dupeParts.Add($"יעד מכיל {result.TargetDuplicateKeysList.Count} מפתחות כפולים");
                summaryParts.Add(string.Join(", ", dupeParts));
            }
            stepA.Summary = string.Join(" | ", summaryParts);
            result.HasRowCountMismatch = true;
        }

        result.RowCountSummary = $"מקור: {sqlRawData.Count} שורות | יעד: {oracleRawData.Count} שורות";
        result.QaSteps.Add(stepA);

        // =====================================================================
        // STEP B: Primary Key Validation — identify missing IDs and duplicates
        // =====================================================================
        var stepB = new QaStepResult
        {
            StepName = "B",
            StepTitle = "זיהוי מפתחות חסרים"
        };

        // Detect duplicate keys and exclude them from main comparison
        var duplicateKeys = new HashSet<string>();
        foreach (var kvp in sqlKeyCounts.Where(k => k.Value > 1))
        {
            duplicateKeys.Add(kvp.Key);
            result.Duplicates.Add(new DuplicateKeyRecord
            {
                KeyValue = kvp.Key,
                SqlCount = kvp.Value,
                OracleCount = oracleKeyCounts.ContainsKey(kvp.Key) ? oracleKeyCounts[kvp.Key] : 0
            });
        }
        foreach (var kvp in oracleKeyCounts.Where(k => k.Value > 1))
        {
            if (!duplicateKeys.Contains(kvp.Key))
            {
                duplicateKeys.Add(kvp.Key);
                result.Duplicates.Add(new DuplicateKeyRecord
                {
                    KeyValue = kvp.Key,
                    SqlCount = sqlKeyCounts.ContainsKey(kvp.Key) ? sqlKeyCounts[kvp.Key] : 0,
                    OracleCount = kvp.Value
                });
            }
        }

        result.TotalDuplicates = result.Duplicates.Sum(d => d.SqlCount + d.OracleCount);

        // Remove duplicate keys from the filtered dictionaries
        foreach (var dk in duplicateKeys)
        {
            sqlDataFiltered.Remove(dk);
            oracleDataFiltered.Remove(dk);
        }

        // Find missing keys: exist in source but not in target
        foreach (var sqlKvp in sqlDataFiltered)
        {
            if (!oracleDataFiltered.ContainsKey(sqlKvp.Key))
            {
                result.TotalMissingInOracle++;
                if (result.MissingInOracle.Count < 50)
                {
                    result.MissingInOracle.Add(sqlKvp.Key);
                }
            }
        }

        // Find missing keys: exist in target but not in source
        foreach (var oracleKvp in oracleDataFiltered)
        {
            if (!sqlDataFiltered.ContainsKey(oracleKvp.Key))
            {
                result.TotalMissingInSql++;
                if (result.MissingInSql.Count < 50)
                {
                    result.MissingInSql.Add(oracleKvp.Key);
                }
            }
        }

        // Set Step B status based on findings
        int totalMissing = result.TotalMissingInOracle + result.TotalMissingInSql;
        if (totalMissing == 0 && result.TotalDuplicates == 0)
        {
            stepB.Status = "Pass";
            stepB.Summary = "כל המפתחות קיימים בשני הצדדים ואין כפילויות.";
        }
        else
        {
            stepB.Status = totalMissing > 0 ? "Fail" : "Warning";
            var parts = new List<string>();
            if (result.TotalMissingInOracle > 0)
                parts.Add($"{result.TotalMissingInOracle} מפתחות חסרים ביעד ({oracleTable})");
            if (result.TotalMissingInSql > 0)
                parts.Add($"{result.TotalMissingInSql} מפתחות חסרים במקור ({sqlTable})");
            if (result.TotalDuplicates > 0)
                parts.Add($"{result.Duplicates.Count} מפתחות כפולים ({result.TotalDuplicates} שורות מושפעות)");
            stepB.Summary = string.Join(" | ", parts);
        }

        result.QaSteps.Add(stepB);

        // =====================================================================
        // STEP C: Data Consistency Check — deep value comparison
        // =====================================================================
        var stepC = new QaStepResult
        {
            StepName = "C",
            StepTitle = "ניתוח עקביות נתונים"
        };

        var discrepancyPatternsMap = new Dictionary<string, DiscrepancyPattern>();

        foreach (var sqlKvp in sqlDataFiltered)
        {
            string compositeKey = sqlKvp.Key;
            var sqlRow = sqlKvp.Value;

            if (oracleDataFiltered.TryGetValue(compositeKey, out var oracleRow))
            {
                // Key exists in both — compare field values
                var differentFields = new List<FieldComparisonDetail>();
                var diffFieldNames = new List<string>();

                foreach (var c in compares)
                {
                    sqlRow.TryGetValue(c.SqlField, out var sqlVal);
                    oracleRow.TryGetValue(c.OracleField, out var oracleVal);

                    if (!AreValuesEqual(sqlVal, oracleVal))
                    {
                        string sqlStr = NormalizeValue(sqlVal);
                        string oracleStr = NormalizeValue(oracleVal);

                        differentFields.Add(new FieldComparisonDetail
                        {
                            FieldName = $"{c.SqlField} / {c.OracleField}",
                            SqlValue = sqlVal?.ToString()?.Trim() ?? "NULL",
                            OracleValue = oracleVal?.ToString()?.Trim() ?? "NULL",
                            IsMatch = false
                        });
                        diffFieldNames.Add(c.SqlField);

                        // Data Integrity Gap detection:
                        // One side has a real value, the other is empty/null/zero
                        bool sqlIsEmpty = IsEmptyOrZero(sqlStr);
                        bool oracleIsEmpty = IsEmptyOrZero(oracleStr);

                        if (sqlIsEmpty != oracleIsEmpty)
                        {
                            // One side has data, the other doesn't
                            if (result.DataIntegrityGaps.Count < 200)
                            {
                                result.DataIntegrityGaps.Add(new DataIntegrityGap
                                {
                                    KeyValue = compositeKey,
                                    FieldName = $"{c.SqlField} / {c.OracleField}",
                                    PresentSide = sqlIsEmpty ? "Target" : "Source",
                                    PresentValue = sqlIsEmpty ? oracleStr : sqlStr
                                });
                            }
                        }
                    }
                }

                if (differentFields.Count == 0)
                {
                    // Rows are completely identical
                    result.TotalMatched++;
                }
                else
                {
                    // Differences found — group into discrepancy patterns
                    string patternKey = string.Join(", ", diffFieldNames);

                    if (!discrepancyPatternsMap.TryGetValue(patternKey, out var pattern))
                    {
                        pattern = new DiscrepancyPattern
                        {
                            PatternDescription = $"הפרש נתונים בשדות: {patternKey}",
                            Fields = differentFields,
                            ExampleKeys = new List<string>()
                        };
                        discrepancyPatternsMap[patternKey] = pattern;
                    }

                    pattern.Count++;
                    result.TotalDiscrepancyRows++;

                    // Add example keys (up to 4 per pattern)
                    if (pattern.ExampleKeys.Count < 4)
                    {
                        pattern.ExampleKeys.Add(compositeKey);
                    }
                }
            }
            // Note: missing keys were already handled in Step B
        }

        // Sort discrepancy patterns by count descending
        result.DiscrepancyPatterns = discrepancyPatternsMap.Values.OrderByDescending(p => p.Count).ToList();

        // Set Step C status based on findings
        int totalIssues = result.TotalDiscrepancyRows + result.DataIntegrityGaps.Count;
        if (totalIssues == 0)
        {
            stepC.Status = "Pass";
            stepC.Summary = $"כל {result.TotalMatched} הרשומות המשותפות תואמות לחלוטין. אין הבדלי נתונים.";
        }
        else
        {
            stepC.Status = "Fail";
            var parts = new List<string>();
            if (result.TotalDiscrepancyRows > 0)
                parts.Add($"{result.TotalDiscrepancyRows} שורות עם הפרשי ערכים ב-{result.DiscrepancyPatterns.Count} תבניות");
            if (result.DataIntegrityGaps.Count > 0)
                parts.Add($"{result.DataIntegrityGaps.Count} פערי שלמות נתונים (ערך מול ריק/אפס)");
            if (result.TotalMatched > 0)
                parts.Add($"{result.TotalMatched} שורות תואמות");
            stepC.Summary = string.Join(" | ", parts);
        }

        result.QaSteps.Add(stepC);

        return result;
    }



    // Helper: check if a normalized value is empty, null, or zero
    private bool IsEmptyOrZero(string normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return true;
        if (normalizedValue == "0")
            return true;
        if (normalizedValue == "0.0" || normalizedValue == "0.00")
            return true;
        return false;
    }
}


