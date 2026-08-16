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
    public async Task<List<DatabaseObject>> GetDatabaseObjectsAsync(string connectionString, string provider)
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
        if (provider == "SQLServer")
        {
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
                        objects.Add(new DatabaseObject { Name = reader.GetString(0), Type = normalizedType });
                    }
                }
            }
        }
        else if (provider == "Oracle")
        {
            using (var connection = new OracleConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT TABLE_NAME, 'TABLE' AS TABLE_TYPE FROM USER_TABLES UNION ALL SELECT VIEW_NAME AS TABLE_NAME, 'VIEW' AS TABLE_TYPE FROM USER_VIEWS ORDER BY TABLE_NAME";
                using (var command = new OracleCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        objects.Add(new DatabaseObject { Name = reader.GetString(0), Type = reader.GetString(1) });
                    }
                }
            }
        }
        return objects;
    }

    public async Task<List<string>> GetColumnsAsync(string connectionString, string provider, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
                return new List<string> { "ID", "NAME", "EMAIL", "AGE", "CREATED_AT" };
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase))
                return new List<string> { "ORDER_ID", "USER_ID", "AMOUNT", "STATUS" };
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase))
                return new List<string> { "PRODUCT_ID", "NAME", "PRICE" };
            return new List<string> { "ID", "NAME" };
        }

        var columns = new List<string>();
        if (provider == "SQLServer")
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName ORDER BY COLUMN_NAME";
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@tableName", tableName);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        else if (provider == "Oracle")
        {
            using (var connection = new OracleConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :tableName ORDER BY COLUMN_NAME";
                using (var command = new OracleCommand(query, connection))
                {
                    command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        return columns;
    }

    // ביצוע השוואת הנתונים בפועל והרצת האלגוריתם בזיכרון
    public async Task<ComparisonResultViewModel> CompareDataAsync(
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
        int maxRows)
    {
        if (!await IsTableValidAsync(sourceConnectionString, sourceProvider, sourceTable))
            throw new ArgumentException("שם טבלת המקור אינו תקין או שאינו קיים במערכת.");
        if (!await IsTableValidAsync(targetConnectionString, targetProvider, targetTable))
            throw new ArgumentException("שם טבלת היעד אינו תקין או שאינו קיים במערכת.");

        var validSqlCols = await GetColumnsAsync(sourceConnectionString, sourceProvider, sourceTable);
        var validOracleCols = await GetColumnsAsync(targetConnectionString, targetProvider, targetTable);

        if (maxRows <= 0) maxRows = 1000;
        else if (maxRows > 10000) maxRows = 10000;

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

        string GetSelectQuery(string provider, string table, IEnumerable<string> fields, int maxRowsLimit)
        {
            var distinctFields = fields.Distinct().ToList();
            if (provider == "SQLServer") {
                string selectString = string.Join(", ", distinctFields.Select(c => $"[{c}]"));
                return $"SELECT TOP ({maxRowsLimit}) {selectString} FROM [{table}]";
            } else {
                string selectString = string.Join(", ", distinctFields.Select(c => $"\"{c}\""));
                return $"SELECT {selectString} FROM \"{table}\" FETCH FIRST {maxRowsLimit} ROWS ONLY";
            }
        }

        string sqlQuery = GetSelectQuery(sourceProvider, sourceTable, keys.Select(k => k.SqlField).Union(compares.Select(c => c.SqlField)), maxRows);
        string oracleQuery = GetSelectQuery(targetProvider, targetTable, keys.Select(k => k.OracleField).Union(compares.Select(c => c.OracleField)), maxRows);

        var sqlData = new Dictionary<string, Dictionary<string, object>>();
        if (sourceConnectionString == "MockConnectionString") {
            var mockRaw = CompareMockData.GetMockData(sourceTable, "SQL");
            foreach (var row in mockRaw) {
                var keyParts = keys.Select(k => row.TryGetValue(k.SqlField, out var v) ? v?.ToString()?.Trim() ?? "NULL" : "NULL");
                sqlData[string.Join("|", keyParts)] = row;
            }
        } else {
            if (sourceProvider == "SQLServer") {
                using (var connection = new SqlConnection(sourceConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(sqlQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            var keyParts = keys.Select(k => row[k.SqlField]?.ToString()?.Trim() ?? "NULL");
                            sqlData[string.Join("|", keyParts)] = row;
                        }
                    }
                }
            } else {
                using (var connection = new OracleConnection(sourceConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(sqlQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            var keyParts = keys.Select(k => row[k.SqlField]?.ToString()?.Trim() ?? "NULL");
                            sqlData[string.Join("|", keyParts)] = row;
                        }
                    }
                }
            }
        }

        var oracleData = new Dictionary<string, Dictionary<string, object>>();
        if (targetConnectionString == "MockConnectionString") {
            var mockRaw = CompareMockData.GetMockData(targetTable, "Oracle");
            foreach (var row in mockRaw) {
                var keyParts = keys.Select(k => row.TryGetValue(k.OracleField, out var v) ? v?.ToString()?.Trim() ?? "NULL" : "NULL");
                oracleData[string.Join("|", keyParts)] = row;
            }
        } else {
            if (targetProvider == "SQLServer") {
                using (var connection = new SqlConnection(targetConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(oracleQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            var keyParts = keys.Select(k => row[k.OracleField]?.ToString()?.Trim() ?? "NULL");
                            oracleData[string.Join("|", keyParts)] = row;
                        }
                    }
                }
            } else {
                using (var connection = new OracleConnection(targetConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(oracleQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            var keyParts = keys.Select(k => row[k.OracleField]?.ToString()?.Trim() ?? "NULL");
                            oracleData[string.Join("|", keyParts)] = row;
                        }
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
                        SourceValue = sqlValStr,
                        TargetValue = oracleValStr,
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
                    SourceValue = sqlRow[c.SqlField]?.ToString()?.Trim() ?? "NULL",
                    TargetValue = "חסר ביעד",
                    IsMatch = false
                }).ToList();

                details.Add(new ComparisonRowDetail
                {
                    KeyValue = compositeKey,
                    Status = "MissingInTarget",
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
                    SourceValue = "חסר במקור",
                    TargetValue = oracleRow[c.OracleField]?.ToString()?.Trim() ?? "NULL",
                    IsMatch = false
                }).ToList();

                details.Add(new ComparisonRowDetail
                {
                    KeyValue = compositeKey,
                    Status = "MissingInSource",
                    Fields = fieldsList
                });
            }
        }

        return new ComparisonResultViewModel
        {
            SourceTable = sourceTable,
            TargetTable = targetTable,
            TotalMatched = matchedCount,
            TotalDifferences = diffCount,
            TotalMissingInTarget = missingInOracleCount,
            TotalMissingInSource = missingInSqlCount,
            Details = details
        };
    }

    public async Task<bool> IsTableValidAsync(string connectionString, string provider, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            return string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(tableName)) return false;
        
        if (provider == "SQLServer")
        {
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
        else if (provider == "Oracle")
        {
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
        return false;
    }

    public async Task<List<(string ColumnName, string DataType)>> GetColumnsWithTypesAsync(string connectionString, string provider, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
            {
                return provider == "SQLServer" ? new List<(string, string)> { ("ID", "int"), ("NAME", "nvarchar"), ("EMAIL", "nvarchar"), ("AGE", "int"), ("CREATED_AT", "datetime") }
                                               : new List<(string, string)> { ("ID", "NUMBER"), ("NAME", "VARCHAR2"), ("EMAIL", "VARCHAR2"), ("AGE", "NUMBER"), ("CREATED_AT", "DATE") };
            }
            return provider == "SQLServer" ? new List<(string, string)> { ("ID", "int"), ("NAME", "nvarchar") }
                                           : new List<(string, string)> { ("ID", "NUMBER"), ("NAME", "VARCHAR2") };
        }

        var columns = new List<(string ColumnName, string DataType)>();
        if (provider == "SQLServer")
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName ORDER BY COLUMN_NAME";
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@tableName", tableName);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }
            }
        }
        else if (provider == "Oracle")
        {
            using (var connection = new OracleConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT COLUMN_NAME, DATA_TYPE FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :tableName ORDER BY COLUMN_NAME";
                using (var command = new OracleCommand(query, connection))
                {
                    command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }
            }
        }
        return columns;
    }

    public async Task<SchemaReviewViewModel> CompareSchemaAsync(
        string sourceConnectionString, 
        string sourceProvider, 
        string targetConnectionString, 
        string targetProvider,
        string sourceTable, 
        string targetTable)
    {
        if (!await IsTableValidAsync(sourceConnectionString, sourceProvider, sourceTable))
            throw new ArgumentException("שם טבלת המקור אינו תקין או שאינו קיים במערכת.");
        if (!await IsTableValidAsync(targetConnectionString, targetProvider, targetTable))
            throw new ArgumentException("שם טבלת היעד אינו תקין או שאינו קיים במערכת.");

        var sqlCols = new List<(string Name, string Type)>();
        var oracleCols = new List<(string Name, string Type)>();
        string sqlPk = string.Empty;
        string oraclePk = string.Empty;

        await Task.WhenAll(
            Task.Run(async () => { sqlCols = await GetColumnsWithTypesAsync(sourceConnectionString, sourceProvider, sourceTable); }),
            Task.Run(async () => { oracleCols = await GetColumnsWithTypesAsync(targetConnectionString, targetProvider, targetTable); }),
            Task.Run(async () => { sqlPk = await GetPrimaryKeyAsync(sourceConnectionString, sourceProvider, sourceTable); }),
            Task.Run(async () => { oraclePk = await GetPrimaryKeyAsync(targetConnectionString, targetProvider, targetTable); })
        );

        var model = new SchemaReviewViewModel
        {
            SourceTable = sourceTable,
            TargetTable = targetTable,
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
                    SourceDataType = sqlCol.Type,
                    TargetDataType = match.Type,
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
                    SourceDataType = sqlCol.Type,
                    TargetDataType = "חסר ביעד",
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
                    SourceDataType = "חסר במקור",
                    TargetDataType = oracleCol.Type,
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

    private async Task<string> GetPrimaryKeyAsync(string connectionString, string provider, string tableName)
    {
        if (connectionString == "MockConnectionString")
        {
            if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase)) return "ID";
            if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase)) return "ORDER_ID";
            if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase)) return "PRODUCT_ID";
            return "ID";
        }

        if (provider == "SQLServer")
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1 AND TABLE_NAME = @tableName";
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@tableName", tableName);
                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString() ?? string.Empty;
                }
            }
        }
        else if (provider == "Oracle")
        {
            using (var connection = new OracleConnection(connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT cols.column_name FROM all_constraints cons, all_cons_columns cols WHERE cons.constraint_type = 'P' AND cons.constraint_name = cols.constraint_name AND cons.owner = cols.owner AND UPPER(cons.table_name) = :tableName";
                using (var command = new OracleCommand(query, connection))
                {
                    command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString() ?? string.Empty;
                }
            }
        }
        return string.Empty;
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

    public async Task<SmartComparisonResultViewModel> SmartCompareAsync(
        string sourceConnectionString,
        string sourceProvider,
        string targetConnectionString,
        string targetProvider,
        string sourceTable,
        string targetTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        int maxRows)
    {
        if (!await IsTableValidAsync(sourceConnectionString, sourceProvider, sourceTable))
            throw new ArgumentException("שם טבלת המקור אינו תקין או שאינו קיים במערכת.");
        if (!await IsTableValidAsync(targetConnectionString, targetProvider, targetTable))
            throw new ArgumentException("שם טבלת היעד אינו תקין או שאינו קיים במערכת.");

        var validSqlCols = await GetColumnsAsync(sourceConnectionString, sourceProvider, sourceTable);
        var validOracleCols = await GetColumnsAsync(targetConnectionString, targetProvider, targetTable);

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

        string GetSelectQuery(string provider, string table, IEnumerable<string> fields, int maxRowsLimit)
        {
            var distinctFields = fields.Distinct().ToList();
            if (provider == "SQLServer") {
                string selectString = string.Join(", ", distinctFields.Select(c => $"[{c}]"));
                return $"SELECT TOP ({maxRowsLimit}) {selectString} FROM [{table}]";
            } else {
                string selectString = string.Join(", ", distinctFields.Select(c => $"\"{c}\""));
                return $"SELECT {selectString} FROM \"{table}\" FETCH FIRST {maxRowsLimit} ROWS ONLY";
            }
        }

        string sqlQuery = GetSelectQuery(sourceProvider, sourceTable, keys.Select(k => k.SqlField).Union(compares.Select(c => c.SqlField)), maxRows);
        string oracleQuery = GetSelectQuery(targetProvider, targetTable, keys.Select(k => k.OracleField).Union(compares.Select(c => c.OracleField)), maxRows);

        var sqlRawData = new List<Dictionary<string, object>>();
        if (sourceConnectionString == "MockConnectionString") {
            sqlRawData = CompareMockData.GetMockData(sourceTable, "SQL");
        } else {
            if (sourceProvider == "SQLServer") {
                using (var connection = new SqlConnection(sourceConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(sqlQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            sqlRawData.Add(row);
                        }
                    }
                }
            } else {
                using (var connection = new OracleConnection(sourceConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(sqlQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            sqlRawData.Add(row);
                        }
                    }
                }
            }
        }

        var oracleRawData = new List<Dictionary<string, object>>();
        if (targetConnectionString == "MockConnectionString") {
            oracleRawData = CompareMockData.GetMockData(targetTable, "Oracle");
        } else {
            if (targetProvider == "SQLServer") {
                using (var connection = new SqlConnection(targetConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(oracleQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            oracleRawData.Add(row);
                        }
                    }
                }
            } else {
                using (var connection = new OracleConnection(targetConnectionString)) {
                    await connection.OpenAsync();
                    using (var command = new OracleCommand(oracleQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                            oracleRawData.Add(row);
                        }
                    }
                }
            }
        }

        return CompareInMemoryDatasets(sqlRawData, oracleRawData, sourceTable, targetTable, sourceFields, targetFields, fieldRoles);
    }

    // QA Workflow comparison engine — executes sequential validation steps (A → B → C)
    public SmartComparisonResultViewModel CompareInMemoryDatasets(
        List<Dictionary<string, object>> sqlRawData,
        List<Dictionary<string, object>> oracleRawData,
        string SourceTable,
        string TargetTable,
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

        // --- הזרקת קוד: זיהוי אוטומטי של DAF ו-DAF_NOSAF כמפתח מורכב ---
        bool hasDaf = sourceFields.Contains("DAF", StringComparer.OrdinalIgnoreCase) && targetFields.Contains("DAF", StringComparer.OrdinalIgnoreCase);
        bool hasDafNosaf = sourceFields.Contains("DAF_NOSAF", StringComparer.OrdinalIgnoreCase) && targetFields.Contains("DAF_NOSAF", StringComparer.OrdinalIgnoreCase);

        if (hasDaf && hasDafNosaf)
        {
            keys.Clear();
            compares.Clear();
            
            for (int i = 0; i < sourceFields.Count; i++)
            {
                if (sourceFields[i].Equals("DAF", StringComparison.OrdinalIgnoreCase) || sourceFields[i].Equals("DAF_NOSAF", StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add((sourceFields[i], targetFields[i]));
                }
                else
                {
                    compares.Add((sourceFields[i], targetFields[i]));
                }
            }
        }

        // Initialize result model with basic statistics
        var result = new SmartComparisonResultViewModel
        {
            SourceTable = SourceTable,
            TargetTable = TargetTable,
            PrimaryKeyColumn = string.Join(", ", keys.Select(k => k.SqlField)),
            TotalRowsInSource = sqlRawData.Count,
            TotalRowsInTarget = oracleRawData.Count
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

        if (hasDaf && hasDafNosaf)
        {
            var allCompositeKeys = sqlKeyCounts.Keys.Union(oracleKeyCounts.Keys).Distinct();
            foreach (var k in allCompositeKeys)
            {
                int s = sqlKeyCounts.GetValueOrDefault(k, 0);
                int t = oracleKeyCounts.GetValueOrDefault(k, 0);

                result.SourceUniqueValidKeys += s;
                result.TargetUniqueValidKeys += t;

                if (t > s)
                {
                    result.SourceMissingKeysCount += (t - s);
                    if (!result.SourceMissingKeysList.Contains(k)) result.SourceMissingKeysList.Add(k);
                }
                else if (s > t)
                {
                    result.TargetMissingKeysCount += (s - t);
                    if (!result.TargetMissingKeysList.Contains(k)) result.TargetMissingKeysList.Add(k);
                }
            }
        }
        else
        {
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
                var summaryParts = new List<string> { "ספירת שורות זהה, אך המפתחות בפועל שונים (מפתחות חסרים לעומת עודפים)." };
                var missingParts = new List<string>();
                if (result.SourceMissingKeysCount > 0)
                    missingParts.Add($"למקור חסרים {result.SourceMissingKeysCount} שורות/מפתחות שיש ביעד");
                if (result.TargetMissingKeysCount > 0)
                    missingParts.Add($"ליעד חסרים {result.TargetMissingKeysCount} שורות/מפתחות שיש במקור");
                summaryParts.Add(string.Join(", ", missingParts));
                stepA.Summary = string.Join(" | ", summaryParts);
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
                $"פער בספירת שורות: מקור ({SourceTable}) מכיל {sqlRawData.Count} שורות, יעד ({TargetTable}) מכיל {oracleRawData.Count} שורות. הפרש: {Math.Abs(sqlRawData.Count - oracleRawData.Count)} שורות."
            };
            if (hasMissing)
            {
                var missingParts = new List<string>();
                if (result.SourceMissingKeysCount > 0)
                    missingParts.Add($"למקור חסרים {result.SourceMissingKeysCount} שורות/מפתחות שיש ביעד");
                if (result.TargetMissingKeysCount > 0)
                    missingParts.Add($"ליעד חסרים {result.TargetMissingKeysCount} שורות/מפתחות שיש במקור");
                summaryParts.Add(string.Join(", ", missingParts));
            }
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
        
        var sqlDupes = hasDaf && hasDafNosaf 
            ? sqlKeyCounts.Where(k => k.Value > 1 && oracleKeyCounts.GetValueOrDefault(k.Key, 0) != k.Value)
            : sqlKeyCounts.Where(k => k.Value > 1);

        foreach (var kvp in sqlDupes)
        {
            duplicateKeys.Add(kvp.Key);
            result.Duplicates.Add(new DuplicateKeyRecord
            {
                KeyValue = kvp.Key,
                SourceCount = kvp.Value,
                TargetCount = oracleKeyCounts.ContainsKey(kvp.Key) ? oracleKeyCounts[kvp.Key] : 0
            });
        }
        
        var oracleDupes = hasDaf && hasDafNosaf
            ? oracleKeyCounts.Where(k => k.Value > 1 && sqlKeyCounts.GetValueOrDefault(k.Key, 0) != k.Value)
            : oracleKeyCounts.Where(k => k.Value > 1);

        foreach (var kvp in oracleDupes)
        {
            if (!duplicateKeys.Contains(kvp.Key))
            {
                duplicateKeys.Add(kvp.Key);
                result.Duplicates.Add(new DuplicateKeyRecord
                {
                    KeyValue = kvp.Key,
                    SourceCount = sqlKeyCounts.ContainsKey(kvp.Key) ? sqlKeyCounts[kvp.Key] : 0,
                    TargetCount = kvp.Value
                });
            }
        }

        result.TotalDuplicates = result.Duplicates.Sum(d => d.SourceCount + d.TargetCount);

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
                result.TotalMissingInTarget++;
                if (result.MissingInTarget.Count < 50)
                {
                    result.MissingInTarget.Add(sqlKvp.Key);
                }
            }
        }

        // Find missing keys: exist in target but not in source
        foreach (var oracleKvp in oracleDataFiltered)
        {
            if (!sqlDataFiltered.ContainsKey(oracleKvp.Key))
            {
                result.TotalMissingInSource++;
                if (result.MissingInSource.Count < 50)
                {
                    result.MissingInSource.Add(oracleKvp.Key);
                }
            }
        }

        // Set Step B status based on findings
        int totalMissing = result.TotalMissingInTarget + result.TotalMissingInSource;
        if (totalMissing == 0 && result.TotalDuplicates == 0)
        {
            stepB.Status = "Pass";
            stepB.Summary = "כל המפתחות קיימים בשני הצדדים ואין כפילויות.";
        }
        else
        {
            stepB.Status = totalMissing > 0 ? "Fail" : "Warning";
            var parts = new List<string>();
            if (result.TotalMissingInTarget > 0)
                parts.Add($"{result.TotalMissingInTarget} מפתחות חסרים ביעד ({TargetTable})");
            if (result.TotalMissingInSource > 0)
                parts.Add($"{result.TotalMissingInSource} מפתחות חסרים במקור ({SourceTable})");
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
                            SourceValue = sqlVal?.ToString()?.Trim() ?? "NULL",
                            TargetValue = oracleVal?.ToString()?.Trim() ?? "NULL",
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

        // הזרקת קוד: חוסר התאמה בכמויות לפי DAF ו-DAF_NOSAF
        var sourceGrouped = sqlRawData
            .GroupBy(row => new 
            { 
                Daf = row.ContainsKey("DAF") ? row["DAF"]?.ToString() ?? "NULL" : "NULL", 
                DafNosaf = row.ContainsKey("DAF_NOSAF") ? row["DAF_NOSAF"]?.ToString() ?? "NULL" : "NULL" 
            })
            .Select(g => new { Key = g.Key, Count = g.Count() });

        var targetGrouped = oracleRawData
            .GroupBy(row => new 
            { 
                Daf = row.ContainsKey("DAF") ? row["DAF"]?.ToString() ?? "NULL" : "NULL", 
                DafNosaf = row.ContainsKey("DAF_NOSAF") ? row["DAF_NOSAF"]?.ToString() ?? "NULL" : "NULL" 
            })
            .Select(g => new { Key = g.Key, Count = g.Count() });

        var sourceDict = sourceGrouped.ToDictionary(g => g.Key, g => g.Count);
        var targetDict = targetGrouped.ToDictionary(g => g.Key, g => g.Count);

        var allKeys = sourceDict.Keys.Union(targetDict.Keys).ToList();

        var mismatches = new List<OccurrenceMismatchRecord>();
        foreach (var key in allKeys)
        {
            sourceDict.TryGetValue(key, out int sCount);
            targetDict.TryGetValue(key, out int tCount);

            if (sCount != tCount)
            {
                mismatches.Add(new OccurrenceMismatchRecord 
                { 
                    Key = new DafKey { Daf = key.Daf, DafNosaf = key.DafNosaf }, 
                    SourceCount = sCount, 
                    TargetCount = tCount 
                });
            }
        }
        
        result.OccurrenceMismatches = mismatches.OrderBy(m => m.Key.Daf).ThenBy(m => m.Key.DafNosaf).ToList();

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


