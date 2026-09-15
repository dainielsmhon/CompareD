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
                {
                    // הגדרת פסק זמן לשאילתה על מנת למנוע תקיעות שרת מול אורקל ישן
                    command.CommandTimeout = 15;
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            objects.Add(new DatabaseObject { Name = reader.GetString(0), Type = reader.GetString(1) });
                        }
                    }
                }
            }
        }
        return objects;
    }

    public async Task<List<string>> GetColumnsAsync(string connectionString, string provider, string tableName)
    {
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

    public async Task<bool> IsTableValidAsync(string connectionString, string provider, string tableName)
    {
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

        // הצעת טיפוס השוואה לכל עמודה לפי טיפוסי הנתונים שנשלפו מהקטלוג.
        // נעשה בלולאה אחת בסוף כדי שלא יהיה צורך לחזור על החישוב בכל אתר הוספה.
        foreach (var column in model.Columns)
        {
            column.SuggestedKind = SuggestValueKind(column.SourceDataType, column.TargetDataType);
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

    // =====================================================================
    // תשתית סינון דינמי מאובטח
    // כל שם עמודה מאומת מול קטלוג המערכת, כל אופרטור מאומת מול רשימה סגורה,
    // וכל ערך מועבר כפרמטר ADO.NET מוקלד ולא כאינטרפולציה במחרוזת השאילתה.
    // =====================================================================

    // רשימת האופרטורים המותרים בסינון (Whitelist סגור) למניעת הזרקת קוד
    private static readonly HashSet<string> AllowedFilterOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        "Equals", "Like", "GreaterThan", "LessThan"
    };

    // טיפוסי נתונים מספריים בשני המסדים - נדרשים להמרת ערך הסינון לטיפוס הנכון
    private static readonly HashSet<string> NumericDataTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // SQL Server
        "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "float", "real", "money", "smallmoney", "bit",
        // Oracle
        "NUMBER", "BINARY_FLOAT", "BINARY_DOUBLE"
    };

    // טיפוסי נתונים של תאריך ושעה בשני המסדים
    private static readonly HashSet<string> DateDataTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // SQL Server
        "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time",
        // Oracle
        "DATE"
    };

    // מבנה עזר המחזיק את מקטע הסינון שנבנה ואת הפרמטרים המשויכים לו
    private sealed class FilterClause
    {
        // מקטע ה-WHERE המוכן לשילוב בשאילתה (כולל המילה WHERE), או מחרוזת ריקה
        public string WhereClause { get; set; } = string.Empty;

        // רשימת הפרמטרים שיש לצרף לפקודה - שם הפרמטר והערך לאחר המרה
        public List<(string Name, object Value)> Parameters { get; } = new();
    }

    // בדיקה האם טיפוס הנתונים הוא מספרי
    private static bool IsNumericDataType(string dataType)
    {
        return !string.IsNullOrWhiteSpace(dataType) && NumericDataTypes.Contains(dataType.Trim());
    }

    // בדיקה האם טיפוס הנתונים הוא תאריך או חותמת זמן
    private static bool IsDateDataType(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return false;
        string trimmed = dataType.Trim();
        // אורקל מחזיר גם וריאציות כגון TIMESTAMP(6) ו-TIMESTAMP(6) WITH TIME ZONE
        return DateDataTypes.Contains(trimmed) || trimmed.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase);
    }

    // המרת ערך הסינון הטקסטואלי שהוגש מהממשק לטיפוס הנתונים של העמודה.
    // זהו לב התיקון: ערך שנשלח כמחרוזת גורם לכל מסד נתונים להמיר אותו בכללים שונים
    // (אורקל לפי NLS, SQL Server לפי ה-scale של העמודה), ולכן אותו תנאי בדיוק
    // עלול להתאים בצד אחד ולסנן את כל השורות בצד השני.
    private static object ConvertFilterValue(string rawValue, string dataType, string columnName)
    {
        string trimmed = rawValue.Trim();

        if (IsNumericDataType(dataType))
        {
            if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
            {
                return numericValue;
            }
            throw new ArgumentException($"הערך '{rawValue}' אינו מספר תקין עבור עמודה '{columnName}' מטיפוס {dataType}.");
        }

        if (IsDateDataType(dataType))
        {
            if (DateTime.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dateValue))
            {
                return dateValue;
            }
            throw new ArgumentException($"הערך '{rawValue}' אינו תאריך תקין עבור עמודה '{columnName}' מטיפוס {dataType}.");
        }

        // עמודה טקסטואלית - הערך מועבר כמחרוזת
        return trimmed;
    }

    // בניית מקטע WHERE פרמטרי ומאובטח מתוך תנאי הסינון שהוגשו מהממשק
    private FilterClause BuildFilterClause(
        string provider,
        List<string> validColumns,
        List<(string ColumnName, string DataType)> columnTypes,
        List<string>? filterColumn,
        List<string>? filterOperator,
        List<string>? filterValue)
    {
        var clause = new FilterClause();
        if (filterColumn == null || filterColumn.Count == 0) return clause;

        var conditions = new List<string>();

        for (int i = 0; i < filterColumn.Count; i++)
        {
            string fCol = filterColumn[i];
            string fOp = (filterOperator != null && filterOperator.Count > i) ? filterOperator[i] : "Equals";
            string fVal = (filterValue != null && filterValue.Count > i) ? filterValue[i] : string.Empty;

            // דילוג על שורות סינון שלא מולאו במלואן
            if (string.IsNullOrWhiteSpace(fCol) || string.IsNullOrWhiteSpace(fVal)) continue;

            // אימות אבטחה: שם העמודה חייב להופיע בקטלוג המערכת
            var matchedColumn = validColumns.FirstOrDefault(c => string.Equals(c, fCol, StringComparison.OrdinalIgnoreCase));
            if (matchedColumn == null)
            {
                throw new ArgumentException($"שם עמודת הסינון '{fCol}' אינו חוקי או אינו קיים בטבלה.");
            }

            // אימות אבטחה: האופרטור חייב להיות מתוך הרשימה המותרת
            if (!AllowedFilterOperators.Contains(fOp))
            {
                throw new ArgumentException($"אופרטור הסינון '{fOp}' אינו חוקי.");
            }

            // שליפת טיפוס הנתונים של העמודה לצורך המרת הערך
            var typeInfo = columnTypes.FirstOrDefault(c => string.Equals(c.ColumnName, matchedColumn, StringComparison.OrdinalIgnoreCase));
            string dataType = typeInfo.ColumnName != null ? typeInfo.DataType : string.Empty;

            string paramName = $"filterValue{clause.Parameters.Count}";
            string quotedColumn = provider == "SQLServer" ? $"[{matchedColumn}]" : $"\"{matchedColumn}\"";

            if (fOp == "Like")
            {
                // חיפוש טקסטואלי על עמודה שאינה טקסטואלית מקבל המרה מפורשת לטקסט,
                // כדי שההתנהגות תהיה זהה בשני המסדים ולא תלויה בהמרה אוטומטית
                bool needsCast = IsNumericDataType(dataType) || IsDateDataType(dataType);
                if (provider == "SQLServer")
                {
                    string expression = needsCast ? $"CAST({quotedColumn} AS NVARCHAR(100))" : quotedColumn;
                    conditions.Add($"{expression} LIKE '%' + @{paramName} + '%'");
                }
                else
                {
                    string expression = needsCast ? $"TO_CHAR({quotedColumn})" : quotedColumn;
                    conditions.Add($"{expression} LIKE '%' || :{paramName} || '%'");
                }

                // בחיפוש טקסטואלי הערך מועבר תמיד כמחרוזת
                clause.Parameters.Add((paramName, fVal.Trim()));
            }
            else
            {
                string sqlOperator = fOp == "GreaterThan" ? ">" : (fOp == "LessThan" ? "<" : "=");
                object typedValue = ConvertFilterValue(fVal, dataType, matchedColumn);
                string placeholder = provider == "SQLServer" ? $"@{paramName}" : $":{paramName}";

                conditions.Add($"{quotedColumn} {sqlOperator} {placeholder}");
                clause.Parameters.Add((paramName, typedValue));
            }
        }

        if (conditions.Count > 0)
        {
            clause.WhereClause = " WHERE " + string.Join(" AND ", conditions);
        }

        return clause;
    }

    // צירוף פרמטרי הסינון לפקודת SQL Server לפי התבנית הקיימת בפרויקט
    private static void ApplyFilterParameters(SqlCommand command, FilterClause filter)
    {
        foreach (var parameter in filter.Parameters)
        {
            command.Parameters.AddWithValue($"@{parameter.Name}", parameter.Value);
        }
    }

    // צירוף פרמטרי הסינון לפקודת אורקל לפי התבנית הקיימת בפרויקט
    private static void ApplyFilterParameters(OracleCommand command, FilterClause filter)
    {
        if (filter.Parameters.Count == 0) return;

        // אורקל קושר פרמטרים לפי מקום כברירת מחדל - מעבר לקישור לפי שם מונע תקלות סדר
        command.BindByName = true;
        foreach (var parameter in filter.Parameters)
        {
            command.Parameters.Add(new OracleParameter(parameter.Name, parameter.Value));
        }
    }

    // תרגום שם עמודת הסינון מצד המקור לצד היעד לפי טבלת המיפוי שהמשתמש הגדיר.
    // הממשק מציג לסינון את עמודות המקור בלבד, ולכן ביעד יש לחפש את העמודה המקבילה.
    // מיון שורות לפי עמודות המפתח, עבור מסלול השוואת הקבצים.
    //
    // מקביל ל-ORDER BY על שדות המפתח שקיים במסלול המסד לפני TOP/FETCH FIRST.
    // בלעדיו, חיתוך ל-N שורות לוקח את N הראשונות בסדר הפיזי בקובץ; שני קבצים
    // בסדר שונה מייצרים חלונות שמכסים טווחי מפתח שונים, וכל מפתח שנמצא בחלון
    // של צד אחד בלבד מדווח כחוסר. כך נוצר דיווח סימטרי של מאות חוסרים מדומים.
    //
    // ההשוואה מספרית כשאפשר ואחרת טקסטואלית, כדי שדף 100 לא יקדם לדף 2.
    public static List<Dictionary<string, object>> OrderRowsByKey(
        List<Dictionary<string, object>> rows,
        List<string> keyColumns)
    {
        if (keyColumns == null || keyColumns.Count == 0) return rows;

        var ordered = rows.OrderBy(r => KeySortValue(r, keyColumns[0]), KeySortComparer.Instance);
        for (int i = 1; i < keyColumns.Count; i++)
        {
            string column = keyColumns[i];
            ordered = ordered.ThenBy(r => KeySortValue(r, column), KeySortComparer.Instance);
        }
        return ordered.ToList();
    }

    // ערך עמודה לצורך מיון, עם התאמת שם עמודה ללא רגישות לרישיות
    private static string KeySortValue(Dictionary<string, object> row, string column)
    {
        if (row.TryGetValue(column, out var value)) return value?.ToString()?.Trim() ?? string.Empty;
        var matched = row.Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
        return matched != null ? row[matched]?.ToString()?.Trim() ?? string.Empty : string.Empty;
    }

    // משווה שמזהה מספרים ומסדר אותם לפי ערכם, ולא לפי סדר לקסיקוגרפי
    private sealed class KeySortComparer : IComparer<string>
    {
        public static readonly KeySortComparer Instance = new KeySortComparer();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;

            bool xNum = decimal.TryParse(x, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal xv);
            bool yNum = decimal.TryParse(y, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal yv);

            if (xNum && yNum) return xv.CompareTo(yv);

            // מספרים מסודרים לפני טקסט, ולא מושווים אליו טקסטואלית.
            //
            // בלי ההפרדה הזו הסדר אינו טרנזיטיבי: "9" קטן מ-"10" בהשוואה
            // מספרית, "10" קטן מ-"1abc" בהשוואה טקסטואלית, אך "9" גדול
            // מ-"1abc" - וסדר כזה גורם ל-Sort לזרוק
            // "IComparer.Compare() method returns inconsistent results".
            // עמודת מפתח שמערבת מספרים וטקסט היא בדיוק מה שקורה במיגרציה
            // שבה עמודה מספרית הפכה ל-VARCHAR2.
            if (xNum != yNum) return xNum ? -1 : 1;

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }

    // סינון שורות בזיכרון, עבור מסלול השוואת הקבצים.
    //
    // עד כה הסינון היה קיים במסלול המסד בלבד. במסלול הקבצים לא היה סינון כלל,
    // ולכן שורות שהמשתמש התכוון להוציא מהבדיקה (למשל יום שאינו בטווח) נכנסו
    // להשוואה ודווחו כחוסר - מה שאילץ מחיקה ידנית של שורות בגיליון.
    //
    // הסמנטיקה זהה ל-BuildFilterClause: Equals השוואה מספרית כשאפשר ואחרת
    // טקסטואלית ללא רגישות לרישיות (כמו ה-collation המוגדר ב-SQL Server),
    // Like הכלה, ו-GreaterThan/LessThan מספרי כשאפשר ואחרת לפי סדר טקסטואלי.
    public static List<Dictionary<string, object>> ApplyRowFilter(
        List<Dictionary<string, object>> rows,
        List<string>? filterColumn,
        List<string>? filterOperator,
        List<string>? filterValue,
        Func<string, string>? translateColumn = null)
    {
        if (filterColumn == null || filterColumn.Count == 0) return rows;

        var conditions = new List<(string Column, string Operator, string Value)>();
        for (int i = 0; i < filterColumn.Count; i++)
        {
            string column = filterColumn[i];
            string op = (filterOperator != null && filterOperator.Count > i) ? filterOperator[i] : "Equals";
            string value = (filterValue != null && filterValue.Count > i) ? filterValue[i] : string.Empty;

            // דילוג על שורות סינון שלא מולאו במלואן - כמו במסלול המסד
            if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(value)) continue;
            if (!AllowedFilterOperators.Contains(op)) continue;

            conditions.Add((translateColumn != null ? translateColumn(column) : column, op, value.Trim()));
        }

        if (conditions.Count == 0) return rows;

        // עמודת סינון שאינה קיימת בקובץ נעצרת בשגיאה מפורשת.
        //
        // בלי הבדיקה הזו, תנאי על עמודה שלא הופתה (או ששמה השתנה בין
        // הצדדים) לא התאים לאף שורה, וכל הקובץ סונן לאפס שורות. הדוח
        // דיווח אז "אחד הקבצים ריק" - הודעה שמצביעה על הקובץ במקום על
        // תנאי הסינון, וזה בדיוק המקום שבו נבזבז זמן חיפוש.
        if (rows.Count > 0)
        {
            var available = rows[0].Keys;
            var missing = conditions
                .Select(c => c.Column)
                .Where(c => !available.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0)
            {
                throw new ArgumentException(
                    "תנאי הסינון מפנה לעמודות שאינן קיימות בקובץ: "
                    + string.Join(", ", missing)
                    + ". יש לבחור עמודה מתוך רשימת העמודות של הקובץ.");
            }
        }

        // התנאים מחוברים ב-AND, בדיוק כמו הסינון במסד
        return rows.Where(row => conditions.All(c => RowMatchesCondition(row, c.Column, c.Operator, c.Value))).ToList();
    }

    // בדיקת תנאי סינון בודד מול שורה אחת
    private static bool RowMatchesCondition(
        Dictionary<string, object> row, string column, string op, string value)
    {
        // התאמת שם העמודה ללא רגישות לרישיות, כמו במסד
        if (!row.TryGetValue(column, out var raw))
        {
            var matched = row.Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
            if (matched == null) return false;
            raw = row[matched];
        }

        string text = raw?.ToString()?.Trim() ?? string.Empty;

        if (op == "Like")
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        decimal rowNumber = 0;
        decimal filterNumber = 0;
        bool bothNumeric =
            decimal.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out rowNumber) &&
            decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out filterNumber);

        if (bothNumeric)
        {
            return op switch
            {
                "GreaterThan" => rowNumber > filterNumber,
                "LessThan" => rowNumber < filterNumber,
                _ => rowNumber == filterNumber
            };
        }

        int comparison = string.Compare(text, value, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "GreaterThan" => comparison > 0,
            "LessThan" => comparison < 0,
            _ => comparison == 0
        };
    }

    private static List<string>? TranslateFilterColumns(
        List<string>? filterColumn, List<string>? sourceFields, List<string>? targetFields)
    {
        if (filterColumn == null) return null;
        if (sourceFields == null || targetFields == null) return filterColumn;

        var translated = new List<string>();
        foreach (var column in filterColumn)
        {
            // חיפוש השדה בטבלת המיפוי; אם אינו ממופה - נשארים עם אותו שם
            int index = sourceFields.FindIndex(f => string.Equals(f, column, StringComparison.OrdinalIgnoreCase));
            translated.Add(index >= 0 && index < targetFields.Count ? targetFields[index] : column);
        }

        return translated;
    }

    // בניית רשימת העמודות לתצוגה מקדימה: השדות שהמשתמש מיפה בפועל.
    // בהיעדר מיפוי נופלים לחמש העמודות הראשונות כברירת מחדל (התנהגות היסטורית).
    private static List<string> BuildPreviewFields(List<string>? mappedFields, List<string> validColumns)
    {
        if (mappedFields != null && mappedFields.Count > 0)
        {
            var selected = mappedFields
                .Where(f => validColumns.Any(c => string.Equals(c, f, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selected.Count > 0) return selected;
        }

        return validColumns.Take(5).ToList();
    }

    // איתור שדות המפתח מתוך טבלת המיפוי לצורך מיון דטרמיניסטי של התצוגה המקדימה
    private static List<string> BuildPreviewKeyFields(
        List<string>? mappedFields, List<string>? fieldRoles, List<string> validColumns, List<string> previewFields)
    {
        if (mappedFields != null && fieldRoles != null)
        {
            var keys = new List<string>();
            for (int i = 0; i < mappedFields.Count && i < fieldRoles.Count; i++)
            {
                if (!string.Equals(fieldRoles[i], "Key", StringComparison.Ordinal)) continue;

                // אימות אבטחה: שם השדה חייב להופיע בקטלוג לפני שילובו במקטע ORDER BY
                var matched = validColumns.FirstOrDefault(c => string.Equals(c, mappedFields[i], StringComparison.OrdinalIgnoreCase));
                if (matched != null) keys.Add(matched);
            }

            if (keys.Count > 0) return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // בהיעדר מפתחות מוגדרים נמיין לפי העמודה הראשונה כדי לשמור על סדר יציב
        return previewFields.Take(1).ToList();
    }

    public async Task<Dictionary<string, object>> GetPreviewDataAsync(
        string sourceConnectionString, string sourceProvider,
        string targetConnectionString, string targetProvider,
        string sourceTable, string targetTable,
        List<string>? sourceFields, List<string>? targetFields, List<string>? fieldRoles,
        List<string>? filterColumn, List<string>? filterOperator, List<string>? filterValue)
    {
        // אימות אבטחה: שמות הטבלאות חייבים להיות קיימים בקטלוג המערכת לפני בניית שאילתה
        if (!await IsTableValidAsync(sourceConnectionString, sourceProvider, sourceTable))
            throw new ArgumentException("שם טבלת המקור אינו תקין או שאינו קיים במערכת.");
        if (!await IsTableValidAsync(targetConnectionString, targetProvider, targetTable))
            throw new ArgumentException("שם טבלת היעד אינו תקין או שאינו קיים במערכת.");

        var validSqlCols = await GetColumnsAsync(sourceConnectionString, sourceProvider, sourceTable);
        var validOracleCols = await GetColumnsAsync(targetConnectionString, targetProvider, targetTable);

        // שליפת טיפוסי הנתונים - נדרשים גם להמרת ערכי הסינון וגם לפאנל האבחון
        var sqlColumnTypes = await GetColumnsWithTypesAsync(sourceConnectionString, sourceProvider, sourceTable);
        var oracleColumnTypes = await GetColumnsWithTypesAsync(targetConnectionString, targetProvider, targetTable);

        // בניית רשימת העמודות לתצוגה לפי המיפוי שהמשתמש הגדיר
        var sqlFields = BuildPreviewFields(sourceFields, validSqlCols);
        var oracleFields = BuildPreviewFields(targetFields, validOracleCols);

        // איתור עמודות המפתח לצורך מיון דטרמיניסטי (ORDER BY) בשני הצדדים
        var sqlKeyFields = BuildPreviewKeyFields(sourceFields, fieldRoles, validSqlCols, sqlFields);
        var oracleKeyFields = BuildPreviewKeyFields(targetFields, fieldRoles, validOracleCols, oracleFields);

        // תרגום עמודות הסינון לשמות היעד לפי טבלת המיפוי
        var targetFilterColumn = TranslateFilterColumns(filterColumn, sourceFields, targetFields);

        // בניית תנאי סינון פרמטריים בנפרד לכל ספק
        var sqlFilter = BuildFilterClause(sourceProvider, validSqlCols, sqlColumnTypes, filterColumn, filterOperator, filterValue);
        var oracleFilter = BuildFilterClause(targetProvider, validOracleCols, oracleColumnTypes, targetFilterColumn, filterOperator, filterValue);

        string GetQuery(string provider, string table, IEnumerable<string> fields, IEnumerable<string> keyFields, FilterClause filter)
        {
            var distinctFields = fields.Distinct().ToList();
            var distinctKeys = keyFields.Distinct().ToList();

            if (provider == "SQLServer") {
                string sel = string.Join(", ", distinctFields.Select(c => $"[{c}]"));
                // מיון דטרמיניסטי מונע החזרת שורות מקריות שאינן ניתנות להשוואה בין הצדדים
                string orderClause = distinctKeys.Count > 0
                    ? " ORDER BY " + string.Join(", ", distinctKeys.Select(c => $"[{c}]"))
                    : string.Empty;
                return $"SELECT TOP (10) {sel} FROM [{table}]{filter.WhereClause}{orderClause}";
            } else {
                string sel = string.Join(", ", distinctFields.Select(c => $"\"{c}\""));
                // באורקל מקטע FETCH FIRST חייב לבוא לאחר מקטע ה-ORDER BY
                string orderClause = distinctKeys.Count > 0
                    ? " ORDER BY " + string.Join(", ", distinctKeys.Select(c => $"\"{c}\""))
                    : string.Empty;
                return $"SELECT {sel} FROM \"{table}\"{filter.WhereClause}{orderClause} FETCH FIRST 10 ROWS ONLY";
            }
        }

        string sqlQuery = GetQuery(sourceProvider, sourceTable, sqlFields, sqlKeyFields, sqlFilter);
        string oracleQuery = GetQuery(targetProvider, targetTable, oracleFields, oracleKeyFields, oracleFilter);

        var sqlData = await ExecuteRowQueryAsync(
            sourceConnectionString, sourceProvider, sqlQuery, sqlFilter, stringifyValues: true);

        var oracleData = await ExecuteRowQueryAsync(
            targetConnectionString, targetProvider, oracleQuery, oracleFilter, stringifyValues: true);

        // בדיקת אבחון: האם קיימות שורות בטבלה כלל, ללא קשר לתנאי הסינון.
        // זהו המפריד הקריטי בין "הסינון סינן הכול" לבין "הטבלה ריקה או אינה נגישה".
        bool sqlHasAnyRows = await HasAnyRowsAsync(sourceConnectionString, sourceProvider, sourceTable);
        bool oracleHasAnyRows = await HasAnyRowsAsync(targetConnectionString, targetProvider, targetTable);

        // איסוף טיפוסי הנתונים של עמודות הסינון משני הצדדים לצורך אבחון הפרשי המרה.
        // טיפוסי העמודות נשלפו כבר בראש הפונקציה ולכן אין צורך בפנייה חוזרת למסדים.
        var filterDiagnostics = new List<Dictionary<string, object>>();
        if (filterColumn != null && filterColumn.Count > 0)
        {
            for (int i = 0; i < filterColumn.Count; i++)
            {
                string fCol = filterColumn[i];
                string fOp = (filterOperator != null && filterOperator.Count > i) ? filterOperator[i] : "Equals";
                string fVal = (filterValue != null && filterValue.Count > i) ? filterValue[i] : "";

                // דילוג על שורות סינון ריקות - זהה לתנאי הדילוג בבניית השאילתה
                if (string.IsNullOrWhiteSpace(fCol) || string.IsNullOrWhiteSpace(fVal)) continue;

                // שם העמודה המקבילה בצד היעד לפי טבלת המיפוי של המשתמש
                string targetCol = (targetFilterColumn != null && targetFilterColumn.Count > i) ? targetFilterColumn[i] : fCol;

                var sqlType = sqlColumnTypes.FirstOrDefault(c => string.Equals(c.ColumnName, fCol, StringComparison.OrdinalIgnoreCase));
                var oracleType = oracleColumnTypes.FirstOrDefault(c => string.Equals(c.ColumnName, targetCol, StringComparison.OrdinalIgnoreCase));

                filterDiagnostics.Add(new Dictionary<string, object>
                {
                    { "columnName", string.Equals(fCol, targetCol, StringComparison.OrdinalIgnoreCase) ? fCol : $"{fCol} / {targetCol}" },
                    { "sourceDataType", sqlType.ColumnName != null ? sqlType.DataType : "העמודה לא נמצאה במקור" },
                    { "targetDataType", oracleType.ColumnName != null ? oracleType.DataType : "העמודה לא נמצאה ביעד" },
                    { "filterOperator", fOp },
                    { "filterValue", fVal }
                });
            }
        }

        return new Dictionary<string, object> {
            { "sqlColumns", sqlFields },
            { "oracleColumns", oracleFields },
            { "sqlData", sqlData },
            { "oracleData", oracleData },
            // נתוני אבחון - טקסט השאילתות מסונן בבקר ומוחזר למנהלי מערכת בלבד
            { "sqlQuery", sqlQuery },
            { "oracleQuery", oracleQuery },
            { "sqlRowCount", sqlData.Count },
            { "oracleRowCount", oracleData.Count },
            { "sqlHasAnyRows", sqlHasAnyRows },
            { "oracleHasAnyRows", oracleHasAnyRows },
            { "filterDiagnostics", filterDiagnostics }
        };
    }

    // הרצת שאילתת שליפה מול כל אחד מהספקים והחזרת השורות כמילונים.
    //
    // עד כה אותן עשרים שורות קוד הופיעו ארבע פעמים (מקור/יעד × תצוגה מקדימה/השוואה),
    // ושינוי שנעשה באחת מהן היה עלול לפסוח על השאר - כלומר שני צדדי ההשוואה
    // היו יכולים להיקרא בשתי דרכים שונות בלי שאיש ישים לב.
    //
    // stringifyValues מבדיל בין שני הצרכנים: התצוגה המקדימה מציגה טקסט ולכן
    // ממירה מיד, וההשוואה שומרת את הערך הגולמי כדי שהנרמול לפי טיפוס
    // (מספר/תאריך) יקבל את הטיפוס המקורי ולא מחרוזת שכבר עוצבה.
    private static async Task<List<Dictionary<string, object>>> ExecuteRowQueryAsync(
        string connectionString, string provider, string query, FilterClause filter, bool stringifyValues)
    {
        var rows = new List<Dictionary<string, object>>();

        async Task ReadAllAsync(System.Data.Common.DbDataReader reader)
        {
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    object value = reader.GetValue(i);
                    row[reader.GetName(i)] = stringifyValues ? (value?.ToString() ?? string.Empty) : value;
                }
                rows.Add(row);
            }
        }

        if (provider == "SQLServer")
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                {
                    // צירוף פרמטרי הסינון המוקלדים לפקודה
                    ApplyFilterParameters(command, filter);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        await ReadAllAsync(reader);
                    }
                }
            }
        }
        else
        {
            using (var connection = new OracleConnection(connectionString))
            {
                await connection.OpenAsync();
                using (var command = new OracleCommand(query, connection))
                {
                    // צירוף פרמטרי הסינון המוקלדים לפקודה
                    ApplyFilterParameters(command, filter);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        await ReadAllAsync(reader);
                    }
                }
            }
        }

        return rows;
    }

    // בדיקת אבחון מהירה (O(1)) המוודאת האם קיימת לפחות שורה אחת בטבלה, ללא תנאי סינון.
    // נמנעים במכוון מ-COUNT(*) שעלול להיתקע על טבלאות גדולות.
    private async Task<bool> HasAnyRowsAsync(string connectionString, string provider, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return false;

        try
        {
            if (provider == "SQLServer")
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT TOP (1) 1 FROM [{tableName}]";
                    using (var command = new SqlCommand(query, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        return result != null;
                    }
                }
            }
            else if (provider == "Oracle")
            {
                using (var connection = new OracleConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT 1 FROM \"{tableName}\" FETCH FIRST 1 ROWS ONLY";
                    using (var command = new OracleCommand(query, connection))
                    {
                        // הגדרת פסק זמן לשאילתה על מנת למנוע תקיעות שרת מול אורקל ישן
                        command.CommandTimeout = 15;
                        var result = await command.ExecuteScalarAsync();
                        return result != null;
                    }
                }
            }
        }
        catch
        {
            // כישלון בבדיקת האבחון לא יפיל את התצוגה המקדימה עצמה
            return false;
        }

        return false;
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
        int maxRows,
        string filterActive = "false",
        List<string>? filterColumn = null,
        List<string>? filterOperator = null,
        List<string>? filterValue = null,
        List<string>? valueKinds = null,
        List<CompositeFieldDefinition>? compositeFields = null)
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

        // איסוף העמודות הנדרשות לשדות המחושבים ואימותן מול קטלוג המערכת.
        // בלי זה העמודות המרכיבות לא היו נשלפות כלל והשדה המחושב היה יוצא ריק.
        var compositeSourceColumns = new List<string>();
        var compositeTargetColumns = new List<string>();
        if (compositeFields != null)
        {
            foreach (var composite in compositeFields)
            {
                foreach (var column in composite.SourceColumns)
                {
                    if (!validSqlCols.Contains(column))
                        throw new ArgumentException($"שם עמודת המקור '{column}' בשדה המחושב '{composite.LogicalName}' אינו חוקי או אינו קיים בטבלת המקור.");
                    compositeSourceColumns.Add(column);
                }
                foreach (var column in composite.TargetColumns)
                {
                    if (!validOracleCols.Contains(column))
                        throw new ArgumentException($"שם עמודת היעד '{column}' בשדה המחושב '{composite.LogicalName}' אינו חוקי או אינו קיים בטבלת היעד.");
                    compositeTargetColumns.Add(column);
                }
            }
        }

        // שדה מחושב שסומן כמפתח הוא מפתח לכל דבר. הוא נוסף לרשימות המיפוי
        // בשלב הנרמול, בתוך מנוע ההשוואה - כלומר אחרי הנקודה הזו - ולכן בדיקה
        // שמסתכלת על fieldRoles בלבד לא רואה אותו.
        //
        // עד כה זה חסם לגמרי השוואת מסדים שהמפתח היחיד שלה מחושב (למשל תאריך
        // המורכב משנה/חודש/יום מול עמודת DATE), בעוד שהשוואת קבצים כן אפשרה
        // אותה - כך שאותה בדיקה בדיוק עבדה בקבצים ונכשלה במסד.
        var compositeKeys = compositeFields?.Where(c => c.Role == "Key").ToList()
            ?? new List<CompositeFieldDefinition>();

        if (keys.Count == 0 && compositeKeys.Count == 0)
        {
            throw new Exception("חובה להגדיר לפחות שדה מפתח אחד לביצוע ההשוואה.");
        }

        // עמודות המיון לשאילתה. ה-ORDER BY חייב לשקף את מפתח ההתאמה, אחרת
        // חלון ה-TOP/FETCH בכל צד מכסה טווח מפתחות אחר וכל מפתח שנפל בחלון
        // של צד אחד בלבד מדווח כחוסר מדומה. כשהמפתח מחושב, המיון נעשה לפי
        // העמודות שמרכיבות אותו - הן נושאות בדיוק את אותו סדר.
        var sqlOrderColumns = keys.Count > 0
            ? keys.Select(k => k.SqlField).ToList()
            : compositeKeys.SelectMany(c => c.SourceColumns).ToList();
        var oracleOrderColumns = keys.Count > 0
            ? keys.Select(k => k.OracleField).ToList()
            : compositeKeys.SelectMany(c => c.TargetColumns).ToList();

        // בניית תנאי הסינון בפועל - עד כה הפרמטרים התקבלו כאן ולא יושמו,
        // כך שהמשתמש ראה תצוגה מקדימה מסוננת אך קיבל השוואה על כל הטבלה.
        var sqlFilter = new FilterClause();
        var oracleFilter = new FilterClause();

        if (string.Equals(filterActive, "true", StringComparison.OrdinalIgnoreCase))
        {
            // שליפת טיפוסי הנתונים הנדרשים להמרת ערכי הסינון לטיפוס העמודה
            var sqlColumnTypes = await GetColumnsWithTypesAsync(sourceConnectionString, sourceProvider, sourceTable);
            var oracleColumnTypes = await GetColumnsWithTypesAsync(targetConnectionString, targetProvider, targetTable);

            // תרגום עמודות הסינון לשמות היעד לפי טבלת המיפוי של המשתמש
            var targetFilterColumn = TranslateFilterColumns(filterColumn, sourceFields, targetFields);

            sqlFilter = BuildFilterClause(sourceProvider, validSqlCols, sqlColumnTypes, filterColumn, filterOperator, filterValue);
            oracleFilter = BuildFilterClause(targetProvider, validOracleCols, oracleColumnTypes, targetFilterColumn, filterOperator, filterValue);
        }

        string GetSelectQuery(string provider, string table, IEnumerable<string> fields, IEnumerable<string> keyFields, int maxRowsLimit, FilterClause filter)
        {
            var distinctFields = fields.Distinct().ToList();
            var distinctKeys = keyFields.Distinct().ToList();

            if (provider == "SQLServer") {
                string selectString = string.Join(", ", distinctFields.Select(c => $"[{c}]"));
                // מיון לפי שדות המפתח: ללא ORDER BY סדר השורות אינו דטרמיניסטי,
                // ולכן כל צד היה מחזיר שורות אחרות והדוח היה מדווח פערים שאינם קיימים.
                string orderClause = " ORDER BY " + string.Join(", ", distinctKeys.Select(c => $"[{c}]"));
                return $"SELECT TOP ({maxRowsLimit}) {selectString} FROM [{table}]{filter.WhereClause}{orderClause}";
            } else {
                string selectString = string.Join(", ", distinctFields.Select(c => $"\"{c}\""));
                // באורקל מקטע FETCH FIRST חייב לבוא לאחר מקטע ה-ORDER BY
                string orderClause = " ORDER BY " + string.Join(", ", distinctKeys.Select(c => $"\"{c}\""));
                return $"SELECT {selectString} FROM \"{table}\"{filter.WhereClause}{orderClause} FETCH FIRST {maxRowsLimit} ROWS ONLY";
            }
        }

        string sqlQuery = GetSelectQuery(sourceProvider, sourceTable,
            keys.Select(k => k.SqlField).Union(compares.Select(c => c.SqlField)).Union(compositeSourceColumns),
            sqlOrderColumns, maxRows + 1, sqlFilter);
        string oracleQuery = GetSelectQuery(targetProvider, targetTable,
            keys.Select(k => k.OracleField).Union(compares.Select(c => c.OracleField)).Union(compositeTargetColumns),
            oracleOrderColumns, maxRows + 1, oracleFilter);

        // נשלפת שורה אחת מעבר לתקרה ("שורת גישוש"). כך ידוע בוודאות אם
        // הטבלה מכילה יותר שורות מהתקרה, בלי שאילתת COUNT נוספת שעלולה
        // להיתקע על טבלה גדולה. בלי זה החיתוך נוחש לפי "הוחזרו בדיוק N",
        // וטבלה שיש בה בדיוק N שורות הניבה אזהרת חיתוך שקרית.
        var sqlRawData = await ExecuteRowQueryAsync(
            sourceConnectionString, sourceProvider, sqlQuery, sqlFilter, stringifyValues: false);
        var oracleRawData = await ExecuteRowQueryAsync(
            targetConnectionString, targetProvider, oracleQuery, oracleFilter, stringifyValues: false);

        bool sourceTruncated = sqlRawData.Count > maxRows;
        bool targetTruncated = oracleRawData.Count > maxRows;
        if (sourceTruncated) sqlRawData = sqlRawData.Take(maxRows).ToList();
        if (targetTruncated) oracleRawData = oracleRawData.Take(maxRows).ToList();

        // אזהרת החיתוך מדווחת בשני מצבי הבדיקה. כאן החיתוך ידוע בוודאות
        // משורת הגישוש, ולכן מועבר כעובדה ולא כניחוש. סך השורות בטבלה
        // אינו ידוע (זו שאילתת COUNT נוספת), ולכן הוא אינו מדווח.
        return CompareInMemoryDatasets(sqlRawData, oracleRawData, sourceTable, targetTable,
            sourceFields, targetFields, fieldRoles, valueKinds, compositeFields,
            maxRowsRequested: maxRows,
            sourceTruncatedKnown: sourceTruncated,
            targetTruncatedKnown: targetTruncated);
    }

    // =====================================================================
    // תשתית נרמול נתונים לפני השוואה (שדה לוגי)
    //
    // הבעיה שזה פותר: המיפוי הקיים הוא 1:1 בלבד ומשווה מחרוזות גולמיות.
    // לכן אותו ערך בדיוק נחשב כשונה כשצורתו שונה בין המסדים -
    // "012345678" מול "12345678" (תז מול מספר עובד), "48.00" מול "48" (מספר דף),
    // ושנה/חודש/יום בשלושה שדות מול שדה תאריך אחד (שם כל השורות נופלות כחסרות).
    //
    // חוק תאימות לאחור: בטיפוס "Text" עם מיפוי 1:1 הנרמול מחזיר
    // בדיוק את ההתנהגות הקיימת (ToString().Trim()), כך שהשוואת קובץ-מול-קובץ
    // אינה משתנה כלל אלא אם המשתמש בוחר טיפוס אחר במפורש.
    // =====================================================================

    // טיפוסי הנרמול המותרים לשדה בודד (Whitelist סגור)
    private static readonly HashSet<string> AllowedValueKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Text", "Number", "Date"
    };

    // אופני ההרכבה המותרים לשדה מחושב (Whitelist סגור)
    private static readonly HashSet<string> AllowedCompositeKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "DateParts", "Date", "Concat"
    };

    // תחילית לשמות העמודות המחושבות שנוספות לשורות בזמן ריצה.
    // תחילית ייחודית מונעת התנגשות עם שם עמודה אמיתי בטבלה.
    private const string CalculatedColumnPrefix = "__CALC__";

    // פורמטי תאריך טקסטואליים שנבדקים במדויק ולפי סדר.
    // יום-חודש-שנה מופיע לפני חודש-יום-שנה מפני שזה הכתיב המקובל אצלנו.
    private static readonly string[] TextDateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
        "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm",
        "yyyy-MM-ddTHH:mm:ss", "MM/dd/yyyy"
    };

    // =====================================================================
    // זיהוי אוטומטי של טיפוס ההשוואה.
    //
    // הסיבה שזה נדרש: ברירת המחדל של בורר הטיפוס הייתה "טקסט", ולכן
    // 160 מול 160.0 דווח כפער עד שהמשתמש שינה ידנית כל שדה. בטבלה עם
    // ארבעים עמודות אלו ארבעים בחירות ידניות בכל השוואה, וכל שכחה
    // מייצרת פער מדווח שאינו קיים בנתונים.
    //
    // הזיהוי הוא הצעת ברירת מחדל בלבד - בורר הטיפוס נשאר במסך,
    // והמשתמש יכול לעקוף אותו בכל שדה.
    // =====================================================================

    // הצעת טיפוס השוואה לפי טיפוסי הנתונים של העמודה בשני המסדים.
    // מספר או תאריך מוצעים רק כששני הצדדים מסכימים על אותה משפחת טיפוסים;
    // אחרת נשארים בטקסט, שהוא ההתנהגות השמרנית שאינה מנרמלת דבר.
    public static string SuggestValueKind(string? sourceDataType, string? targetDataType)
    {
        string source = sourceDataType ?? string.Empty;
        string target = targetDataType ?? string.Empty;

        if (IsNumericDataType(source) && IsNumericDataType(target)) return "Number";
        if (IsDateDataType(source) && IsDateDataType(target)) return "Date";

        // עמודה שקיימת בצד אחד בלבד - נשפוט לפי הצד שקיים בלבד
        bool sourceKnown = IsNumericDataType(source) || IsDateDataType(source);
        bool targetKnown = IsNumericDataType(target) || IsDateDataType(target);

        if (sourceKnown && !targetKnown)
        {
            return IsNumericDataType(source) ? "Number" : "Date";
        }
        if (targetKnown && !sourceKnown)
        {
            return IsNumericDataType(target) ? "Number" : "Date";
        }

        return "Text";
    }

    // הצעת טיפוס השוואה עבור קובץ, שאין לו סכימה - על בסיס מדגם ערכים.
    // הכלל מחמיר בכוונה: די בערך אחד שאינו מספר כדי לפסול "מספר",
    // שכן נרמול מספרי על עמודה טקסטואלית עלול להשוות ערכים שונים.
    public static string DetectValueKindFromSamples(IEnumerable<string?> samples)
    {
        int considered = 0;
        bool allNumeric = true;
        bool allDate = true;

        foreach (var sample in samples)
        {
            if (string.IsNullOrWhiteSpace(sample)) continue;
            string value = sample.Trim();

            // "NULL" כטקסט מטופל כערך חסר, בעקביות עם NormalizeValue
            if (string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase)) continue;

            considered++;

            if (allNumeric && !decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                allNumeric = false;
            }

            if (allDate)
            {
                bool parsed = DateTime.TryParseExact(value, TextDateFormats,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AllowWhiteSpaces, out _)
                    || DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AllowWhiteSpaces, out _);
                if (!parsed) allDate = false;
            }

            if (!allNumeric && !allDate) break;
        }

        // עמודה ריקה לחלוטין אינה מספיקה כדי להחליט
        if (considered == 0) return "Text";

        // מספר נבדק לפני תאריך: מחרוזת בת שמונה ספרות כמו "20260913" תקפה
        // בשני הפירושים, והפירוש המספרי הוא השמרני מביניהם
        if (allNumeric) return "Number";
        if (allDate) return "Date";
        return "Text";
    }

    // המרת ערך בודד לצורתו המנורמלת לצורך השוואה
    private static string NormalizeTypedValue(object? raw, string kind)
    {
        if (raw == null || raw == DBNull.Value) return string.Empty;

        // ערך שהגיע כטיפוס אמיתי (ולא כמחרוזת) מפורמט ישירות מהטיפוס.
        // חובה לעשות זאת לפני ToString: המרה של DateTime או decimal למחרוזת
        // תלויה בתרבות התהליך, ומיד לאחריה הפירסור מתבצע ב-InvariantCulture.
        // כך תאריך שהגיע כ-DateTime הפך תחת he-IL ל-"13/09/2026 00:00:00",
        // הפירסור ב-invariant נכשל על 13 כחודש, והנרמול לא בוצע בפועל על אף
        // תאריך אמיתי - וזה בדיוק הטיפוס שמסדי הנתונים ו-MiniExcel מחזירים.
        if (kind == "Date")
        {
            if (raw is DateTime typedDate)
                return typedDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            if (raw is DateTimeOffset typedOffset)
                return typedOffset.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (kind == "Number")
        {
            if (raw is decimal typedDecimal)
                return typedDecimal.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);

            // bool מטופל במפורש: העמודה bit נחשבת מספרית, ולכן טיפוס ההשוואה
            // המוצע לה הוא "מספר". SqlDataReader מחזיר עבורה bool, וללא הטיפול
            // הזה היא נפלה למסלול הטקסטואלי והפכה ל-"True" מול "1" של אורקל -
            // כלומר כל שורה בעמודת bit דווחה כשונה, ומעל עשר שורות היא אף
            // סומנה כ"הבדל שיטתי". מיפוי bit ל-NUMBER(1) הוא מהנפוצים במיגרציה.
            if (raw is bool typedBool)
                return typedBool ? "1" : "0";

            if (raw is double or float or int or long or short or byte or sbyte or uint or ulong or ushort)
            {
                // המרה מוגנת: double מחוץ לתחום decimal, NaN או Infinity זרקו
                // OverflowException שהפיל את כל ההשוואה עם הודעה שאינה מציינת
                // את שם העמודה. עמודות real ו-BINARY_DOUBLE הן בדיוק אלה
                // שטיפוס ההשוואה המוצע להן הוא "מספר".
                try
                {
                    return Convert.ToDecimal(raw, System.Globalization.CultureInfo.InvariantCulture)
                        .ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    // ערך שאינו מיוצג ב-decimal מושווה כטקסט ולא מפיל את ההשוואה
                    return raw.ToString()?.Trim() ?? string.Empty;
                }
            }
        }

        string text = raw.ToString()?.Trim() ?? string.Empty;
        if (text.Length == 0) return string.Empty;

        if (kind == "Number")
        {
            // נרמול מספרי: מבטל אפסים מובילים, אפסים עוקבים אחרי הנקודה והפרשי scale.
            // כך "012345678" ו-"12345678" משתווים, וכן "48.00" ו-"48".
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return number.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
            }
            // ערך שאינו מספרי נשאר כטקסט ולא מפיל את ההשוואה
            return text;
        }

        if (kind == "Date")
        {
            // נרמול תאריך לפורמט אחיד yyyyMMdd: מבטל הפרשי פורמט
            // ואת שברי השנייה שאורקל DATE אינו שומר.
            // הפורמטים המדויקים נבדקים ראשונים ולפי סדר, כדי שתאריך בכתיב
            // יום-חודש-שנה לא יתפרש כחודש-יום-שנה: "13/09/2026" ו-"09/13/2026"
            // שניהם חוקיים תחבירית, ורק סדר הבדיקה קובע איזה פירוש נבחר.
            if (DateTime.TryParseExact(text, TextDateFormats,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var exactDate))
            {
                return exactDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var date))
            {
                return date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            }
            // תאריך שנשמר כמספר בן שמונה ספרות בצורת yyyyMMdd נחשב תקין כפי שהוא
            if (text.Length == 8 && text.All(char.IsDigit)) return text;
            return text;
        }

        // Text - בדיוק ההתנהגות הקיימת בפרויקט
        return text;
    }

    // הרכבת הערך של שדה מחושב מתוך שורה בודדת
    private static string BuildCompositeValue(Dictionary<string, object> row, List<string> columns, string kind)
    {
        if (columns == null || columns.Count == 0) return string.Empty;

        if (kind == "DateParts")
        {
            // שלוש עמודות בסדר שנה, חודש, יום -> yyyyMMdd עם ריפוד אפסים.
            // הריפוד הוא מה שמשווה בין חודש "9" לחודש "09".
            var parts = new List<int>();
            foreach (var column in columns)
            {
                row.TryGetValue(column, out var raw);
                string text = raw?.ToString()?.Trim() ?? string.Empty;
                if (!int.TryParse(text, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out int part))
                {
                    // חלק חסר או שאינו מספרי - הערך אינו בר-השוואה
                    return string.Empty;
                }
                parts.Add(part);
            }

            if (parts.Count == 3)
            {
                // תמיכה בשנה דו-ספרתית (למשל 26 מתפרש כ-2026)
                int year = parts[0] < 100 ? 2000 + parts[0] : parts[0];
                return $"{year:D4}{parts[1]:D2}{parts[2]:D2}";
            }

            return string.Join(string.Empty,
                parts.Select(p => p.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (kind == "Date")
        {
            row.TryGetValue(columns[0], out var raw);
            return NormalizeTypedValue(raw, "Date");
        }

        // Concat - שרשור הערכים המנורמלים. המפריד הוא תו בקרה שאינו יכול
        // להופיע בנתונים, כדי למנוע התנגשות בין צירופים שונים.
        return string.Join(KeyPartWildcard, columns.Select(c =>
        {
            row.TryGetValue(c, out var raw);
            return NormalizeTypedValue(raw, "Text");
        }));
    }

    // מעבר הנרמול המרכזי: מריץ המרת ערכים לפי טיפוס ומוסיף עמודות מחושבות לשורות,
    // ומחזיר את רשימות המיפוי האפקטיביות שהמנוע יעבוד מולן.
    // השדות המחושבים נוספים כמיפוי 1:1 רגיל, ולכן מנוע ההשוואה עצמו אינו משתנה.
    private static (List<string> SourceFields, List<string> TargetFields, List<string> FieldRoles) ApplyNormalization(
        List<Dictionary<string, object>> sourceRows,
        List<Dictionary<string, object>> targetRows,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        List<string>? valueKinds,
        List<CompositeFieldDefinition>? compositeFields)
    {
        var effectiveSource = new List<string>(sourceFields);
        var effectiveTarget = new List<string>(targetFields);
        var effectiveRoles = new List<string>(fieldRoles);

        // שלב א: נרמול ערכים לפי טיפוס השדה.
        // שדה בטיפוס Text נותר ללא שינוי כדי לשמר את ההתנהגות הקיימת.
        if (valueKinds != null)
        {
            for (int i = 0; i < effectiveSource.Count && i < valueKinds.Count; i++)
            {
                string kind = valueKinds[i];
                if (!AllowedValueKinds.Contains(kind) || kind == "Text") continue;

                string sourceColumn = effectiveSource[i];
                string targetColumn = i < effectiveTarget.Count ? effectiveTarget[i] : sourceColumn;

                foreach (var row in sourceRows)
                {
                    if (row.TryGetValue(sourceColumn, out var raw))
                        row[sourceColumn] = NormalizeTypedValue(raw, kind);
                }
                foreach (var row in targetRows)
                {
                    if (row.TryGetValue(targetColumn, out var raw))
                        row[targetColumn] = NormalizeTypedValue(raw, kind);
                }
            }
        }

        // שלב ב: הוספת שדות מחושבים כעמודות חדשות בכל שורה, בשני הצדדים
        if (compositeFields != null)
        {
            foreach (var composite in compositeFields)
            {
                if (string.IsNullOrWhiteSpace(composite.LogicalName)) continue;
                if (composite.SourceColumns.Count == 0 || composite.TargetColumns.Count == 0) continue;
                if (!AllowedCompositeKinds.Contains(composite.SourceKind)) continue;
                if (!AllowedCompositeKinds.Contains(composite.TargetKind)) continue;

                string calculatedColumn = CalculatedColumnPrefix + composite.LogicalName;

                foreach (var row in sourceRows)
                    row[calculatedColumn] = BuildCompositeValue(row, composite.SourceColumns, composite.SourceKind);

                foreach (var row in targetRows)
                    row[calculatedColumn] = BuildCompositeValue(row, composite.TargetColumns, composite.TargetKind);

                // רישום השדה המחושב כמיפוי רגיל - אותו שם עמודה בשני הצדדים
                effectiveSource.Add(calculatedColumn);
                effectiveTarget.Add(calculatedColumn);
                effectiveRoles.Add(composite.Role == "Key" ? "Key" : "Compare");
            }
        }

        return (effectiveSource, effectiveTarget, effectiveRoles);
    }

    // הסרת התחילית הפנימית משם עמודה מחושבת, לצורך תצוגה קריאה בדוח
    private static string DisplayFieldName(string fieldName)
    {
        return fieldName.StartsWith(CalculatedColumnPrefix, StringComparison.Ordinal)
            ? fieldName.Substring(CalculatedColumnPrefix.Length)
            : fieldName;
    }

    // escaping לחלק של מפתח מורכב.
    // ערך שמכיל את תו המפריד היה גורם להתנגשות בין מפתחות שונים -
    // למשל ("A|B","C") ו-("A","B|C") היו מייצרים את אותו מפתח בדיוק.
    private static string EscapeKeyPart(string part)
    {
        return part.Replace("\\", "\\\\").Replace("|", "\\|");
    }

    // היפוך ה-escaping, לצורך הצגת הערך המקורי של רכיב מפתח בדוח
    private static string UnescapeKeyPart(string part)
    {
        var builder = new System.Text.StringBuilder(part.Length);
        for (int i = 0; i < part.Length; i++)
        {
            if (part[i] == '\\' && i + 1 < part.Length)
            {
                builder.Append(part[++i]);
                continue;
            }
            builder.Append(part[i]);
        }
        return builder.ToString();
    }

    // תו סימון פנימי המחליף רכיב מפתח בעת חיפוש שורה "כמעט זהה".
    // תו בקרה שאינו יכול להופיע בנתונים עצמם, ולכן אינו מתנגש בערך אמיתי.
    private const string KeyPartWildcard = "\u0001";

    // שליפת רכיבי המפתח הגולמיים של שורה, לפי צד ההשוואה המבוקש
    private static List<string> KeyPartsOf(
        Dictionary<string, object> row,
        List<(string SqlField, string OracleField)> keys,
        bool useSourceSide)
    {
        var parts = new List<string>(keys.Count);
        foreach (var k in keys)
        {
            string column = useSourceSide ? k.SqlField : k.OracleField;
            string part = row.TryGetValue(column, out var value)
                ? value?.ToString()?.Trim() ?? string.Empty
                : string.Empty;

            // ערך חסר מיוצג במחרוזת ריקה אחת בלבד, ולא פעם "NULL" ופעם ריק.
            //
            // הייצוא מ-SSMS כותב לקובץ את הטקסט NULL, וייצוא מאורקל כותב תא
            // ריק. בלי הנרמול הזה אותה שורה בדיוק קיבלה מפתח "41|NULL" בצד
            // אחד ו-"41|" בצד השני, ודווחה גם כחסרה ביעד וגם כחסרה במקור -
            // עם אבחון "המפתח אינו קיים כלל", למרות שכל שדות ההשוואה זהים.
            // זה גם מיישר את בניית המפתח עם NormalizeValue, שכבר מתייחסת
            // לטקסט NULL כערך חסר בשדות ההשוואה.
            if (part.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                part = string.Empty;

            parts.Add(part);
        }
        return parts;
    }

    // בניית התיאור הקריא של מפתח מורכב: כל רכיב מוצג עם שם העמודה שלו,
    // למשל "DAF=41 | DAF_NOSAF=1 | YOM=2 | HODESH=9 | SHANA=2025".
    private static string BuildKeyDisplay(
        List<(string SqlField, string OracleField)> keys,
        List<string> rawParts,
        bool useSourceSide)
    {
        var labelled = new List<string>(keys.Count);
        for (int i = 0; i < keys.Count && i < rawParts.Count; i++)
        {
            string column = useSourceSide ? keys[i].SqlField : keys[i].OracleField;
            string value = string.IsNullOrEmpty(rawParts[i]) ? "(ריק)" : rawParts[i];
            labelled.Add($"{DisplayFieldName(column)}={value}");
        }
        return string.Join(" | ", labelled);
    }

    // מפתח מורכב שבו רכיב אחד הוחלף בתו סימון, לצורך חיפוש שורה שזהה בכל
    // רכיבי המפתח למעט אותו רכיב.
    private static string MaskedKey(List<string> escapedParts, int skipIndex)
    {
        return string.Join("|", escapedParts.Select((part, i) => i == skipIndex ? KeyPartWildcard : part));
    }

    // מופע בודד של שורה בתוך קבוצת מפתח.
    // בעבר נשמרה השורה הראשונה בלבד לכל מפתח וכל שאר המופעים רק נספרו ולא הושוו,
    // ולכן מפתח שחזר (למשל כמה אירועים באותו דף ואותו קוד אירוע) הוחרג מההשוואה.
    private sealed class RowOccurrence
    {
        // השורה עצמה כפי שנטענה
        public Dictionary<string, object> Row = null!;

        // מספר השורה בנתונים שנטענו, 1 = הראשונה. משמש לזיהוי בדוח ולמיון יציב.
        public int RowNumber;

        // ערכי שדות ההשוואה המנורמלים, לפי סדר compares
        public string[] CompareValues = Array.Empty<string>();

        // שרשור CompareValues - המפתח שלפיו מזווגים מופעים זהים
        public string CompareSignature = string.Empty;

        // המופע בצד השני שאליו שויך, null כל עוד לא שויך
        public RowOccurrence? PairedWith;
    }

    // כל המופעים של מפתח מורכב אחד, בצד אחד
    private sealed class KeyGroup
    {
        public string CompositeKey = string.Empty;

        // רכיבי המפתח לאחר escaping - נדרשים לאבחון "כמעט זהה"
        public List<string> EscapedParts = new();

        // רכיבי המפתח הגולמיים - נדרשים לתצוגה ולעמודות מפתח נפרדות
        public List<string> RawParts = new();

        public List<RowOccurrence> Occurrences = new();
    }

    // תקרת תאים במטריצת הזיווג לקבוצת מפתח בודדת (50x50).
    // מעליה עוברים לזיווג לפי סדר, כדי שמפתח בעל ריבוי חריג לא יתקע את הבקשה.
    private const int MaxPairingMatrixCells = 2500;

    // מספר הממצאים המקסימלי שמפורט בדוח לכל רשימה. הספירה המלאה תמיד מדווחת
    // בנפרד; זו רק תקרת פירוט, כדי שהמודל שנשלח לדפדפן לא יתנפח ללא גבול.
    private const int MaxReportedFindings = 400;

    // תקרת הפירוט לרשימות שהמשתמש עובד מולן בפועל: השורות החסרות והעודפות,
    // וההבדלים ברמת שדה. ממצא שנספר ולא פורט מחייב חיפוש ידני בקובץ המקורי -
    // בדיוק מה שהדוח אמור לחסוך - ולכן התקרה כאן גבוהה בהרבה.
    private const int MaxDetailRows = 5000;

    // תקרת מפתחות לבניית אינדקס הדמיון. מעליה האבחון מדלג, כדי שטבלה ענקית
    // לא תשלם בזיכרון על מידע עזר.
    private const int MaxKeysForSimilarityIndex = 60000;

    // כמה ערכים מוצגים בהערה על רכיב מפתח שחסר בצד אחד, וכמה הערות בסך הכול
    private const int MaxValuesPerKeyGap = 12;
    private const int MaxKeyValueGapNotes = 12;

    // תקרת פעולות לסריקת הדמיון המלאה (שורות חסרות כפול מפתחות בצד השני).
    // מתחתיה אפשר לדרג כל שורה חסרה מול כל מפתח ולדעת כמה רכיבים תאמו.
    private const long MaxSimilarityScanOperations = 20_000_000;

    // תקרות פירוט לרשימות הנוספות. הספירה המלאה מדווחת בנפרד בכל מקרה.
    private const int MaxReportedDuplicates = 200;
    private const int MaxReportedGaps = 200;

    // מספר השורות המינימלי שדרוש כדי לסמן שדה כ"הבדל שיטתי".
    // בלי רצפה כזו, בהשוואה שבה נמצאה שורה אחת שונה, השדה שלה היה מסומן
    // שיטתי - כי אחת מתוך אחת היא מאה אחוז - וזו קביעה חסרת בסיס.
    private const int MinRowsForSystematicField = 10;

    // אינדקס לכל מיקום ברכיבי המפתח: מפתח ממוסך -> המפתח המורכב הראשון שנמצא.
    // מאפשר לאתר בעלות O(1) שורה בצד השני שנבדלת ברכיב מפתח אחד בלבד.
    private static List<Dictionary<string, string>> BuildMaskedKeyIndex(
        Dictionary<string, List<string>> keyPartsByComposite,
        int keyCount)
    {
        var index = new List<Dictionary<string, string>>(keyCount);
        for (int i = 0; i < keyCount; i++)
            index.Add(new Dictionary<string, string>());

        foreach (var entry in keyPartsByComposite)
        {
            for (int i = 0; i < keyCount && i < entry.Value.Count; i++)
            {
                string masked = MaskedKey(entry.Value, i);
                if (!index[i].ContainsKey(masked))
                    index[i][masked] = entry.Key;
            }
        }

        return index;
    }

    // האם רכיב מפתח ריק בפועל. שורה שכל רכיבי המפתח שלה ריקים אינה שורת נתונים -
    // ייצוא אקסל מייצר שורות מעוצבות אך ריקות בסוף הגיליון, וללא הכלל הזה כולן
    // מתקפלות למפתח אחד ומדווחות כעשרות כפילויות ושורות עודפות.
    private static bool IsBlankKeyPart(string part)
    {
        return string.IsNullOrWhiteSpace(part) || part.Equals("NULL", StringComparison.OrdinalIgnoreCase);
    }

    // בניית אינדקס קבוצות המפתח לצד אחד: כל המופעים של כל מפתח, ולא רק הראשון.
    // מחזיר גם את מספר השורות שכל רכיבי המפתח שלהן ריקים, שהוחרגו מהאינדקס.
    private Dictionary<string, KeyGroup> BuildKeyGroups(
        List<Dictionary<string, object>> rows,
        List<(string SqlField, string OracleField)> keys,
        List<(string SqlField, string OracleField)> compares,
        bool useSourceSide,
        Dictionary<string, string> keyDisplay,
        out int emptyKeyRows)
    {
        var groups = new Dictionary<string, KeyGroup>(StringComparer.Ordinal);
        emptyKeyRows = 0;

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rawParts = KeyPartsOf(row, keys, useSourceSide);

            // שורה שכל רכיבי המפתח שלה ריקים אינה נכנסת להשוואה
            if (rawParts.Count > 0 && rawParts.All(IsBlankKeyPart))
            {
                emptyKeyRows++;
                continue;
            }

            var escapedParts = rawParts.Select(EscapeKeyPart).ToList();
            string compositeKey = string.Join("|", escapedParts);

            if (!keyDisplay.ContainsKey(compositeKey))
                keyDisplay[compositeKey] = BuildKeyDisplay(keys, rawParts, useSourceSide);

            if (!groups.TryGetValue(compositeKey, out var group))
            {
                group = new KeyGroup
                {
                    CompositeKey = compositeKey,
                    EscapedParts = escapedParts,
                    RawParts = rawParts
                };
                groups[compositeKey] = group;
            }

            // ערכי ההשוואה מנורמלים באותה פונקציה שבה משתמשת בדיקת השוויון עצמה,
            // כדי שזהות חתימה תהיה שקולה בהכרח ל"כל שדות ההשוואה זהים".
            var compareValues = new string[compares.Count];
            for (int i = 0; i < compares.Count; i++)
            {
                string column = useSourceSide ? compares[i].SqlField : compares[i].OracleField;
                row.TryGetValue(column, out var value);
                compareValues[i] = NormalizeValue(value);
            }

            group.Occurrences.Add(new RowOccurrence
            {
                Row = row,
                RowNumber = rowIndex + 1,
                CompareValues = compareValues,
                CompareSignature = string.Join(KeyPartWildcard, compareValues)
            });
        }

        return groups;
    }

    // האם אותה שורה בדיוק (אותה חתימת ערכים) חוזרת יותר מפעם אחת באותו צד.
    // מבדיל בין "אותו מפתח עם נתונים שונים" לבין שורה כפולה ממש.
    private static bool HasIdenticalSignatureRepeat(KeyGroup? group)
    {
        if (group == null || group.Occurrences.Count < 2) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in group.Occurrences)
        {
            if (!seen.Add(occurrence.CompareSignature)) return true;
        }
        return false;
    }

    // מספר שדות ההשוואה שנמצאו שונים בין שני מופעים
    private static int CountDifferingFields(RowOccurrence a, RowOccurrence b)
    {
        int diff = 0;
        int len = Math.Min(a.CompareValues.Length, b.CompareValues.Length);
        for (int i = 0; i < len; i++)
        {
            if (!string.Equals(a.CompareValues[i], b.CompareValues[i], StringComparison.Ordinal))
                diff++;
        }
        return diff;
    }

    // זיווג המופעים של מפתח אחד בין שני הצדדים, בשני מעברים:
    //
    // מעבר 1 - התאמה לפי חתימה: מופעים שכל ערכי ההשוואה שלהם זהים מזווגים מיד.
    //           זה מה שמנטרל את השפעת סדר השורות בקובץ - אותן ארבע שורות
    //           בסדר שונה בשני הצדדים מזווגות לארבעה זוגות זהים.
    //
    // מעבר 2 - זיווג השארית לפי מספר השדות השונים הקטן ביותר. בלי זה, זיווג
    //           לפי סדר מייצר "ממצאים" שהם למעשה פסולת של סדר השורות בייצוא.
    //
    // מה שנשאר ללא זוג הוא חוסר או תוספת אמיתיים. מספר הזוגות אינו תלוי באיכות
    // הזיווג - הוא תמיד min(מקור, יעד) - ולכן תשובת השלמות זהה בכל אופן זיווג;
    // מה שהזיווג קובע הוא כמה מהזוגות יוצאים זהים וכמה שדות מדווחים כשונים.
    private static void PairOccurrences(KeyGroup? sourceGroup, KeyGroup? targetGroup, ref int degradedGroups)
    {
        if (sourceGroup == null || targetGroup == null) return;

        var sourceRows = sourceGroup.Occurrences;
        var targetRows = targetGroup.Occurrences;

        // מעבר 1: חתימות זהות
        var targetBySignature = new Dictionary<string, Queue<RowOccurrence>>(StringComparer.Ordinal);
        foreach (var target in targetRows)
        {
            if (!targetBySignature.TryGetValue(target.CompareSignature, out var queue))
            {
                queue = new Queue<RowOccurrence>();
                targetBySignature[target.CompareSignature] = queue;
            }
            queue.Enqueue(target);
        }

        foreach (var source in sourceRows)
        {
            if (targetBySignature.TryGetValue(source.CompareSignature, out var queue) && queue.Count > 0)
            {
                var target = queue.Dequeue();
                source.PairedWith = target;
                target.PairedWith = source;
            }
        }

        // מעבר 2: זיווג השארית
        var sourceLeft = sourceRows.Where(o => o.PairedWith == null).ToList();
        var targetLeft = targetRows.Where(o => o.PairedWith == null).ToList();

        if (sourceLeft.Count == 0 || targetLeft.Count == 0) return;

        if (sourceLeft.Count == 1 && targetLeft.Count == 1)
        {
            sourceLeft[0].PairedWith = targetLeft[0];
            targetLeft[0].PairedWith = sourceLeft[0];
            return;
        }

        // מגן ביצועים: קבוצה חריגה בגודלה מזווגת לפי סדר במקום במטריצה
        if (sourceLeft.Count * targetLeft.Count > MaxPairingMatrixCells)
        {
            degradedGroups++;
            int pairs = Math.Min(sourceLeft.Count, targetLeft.Count);
            for (int i = 0; i < pairs; i++)
            {
                sourceLeft[i].PairedWith = targetLeft[i];
                targetLeft[i].PairedWith = sourceLeft[i];
            }
            return;
        }

        // מטריצת מספר השדות השונים, וזיווג חמדני מהתא הקטן ביותר כלפי מעלה.
        // שוברי שוויון לפי מספר השורה, כדי שהתוצאה תהיה דטרמיניסטית.
        int rows2 = sourceLeft.Count;
        int cols = targetLeft.Count;
        var matrix = new int[rows2, cols];
        for (int i = 0; i < rows2; i++)
            for (int j = 0; j < cols; j++)
                matrix[i, j] = CountDifferingFields(sourceLeft[i], targetLeft[j]);

        int maxPairs = Math.Min(rows2, cols);
        for (int paired = 0; paired < maxPairs; paired++)
        {
            int bestI = -1, bestJ = -1, bestDiff = int.MaxValue;
            for (int i = 0; i < rows2; i++)
            {
                if (sourceLeft[i].PairedWith != null) continue;
                for (int j = 0; j < cols; j++)
                {
                    if (targetLeft[j].PairedWith != null) continue;
                    if (matrix[i, j] < bestDiff)
                    {
                        bestDiff = matrix[i, j];
                        bestI = i;
                        bestJ = j;
                    }
                }
            }

            if (bestI < 0) break;

            sourceLeft[bestI].PairedWith = targetLeft[bestJ];
            targetLeft[bestJ].PairedWith = sourceLeft[bestI];
        }
    }

    // QA Workflow comparison engine — executes sequential validation steps (A → B → C)
    public SmartComparisonResultViewModel CompareInMemoryDatasets(
        List<Dictionary<string, object>> sqlRawData,
        List<Dictionary<string, object>> oracleRawData,
        string SourceTable,
        string TargetTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
        List<string>? valueKinds = null,
        List<CompositeFieldDefinition>? compositeFields = null,
        int maxRowsRequested = 0,
        int sourceTotalRows = 0,
        int targetTotalRows = 0,
        bool? sourceTruncatedKnown = null,
        bool? targetTruncatedKnown = null)
    {
        // מעבר נרמול לפני ההשוואה: המרת ערכים לפי טיפוס והוספת שדות מחושבים.
        // ללא טיפוסים ובלי שדות מחושבים, הרשימות חוזרות כפי שהתקבלו.
        (sourceFields, targetFields, fieldRoles) = ApplyNormalization(
            sqlRawData, oracleRawData, sourceFields, targetFields, fieldRoles, valueKinds, compositeFields);

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

        // --- קביעת מפתח ההתאמה ---
        // בחירת המפתחות במסך המיפוי היא הקובעת, ואין כאן שום עקיפה.
        //
        // בעבר הייתה כאן עקיפה קשיחה שמחקה את הבחירה והשתמשה ב-DAF + DAF_NOSAF
        // בלבד. התוצאה הייתה שדף מסוים ביום ראשון ואותו דף ביום שני נחשבו לאותה
        // שורה, ודוח החוסרים דיווח מספר דף בלי תאריך - כך שכל ממצא הצביע על
        // עשרות שורות אפשריות. העקיפה הוסרה במלואה.
        //
        // שומר מפורש במקום נפילה שקטה: בלי אף מפתח, המפתח המורכב של כל השורות
        // היה יוצא זהה, כל הטבלה הייתה מתקפלת לשורה אחת והדוח היה חסר משמעות.
        // שני נתיבי הכניסה חוסמים בקשה בלי מפתח, ולכן זו הגנה בפני שינוי עתידי.
        if (keys.Count == 0)
        {
            throw new InvalidOperationException(
                "חובה להגדיר לפחות שדה מפתח אחד להשוואה. בלי מפתח אין דרך להתאים שורות בין שני הצדדים.");
        }

        // Initialize result model with basic statistics
        var result = new SmartComparisonResultViewModel
        {
            SourceTable = SourceTable,
            TargetTable = TargetTable,
            PrimaryKeyColumn = string.Join(", ", keys.Select(k => DisplayFieldName(k.SqlField))),
            // שמות רכיבי המפתח לפי סדרם - כל ממצא בדוח נושא את הערכים באותו סדר
            KeyColumnNames = keys.Select(k => DisplayFieldName(k.SqlField)).ToList(),
            DetailRowCap = MaxDetailRows,
            TotalRowsInSource = sqlRawData.Count,
            TotalRowsInTarget = oracleRawData.Count
        };

        // תיאור קריא של כל מפתח מורכב שנראה בהשוואה, עם שמות רכיבי המפתח.
        // כל דיווח בדוח (חוסר, כפילות, פער או תבנית) מוצג דרך המילון הזה,
        // כדי שממצא יזהה שורה אחת בדיוק ולא ערך מפתח חלקי.
        var keyDisplay = new Dictionary<string, string>();

        // אינדקס קבוצות המפתח: כל המופעים של כל מפתח, בשני הצדדים.
        // בעבר נשמרה השורה הראשונה בלבד לכל מפתח, ומפתח שחזר הוחרג מההשוואה -
        // מה שהפך דוח על נתונים תקינים לדוח של 0% תאימות.
        var sqlGroups = BuildKeyGroups(sqlRawData, keys, compares, true, keyDisplay, out int sourceEmptyKeyRows);
        var oracleGroups = BuildKeyGroups(oracleRawData, keys, compares, false, keyDisplay, out int targetEmptyKeyRows);

        result.SourceEmptyKeyRows = sourceEmptyKeyRows;
        result.TargetEmptyKeyRows = targetEmptyKeyRows;

        // זיווג המופעים בתוך כל מפתח שקיים בשני הצדדים
        int pairingDegradedGroups = 0;
        foreach (var sqlGroup in sqlGroups.Values)
        {
            oracleGroups.TryGetValue(sqlGroup.CompositeKey, out var oracleGroup);
            PairOccurrences(sqlGroup, oracleGroup, ref pairingDegradedGroups);
        }
        result.PairingDegradedGroups = pairingDegradedGroups;

        // ספירות ורכיבי מפתח, נגזרים מאינדקס הקבוצות כדי שכל שלבי הדוח
        // יחושבו מאותו מקור אחד ולא יוכלו לסתור זה את זה.
        var sqlKeyCounts = sqlGroups.ToDictionary(g => g.Key, g => g.Value.Occurrences.Count, StringComparer.Ordinal);
        var oracleKeyCounts = oracleGroups.ToDictionary(g => g.Key, g => g.Value.Occurrences.Count, StringComparer.Ordinal);
        var sqlKeyParts = sqlGroups.ToDictionary(g => g.Key, g => g.Value.EscapedParts, StringComparer.Ordinal);
        var oracleKeyParts = oracleGroups.ToDictionary(g => g.Key, g => g.Value.EscapedParts, StringComparer.Ordinal);

        // תרגום מפתח מורכב לתיאור הקריא שלו, עם נפילה לערך הגולמי אם אינו מוכר
        string Describe(string compositeKey) =>
            keyDisplay.TryGetValue(compositeKey, out var display) ? display : compositeKey;

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
                    result.SourceDuplicateKeysList.Add(Describe(kvp.Key));
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
                    result.TargetDuplicateKeysList.Add(Describe(kvp.Key));
                }
            }

            // Calculate missing keys and build lists
            // Source missing keys = exists in target keys, but not in source keys
            foreach (var k in oracleKeyCounts.Keys)
            {
                if (!sqlKeyCounts.ContainsKey(k))
                {
                    result.SourceMissingKeysCount++;
                    if (result.SourceMissingKeysList.Count < MaxReportedFindings)
                    {
                        result.SourceMissingKeysList.Add(Describe(k));
                    }
                }
            }

            // Target missing keys = exists in source keys, but not in target keys
            foreach (var k in sqlKeyCounts.Keys)
            {
                if (!oracleKeyCounts.ContainsKey(k))
                {
                    result.TargetMissingKeysCount++;
                    if (result.TargetMissingKeysList.Count < MaxReportedFindings)
                    {
                        result.TargetMissingKeysList.Add(Describe(k));
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

        result.RowCountSummary = $"{SourceTable}: {sqlRawData.Count} שורות | {TargetTable}: {oracleRawData.Count} שורות";
        result.QaSteps.Add(stepA);

        // =====================================================================
        // STEP B: Primary Key Validation — identify missing IDs and duplicates
        // =====================================================================
        var stepB = new QaStepResult
        {
            StepName = "B",
            StepTitle = "זיהוי מפתחות חסרים"
        };

        // --- זיהוי מפתחות חוזרים ---
        // בעבר מפתח חוזר הוחרג מההשוואה לגמרי, וזו הייתה הסיבה העיקרית
        // ל-0% תאימות: בדוח שהתקבל בפועל 47 מפתחות יצאו מהבדיקה.
        // כיום מפתח חוזר מושווה ככל מפתח אחר, ובנוסף מדווח כממצא בפני עצמו.

        // שמות שדות ההשוואה שנמצאו נושאים ערכים שונים בין המופעים של אותו מפתח.
        // נגזר מהנתונים ולא מרשימת שמות עמודות קבועה.
        string DescribeDistinguishingFields(KeyGroup? sourceGroup, KeyGroup? targetGroup)
        {
            var names = new List<string>();
            for (int i = 0; i < compares.Count; i++)
            {
                var values = new HashSet<string>(StringComparer.Ordinal);
                if (sourceGroup != null)
                    foreach (var occurrence in sourceGroup.Occurrences) values.Add(occurrence.CompareValues[i]);
                if (targetGroup != null)
                    foreach (var occurrence in targetGroup.Occurrences) values.Add(occurrence.CompareValues[i]);
                if (values.Count > 1) names.Add(DisplayFieldName(compares[i].SqlField));
                if (names.Count == 4) break;
            }
            return string.Join(", ", names);
        }

        foreach (var compositeKey in sqlKeyCounts.Keys.Union(oracleKeyCounts.Keys))
        {
            sqlGroups.TryGetValue(compositeKey, out var sourceGroup);
            oracleGroups.TryGetValue(compositeKey, out var targetGroup);

            int sourceCount = sourceGroup?.Occurrences.Count ?? 0;
            int targetCount = targetGroup?.Occurrences.Count ?? 0;
            if (sourceCount <= 1 && targetCount <= 1) continue;

            // האם אותה שורה בדיוק חוזרת באותו צד, או שמדובר במופעים שונים תחת מפתח אחד
            bool identicalRepeat = HasIdenticalSignatureRepeat(sourceGroup) || HasIdenticalSignatureRepeat(targetGroup);

            result.TotalDuplicateFindings++;
            result.TotalDuplicateRows += sourceCount + targetCount;

            if (result.Duplicates.Count < MaxReportedDuplicates)
            {
                result.Duplicates.Add(new DuplicateKeyRecord
                {
                    KeyValue = Describe(compositeKey),
                    // רכיבי המפתח בנפרד, כדי שגם כאן תוצג עמודה לכל רכיב
                    KeyParts = new List<string>(
                        (sourceGroup ?? targetGroup)?.RawParts ?? new List<string>()),
                    SourceCount = sourceCount,
                    TargetCount = targetCount,
                    Kind = identicalRepeat ? "IdenticalRowRepeat" : "SameKeyDifferentData",
                    KindTitle = identicalRepeat
                        ? "שורה זהה לחלוטין מופיעה יותר מפעם אחת"
                        : "אותו מפתח עם נתונים שונים",
                    Severity = identicalRepeat || sourceCount != targetCount ? "Fail" : "Warning",
                    CountsBalanced = sourceCount == targetCount,
                    HasIdenticalRepeat = identicalRepeat,
                    SourceDistinctRows = sourceGroup?.Occurrences.Select(o => o.CompareSignature).Distinct(StringComparer.Ordinal).Count() ?? 0,
                    TargetDistinctRows = targetGroup?.Occurrences.Select(o => o.CompareSignature).Distinct(StringComparer.Ordinal).Count() ?? 0,
                    DistinguishingFields = DescribeDistinguishingFields(sourceGroup, targetGroup)
                });
            }
        }

        // המונה מדווח מספר ממצאים, ולא סכום מופעים משני הצדדים כפי שהיה קודם -
        // חיבור מקור ויעד לאותו מספר הוא ספירה כפולה שהקשתה על קריאת הדוח.
        result.TotalDuplicates = result.TotalDuplicateFindings;

        // --- חוסרים ועודפים ברמת המופע ---
        // מופע שלא נמצא לו זוג בצד השני הוא חוסר אמיתי. זו התשובה לשאלה
        // שדניאל הגדיר כעיקרית: שמה שיש באורקל יהיה ב-SQL בלי תוספים ובלי חוסרים.
        var unpairedSource = new List<(string Key, RowOccurrence Occurrence)>();
        var unpairedTarget = new List<(string Key, RowOccurrence Occurrence)>();

        foreach (var group in sqlGroups.Values)
            foreach (var occurrence in group.Occurrences)
                if (occurrence.PairedWith == null) unpairedSource.Add((group.CompositeKey, occurrence));

        foreach (var group in oracleGroups.Values)
            foreach (var occurrence in group.Occurrences)
                if (occurrence.PairedWith == null) unpairedTarget.Add((group.CompositeKey, occurrence));

        result.TotalMissingInTarget = unpairedSource.Count;
        result.TotalMissingInSource = unpairedTarget.Count;

        // הפרדה בין מפתח שאינו קיים כלל בצד השני לבין מפתח שקיים אך בפחות מופעים.
        // שני המקרים דורשים בדיקה שונה לגמרי, ובדוח הקודם הם התערבבו.
        result.KeysMissingInTarget = unpairedSource.Select(x => x.Key).Distinct(StringComparer.Ordinal)
            .Count(k => !oracleGroups.ContainsKey(k));
        result.OccurrenceShortfallInTarget = unpairedSource.Count(x => oracleGroups.ContainsKey(x.Key));
        result.KeysMissingInSource = unpairedTarget.Select(x => x.Key).Distinct(StringComparer.Ordinal)
            .Count(k => !sqlGroups.ContainsKey(k));
        result.OccurrenceShortfallInSource = unpairedTarget.Count(x => sqlGroups.ContainsKey(x.Key));

        // --- ערכי מפתח שלמים שקיימים בצד אחד בלבד ---
        //
        // כשיום שלם יוצא בקובץ אחד ולא בשני, הממצא מופיע כמאות שורות חסרות -
        // ובצורה הזו הוא נראה כרשימה ארוכה ולא כחור אחד. הסיכום כאן מרים
        // אותו לכותרת: איזה רכיב, אילו ערכים, כמה שורות, ובאיזה צד הם קיימים.
        {
            var gapNotes = new List<KeyValueGapNote>();

            for (int i = 0; i < keys.Count; i++)
            {
                // ספירת השורות לכל ערך של רכיב המפתח, בכל צד
                var sourceValues = new Dictionary<string, int>(StringComparer.Ordinal);
                var targetValues = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var group in sqlGroups.Values)
                {
                    if (i >= group.RawParts.Count) continue;
                    string value = group.RawParts[i];
                    sourceValues[value] = sourceValues.GetValueOrDefault(value, 0) + group.Occurrences.Count;
                }

                foreach (var group in oracleGroups.Values)
                {
                    if (i >= group.RawParts.Count) continue;
                    string value = group.RawParts[i];
                    targetValues[value] = targetValues.GetValueOrDefault(value, 0) + group.Occurrences.Count;
                }

                int distinctInField = sourceValues.Keys.Union(targetValues.Keys, StringComparer.Ordinal).Count();

                void Collect(Dictionary<string, int> own, Dictionary<string, int> other, bool inSource)
                {
                    var missing = own.Where(v => !other.ContainsKey(v.Key))
                                     .OrderByDescending(v => v.Value)
                                     .ToList();
                    if (missing.Count == 0) return;

                    gapNotes.Add(new KeyValueGapNote
                    {
                        FieldName = DisplayFieldName(inSource ? keys[i].SqlField : keys[i].OracleField),
                        PresentIn = inSource ? "Source" : "Target",
                        ValueCount = missing.Count,
                        RowCount = missing.Sum(v => v.Value),
                        Values = missing.Take(MaxValuesPerKeyGap)
                                        .Select(v => string.IsNullOrEmpty(v.Key) ? "(ריק)" : v.Key)
                                        .ToList(),
                        DistinctValuesInField = distinctInField
                    });
                }

                Collect(sourceValues, targetValues, true);
                Collect(targetValues, sourceValues, false);
            }

            // ההערות המשמעותיות קודם: חור שמשפיע על יותר שורות הוא החשוב יותר
            result.KeyValueGaps = gapNotes
                .OrderByDescending(n => n.RowCount)
                .Take(MaxKeyValueGapNotes)
                .ToList();
        }

        // אבחון "כמעט זהה" לשורות חסרות.
        // שורה שנשארה ללא התאמה נובעת לרוב מהפרש ברכיב מפתח בודד
        // (למשל אותו דף ואותו דף נוסף אך תאריך שונה). בלי לציין את הרכיב
        // השונה, ממצא של "דף חסר" מחייב חיפוש ידני בין כל המופעים של אותו דף.
        //
        // האינדקס מכיל רק שורות שגם הן נשארו ללא התאמה. שורה שכן נמצאה בשני
        // הצדדים אינה מועמדת להיות המקבילה החסרה, והצגתה הייתה מפנה את
        // המשתמש לשורה הלא נכונה.
        //
        // האינדקס נבנה רק כשיש יותר מרכיב מפתח אחד: עם רכיב אחד כל השורות
        // היו נחשבות דומות והאבחון היה חסר משמעות.
        bool canDiagnoseKeyDiff = keys.Count >= 2
            && sqlGroups.Count <= MaxKeysForSimilarityIndex
            && oracleGroups.Count <= MaxKeysForSimilarityIndex;

        // האינדקס נבנה מכל מפתחות הצד השני, ולא רק מאלה שגם הם נשארו ללא זוג.
        //
        // קודם הוא נבנה מהמפתחות הלא מזווגים בלבד, ולכן כשצד אחד התכסה במלואו
        // (אפס עודפים בו) האינדקס יצא ריק, וכל שורה חסרה קיבלה "לא נמצאה שורה
        // דומה" - גם כשבצד השני ישבה שורה שנבדלת ברכיב מפתח אחד בלבד. זה בדיוק
        // המצב של 147 שורות מול צד שכל שורותיו זווגו.
        var sqlMaskedIndex = canDiagnoseKeyDiff
            ? BuildMaskedKeyIndex(sqlKeyParts, keys.Count)
            : new List<Dictionary<string, string>>();
        var oracleMaskedIndex = canDiagnoseKeyDiff
            ? BuildMaskedKeyIndex(oracleKeyParts, keys.Count)
            : new List<Dictionary<string, string>>();

        // סריקת הדירוג המלאה מופעלת רק כשהיא זולה. היא זו שנותנת "3 מתוך 5"
        // ולא רק "4 מתוך 5", ולכן היא שווה את המעבר כשהיקף הנתונים מאפשר.
        long scanBudgetTarget = (long)unpairedSource.Count * oracleGroups.Count;
        long scanBudgetSource = (long)unpairedTarget.Count * sqlGroups.Count;
        bool canScanForBestMatch = canDiagnoseKeyDiff
            && scanBudgetTarget + scanBudgetSource <= MaxSimilarityScanOperations;

        // דירוג מלא: המפתח בצד השני שחולק את המספר הגדול ביותר של רכיבים.
        // מוחזר גם מספר הרכיבים שתאמו, כדי שהדוח יאמר "3 מתוך 5" ולא רק
        // "לא נמצאה שורה דומה".
        (string Key, int Matched) BestMatchingKey(
            List<string> ownParts,
            Dictionary<string, List<string>> otherParts)
        {
            string bestKey = string.Empty;
            int bestCount = 0;
            int bestWeight = 0;
            int partCount = ownParts.Count;

            foreach (var candidate in otherParts)
            {
                int count = 0;
                int weight = 0;
                var parts = candidate.Value;
                int limit = Math.Min(partCount, parts.Count);

                for (int i = 0; i < limit; i++)
                {
                    if (!string.Equals(ownParts[i], parts[i], StringComparison.Ordinal)) continue;
                    count++;

                    // רכיב מוקדם יותר במפתח שוקל יותר, מפני שהוא הגס יותר.
                    // בלי המשקל, שני מועמדים עם אותו מספר רכיבים תואמים היו
                    // נבחרים לפי סדר מקרי, והדוח היה מצביע על דף אחר לגמרי
                    // במקום על אותו דף עם דף נוסף שונה.
                    weight += partCount - i;
                }

                if (count > bestCount || (count == bestCount && weight > bestWeight))
                {
                    bestCount = count;
                    bestWeight = weight;
                    bestKey = candidate.Key;
                }
            }

            return (bestKey, bestCount);
        }

        // איתור רכיב המפתח היחיד שנבדל בין שורה חסרה לשורה הדומה לה בצד השני
        MissingRowDetail BuildMissingDetail(string compositeKey, RowOccurrence occurrence, bool missingInTarget)
        {
            bool keyAbsent = missingInTarget
                ? !oracleGroups.ContainsKey(compositeKey)
                : !sqlGroups.ContainsKey(compositeKey);

            sqlGroups.TryGetValue(compositeKey, out var sourceGroup);
            oracleGroups.TryGetValue(compositeKey, out var targetGroup);
            int sourceOccurrences = sourceGroup?.Occurrences.Count ?? 0;
            int targetOccurrences = targetGroup?.Occurrences.Count ?? 0;

            var detail = new MissingRowDetail
            {
                KeyValue = Describe(compositeKey),
                // רכיבי המפתח בנפרד, כדי שהממצא יוצג כשורה בטבלה עם עמודה
                // לכל רכיב ולא כמחרוזת אחת שצריך לפרק בעיניים
                KeyParts = new List<string>(
                    (missingInTarget ? sourceGroup?.RawParts : targetGroup?.RawParts)
                        ?? new List<string>()),
                RowNumber = occurrence.RowNumber,
                SourceOccurrences = sourceOccurrences,
                TargetOccurrences = targetOccurrences,
                MissingKind = keyAbsent ? "KeyAbsent" : "OccurrenceShortfall",
                MissingKindTitle = keyAbsent
                    ? "המפתח אינו קיים כלל בצד השני"
                    : $"המפתח קיים בצד השני אך בפחות מופעים ({sourceOccurrences} מול {targetOccurrences})"
            };

            // כשהמפתח חוזר, המפתח לבדו אינו מזהה שורה - נדרשים גם הערכים המבדילים
            if (sourceOccurrences > 1 || targetOccurrences > 1)
            {
                var parts = new List<string>();
                for (int i = 0; i < compares.Count; i++)
                {
                    var values = new HashSet<string>(StringComparer.Ordinal);
                    if (sourceGroup != null)
                        foreach (var sibling in sourceGroup.Occurrences) values.Add(sibling.CompareValues[i]);
                    if (targetGroup != null)
                        foreach (var sibling in targetGroup.Occurrences) values.Add(sibling.CompareValues[i]);
                    if (values.Count <= 1) continue;

                    string column = missingInTarget ? compares[i].SqlField : compares[i].OracleField;
                    parts.Add($"{DisplayFieldName(column)}={occurrence.CompareValues[i]}");
                    if (parts.Count == 4) break;
                }
                detail.DistinguishingValues = string.Join(" | ", parts);
            }

            detail.KeyPartsTotal = keys.Count;

            // כשהמפתח קיים בצד השני, כל רכיביו תאמו בהגדרה - חסר רק מופע
            if (!keyAbsent)
            {
                detail.KeyPartsMatched = keys.Count;
                return detail;
            }

            if (!canDiagnoseKeyDiff) return detail;

            var ownParts = missingInTarget ? sqlKeyParts[compositeKey] : oracleKeyParts[compositeKey];
            var otherIndex = missingInTarget ? oracleMaskedIndex : sqlMaskedIndex;
            var otherParts = missingInTarget ? oracleKeyParts : sqlKeyParts;

            // המסלול המהיר: שורה שנבדלת ברכיב מפתח אחד בדיוק.
            //
            // הסריקה יורדת מהרכיב האחרון לראשון, כדי להעדיף מועמד שנבדל ברכיב
            // המשני ביותר. אותו דף עם דף נוסף שונה הוא ממצא קרוב בהרבה מדף
            // אחר לגמרי שבמקרה חולק את שאר רכיבי המפתח, ושניהם "ארבעה מתוך חמישה".
            string bestKey = string.Empty;
            int bestMatched = 0;

            for (int i = keys.Count - 1; i >= 0; i--)
            {
                string masked = MaskedKey(ownParts, i);
                if (!otherIndex[i].TryGetValue(masked, out var counterpartKey)) continue;
                bestKey = counterpartKey;
                bestMatched = keys.Count - 1;
                break;
            }

            // לא נמצאה שורה שנבדלת ברכיב אחד - מדרגים מול כל הצד השני,
            // כדי לומר כמה רכיבים כן תאמו ולא להסתפק ב"אין שורה דומה"
            if (bestMatched == 0 && canScanForBestMatch)
            {
                (bestKey, bestMatched) = BestMatchingKey(ownParts, otherParts);
            }

            if (bestMatched == 0 || string.IsNullOrEmpty(bestKey)) return detail;

            detail.KeyPartsMatched = bestMatched;
            detail.CounterpartKeyValue = Describe(bestKey);

            // שמות הרכיבים השונים והערכים שלהם בשני הצדדים, עד שלושה
            var counterpartParts = otherParts[bestKey];
            var diffNames = new List<string>();
            var ownValues = new List<string>();
            var otherValues = new List<string>();

            for (int i = 0; i < keys.Count && i < ownParts.Count && i < counterpartParts.Count; i++)
            {
                if (string.Equals(ownParts[i], counterpartParts[i], StringComparison.Ordinal)) continue;

                diffNames.Add(missingInTarget
                    ? DisplayFieldName(keys[i].SqlField)
                    : DisplayFieldName(keys[i].OracleField));
                ownValues.Add(UnescapeKeyPart(ownParts[i]));
                otherValues.Add(UnescapeKeyPart(counterpartParts[i]));

                if (diffNames.Count == 3) break;
            }

            detail.DiffFieldName = string.Join(", ", diffNames);
            string ownJoined = string.Join(", ", ownValues);
            string otherJoined = string.Join(", ", otherValues);
            detail.SourceValue = missingInTarget ? ownJoined : otherJoined;
            detail.TargetValue = missingInTarget ? otherJoined : ownJoined;

            return detail;
        }

        // הפירוט הוא לכל מופע ללא זוג, ולא לכל מפתח - כך שמפתח שחסר בו מופע
        // אחד מתוך ארבעה מדווח פעם אחת ולא נעלם מהדוח.
        foreach (var (key, occurrence) in unpairedSource.Take(MaxDetailRows))
        {
            result.MissingInTarget.Add(Describe(key));
            result.MissingInTargetDetails.Add(BuildMissingDetail(key, occurrence, missingInTarget: true));
        }

        foreach (var (key, occurrence) in unpairedTarget.Take(MaxDetailRows))
        {
            result.MissingInSource.Add(Describe(key));
            result.MissingInSourceDetails.Add(BuildMissingDetail(key, occurrence, missingInTarget: false));
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
                // המספרים נלקחים מהמונים ולא מאורך הרשימה המוצגת.
                //
                // Duplicates חסום ב-MaxReportedDuplicates, ולכן בדוח עם
                // 500 מפתחות חוזרים השורה אמרה "200 מפתחות כפולים" -
                // מספר שסתר את הכרטיס שמעליו. וגם TotalDuplicates הוא
                // מספר הממצאים ולא מספר השורות המושפעות, שהוא
                // TotalDuplicateRows.
                parts.Add($"{result.TotalDuplicateFindings} מפתחות כפולים "
                    + $"({result.TotalDuplicateRows} שורות מושפעות)");
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

        // צבירת מספר השורות שבהן כל שדה נמצא שונה, עם דוגמה ראשונה.
        // זה מה שהופך "2428 שורות שונות" לתשובה קצרה: אילו שדות אשמים,
        // ומי מהם נמצא שונה בכל השורות ולכן הוא הבדל שיטתי ולא פער נתונים.
        var fieldDiffCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var fieldDiffExamples = new Dictionary<string, (string Source, string Target)>(StringComparer.Ordinal);

        // ההשוואה עוברת על זוגות מופעים שהותאמו, ולא על שורה אחת לכל מפתח.
        // כך שורה שנייה ושלישית תחת אותו מפתח נבדקות גם הן, במקום להיעלם.
        foreach (var group in sqlGroups.Values)
        {
            string compositeKey = group.CompositeKey;

            foreach (var sourceOccurrence in group.Occurrences)
            {
                var pairedTarget = sourceOccurrence.PairedWith;
                if (pairedTarget == null) continue;   // חוסר - טופל בשלב ב

                var sqlRow = sourceOccurrence.Row;
                var oracleRow = pairedTarget.Row;

                result.TotalPairedOccurrences++;

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
                            FieldName = $"{DisplayFieldName(c.SqlField)} / {DisplayFieldName(c.OracleField)}",
                            SourceValue = sqlVal?.ToString()?.Trim() ?? "NULL",
                            TargetValue = oracleVal?.ToString()?.Trim() ?? "NULL",
                            IsMatch = false
                        });
                        diffFieldNames.Add(c.SqlField);

                        // צבירה לפרופיל השדות של השורה התחתונה
                        string profileName = DisplayFieldName(c.SqlField);
                        fieldDiffCounts[profileName] = fieldDiffCounts.GetValueOrDefault(profileName, 0) + 1;

                        // הממצא השטוח: אילו רכיבי מפתח, איזה שדה, ומה הערך בכל צד.
                        // התבניות אומרות כמה ואילו שדות; רק הרשימה הזו אומרת
                        // באיזו שורה בדיוק, ובלעדיה נדרש חיפוש ידני בקובץ.
                        result.TotalDiscrepancyFindings++;
                        if (result.DiscrepancyRows.Count < MaxDetailRows)
                        {
                            result.DiscrepancyRows.Add(new RowDiffDetail
                            {
                                KeyParts = new List<string>(group.RawParts),
                                KeyValue = Describe(compositeKey),
                                FieldName = profileName,
                                SourceValue = sqlVal?.ToString()?.Trim() ?? "NULL",
                                TargetValue = oracleVal?.ToString()?.Trim() ?? "NULL",
                                SourceRowNumber = sourceOccurrence.RowNumber,
                                TargetRowNumber = pairedTarget.RowNumber
                            });
                        }
                        if (!fieldDiffExamples.ContainsKey(profileName))
                        {
                            fieldDiffExamples[profileName] = (
                                sqlVal?.ToString()?.Trim() ?? "NULL",
                                oracleVal?.ToString()?.Trim() ?? "NULL");
                        }

                        // Data Integrity Gap detection:
                        // One side has a real value, the other is empty/null/zero
                        bool sqlIsEmpty = IsEmptyOrZero(sqlStr);
                        bool oracleIsEmpty = IsEmptyOrZero(oracleStr);

                        if (sqlIsEmpty != oracleIsEmpty)
                        {
                            // One side has data, the other doesn't
                            if (result.DataIntegrityGaps.Count < MaxReportedGaps)
                            {
                                result.DataIntegrityGaps.Add(new DataIntegrityGap
                                {
                                    KeyValue = Describe(compositeKey),
                                    KeyParts = new List<string>(group.RawParts),
                                    FieldName = $"{DisplayFieldName(c.SqlField)} / {DisplayFieldName(c.OracleField)}",
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
                    string patternKey = string.Join(", ", diffFieldNames.Select(DisplayFieldName));

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
                    // כל דוגמה נושאת את המפתח המלא שלה יחד עם הערכים שנמצאו
                    // שונים באותה שורה בפועל, ולא ערכים מהשורה הראשונה בתבנית.
                    if (pattern.ExampleKeys.Count < 4)
                    {
                        pattern.ExampleKeys.Add(Describe(compositeKey));
                        pattern.Examples.Add(new DiscrepancyExample
                        {
                            KeyValue = Describe(compositeKey),
                            Fields = differentFields
                        });
                    }
                }
            }
            // Note: missing keys were already handled in Step B
        }

        // Sort discrepancy patterns by count descending
        result.DiscrepancyPatterns = discrepancyPatternsMap.Values.OrderByDescending(p => p.Count).ToList();

        // --- פרופיל ההבדלים לפי שדה ---
        // שדה שנמצא שונה ביותר ממחצית השורות הוא כמעט תמיד הבדל שיטתי בין
        // המסדים (עמודת שירות שנשמרת אחרת, פורמט תאריך) ולא פער נתונים אמיתי.
        // הפרדתו מהשאר היא מה שמונע ממנו להטביע את הממצאים שכן דורשים בדיקה.
        int systematicThreshold = result.TotalDiscrepancyRows / 2;
        foreach (var entry in fieldDiffCounts.OrderByDescending(e => e.Value))
        {
            var example = fieldDiffExamples.GetValueOrDefault(entry.Key, (string.Empty, string.Empty));
            bool isSystematic = result.TotalDiscrepancyRows >= MinRowsForSystematicField
                && entry.Value > systematicThreshold;

            result.FieldGapProfiles.Add(new FieldGapProfile
            {
                FieldName = entry.Key,
                DiffCount = entry.Value,
                DiffPercentage = result.TotalDiscrepancyRows > 0
                    ? Math.Round((double)entry.Value / result.TotalDiscrepancyRows * 100, 1)
                    : 0,
                ExampleSourceValue = string.IsNullOrEmpty(example.Item1) ? "(ריק)" : example.Item1,
                ExampleTargetValue = string.IsNullOrEmpty(example.Item2) ? "(ריק)" : example.Item2,
                IsSystematic = isSystematic
            });

            if (isSystematic) result.SystematicFieldCount++;
        }

        // סימון הממצאים השטוחים שנובעים משדה שיטתי. בלי הסימון, שדה אחד
        // שנשמר אחרת בין המסדים מציף את הרשימה ומסתיר את הממצאים האמיתיים.
        if (result.SystematicFieldCount > 0)
        {
            var systematicNames = new HashSet<string>(
                result.FieldGapProfiles.Where(f => f.IsSystematic).Select(f => f.FieldName),
                StringComparer.Ordinal);

            foreach (var finding in result.DiscrepancyRows)
                finding.IsSystematicField = systematicNames.Contains(finding.FieldName);
        }

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

        // --- שורה תחתונה ---
        // התשובה בשורה אחת, לפני 2000 שורות הפירוט. בלעדיה נדרש לקרוא את כל
        // הדוח כדי להבין אם המיגרציה תקינה, ומה בדיוק דורש בדיקה.
        {
            var summary = new List<string>();

            if (result.TotalMissingInTarget == 0 && result.TotalMissingInSource == 0)
                summary.Add($"כל {result.TotalPairedOccurrences} השורות נמצאו בשני הצדדים, בלי חוסרים ובלי עודפים");
            else
                // שמות הקבצים ולא "מקור"/"יעד": קורא הדוח אינו אמור לזכור מי מהם מי
                summary.Add($"{result.TotalPairedOccurrences} שורות נמצאו בשני הצדדים, " +
                            $"{result.TotalMissingInTarget} נמצאות רק ב-{SourceTable}, " +
                            $"{result.TotalMissingInSource} נמצאות רק ב-{TargetTable}");

            if (result.TotalDuplicateFindings > 0)
                summary.Add($"{result.TotalDuplicateFindings} מפתחות חוזרים");

            if (result.TotalDiscrepancyRows == 0)
            {
                summary.Add("אין הבדלי ערכים");
            }
            else
            {
                var systematic = result.FieldGapProfiles.Where(f => f.IsSystematic).ToList();
                if (systematic.Count > 0)
                {
                    summary.Add($"{result.TotalDiscrepancyRows} שורות עם הבדלי ערכים, ומהן " +
                                string.Join(" ו-", systematic.Select(f => $"{f.FieldName} ב-{f.DiffCount} שורות")) +
                                " - הבדל שיטתי שכדאי להוציא מההשוואה");

                    var rest = result.FieldGapProfiles.Where(f => !f.IsSystematic).Take(3).ToList();
                    if (rest.Count > 0)
                        summary.Add("השדות שדורשים בדיקה בפועל: " +
                                    string.Join(", ", rest.Select(f => $"{f.FieldName} ({f.DiffCount})")));
                }
                else
                {
                    summary.Add($"{result.TotalDiscrepancyRows} שורות עם הבדלי ערכים ב-{result.FieldGapProfiles.Count} שדות");
                }
            }

            result.BottomLine = string.Join(". ", summary) + ".";
        }

        // --- דיווח חיתוך מספר שורות ---
        // כשצד נחתך, ממצאי החוסר והעודף אינם חד-משמעיים: שורה יכולה להיראות
        // חסרה רק מפני שנפלה מחוץ לחלון. זה נאמר במפורש ולא נשאר מוסתר.
        result.MaxRowsRequested = maxRowsRequested;
        result.SourceTotalRows = sourceTotalRows;
        result.TargetTotalRows = targetTotalRows;
        // שני מקורות ידע על חיתוך, לפי מה שהקורא באמת יודע:
        // מסלול הקבצים סופר את השורות בעצמו ולכן מוסר את הסך המדויק,
        // ומסלול המסד שולף שורת גישוש אחת מעבר לתקרה ולכן יודע בוודאות
        // אם נחתך - אך לא כמה שורות יש בסך הכול.
        //
        // הניחוש "הוחזרו בדיוק N שורות" הוסר: הוא הפיק אזהרת חיתוך שקרית
        // על כל טבלה שמכילה בדיוק N שורות, ולכן הפך את האזהרה לרעש.
        bool sourceTotalKnown = sourceTotalRows > 0;
        bool targetTotalKnown = targetTotalRows > 0;

        result.SourceTruncated = sourceTruncatedKnown
            ?? (maxRowsRequested > 0 && sourceTotalKnown && sourceTotalRows > maxRowsRequested);
        result.TargetTruncated = targetTruncatedKnown
            ?? (maxRowsRequested > 0 && targetTotalKnown && targetTotalRows > maxRowsRequested);

        if (result.SourceTruncated || result.TargetTruncated)
        {
            var parts = new List<string>();
            if (result.SourceTruncated)
                parts.Add(sourceTotalKnown
                    ? $"מקור: הושוו {maxRowsRequested} מתוך {sourceTotalRows} שורות"
                    : $"מקור: הושוו {maxRowsRequested} השורות הראשונות לפי סדר המפתח, ויש בטבלה שורות נוספות");
            if (result.TargetTruncated)
                parts.Add(targetTotalKnown
                    ? $"יעד: הושוו {maxRowsRequested} מתוך {targetTotalRows} שורות"
                    : $"יעד: הושוו {maxRowsRequested} השורות הראשונות לפי סדר המפתח, ויש בטבלה שורות נוספות");

            result.TruncationWarning = string.Join(" | ", parts)
                + ". ממצאי החוסר והעודף אינם חד-משמעיים במצב הזה - שורה יכולה להיראות חסרה"
                + " רק מפני שנפלה מחוץ למספר השורות שנטענו. להסקת מסקנות יש להעלות את המגבלה"
                + " או לצמצם את טווח הבדיקה בסינון.";
        }

        // --- אחוזים ושורת מאזן ---
        // האחוזים מחושבים כאן פעם אחת בלבד. בעבר המסך והייצוא לוורד חישבו כל אחד
        // לחוד ולכן היו יכולים להציג מספרים שונים על אותו דוח.
        //
        // הפרדה לשני אחוזים במקום אחד: "תאימות" חושב קודם מול סך שורות המקור,
        // ולכן כל שורה שלא הותאמה דיללה מספר שנקרא כאילו הוא מודד נכונות.
        result.MatchPercentage = result.TotalPairedOccurrences > 0
            ? Math.Round((double)result.TotalMatched / result.TotalPairedOccurrences * 100, 2)
            : 0;

        int widerSide = Math.Max(result.TotalRowsInSource, result.TotalRowsInTarget);
        result.CoveragePercentage = widerSide > 0
            ? Math.Round((double)result.TotalPairedOccurrences / widerSide * 100, 2)
            : 0;

        // כל שורה חייבת להיות באחד מארבעה מצבים: מפתח ריק, זוג זהה, זוג שונה, או עודף.
        // אם השוויון הזה נשבר - הדוח סותר את עצמו, וזה נאמר במפורש במקום להיקרא כתקין.
        int sourceAccounted = result.SourceEmptyKeyRows + result.TotalMatched
            + result.TotalDiscrepancyRows + result.TotalMissingInTarget;
        int targetAccounted = result.TargetEmptyKeyRows + result.TotalMatched
            + result.TotalDiscrepancyRows + result.TotalMissingInSource;

        result.IsInternallyConsistent = sourceAccounted == result.TotalRowsInSource
            && targetAccounted == result.TotalRowsInTarget;

        result.ReconciliationLine =
            $"מקור: {result.TotalRowsInSource} = {result.TotalMatched} זהות + {result.TotalDiscrepancyRows} שונות + " +
            $"{result.TotalMissingInTarget} עודפות" +
            (result.SourceEmptyKeyRows > 0 ? $" + {result.SourceEmptyKeyRows} מפתח ריק" : "") +
            $" | יעד: {result.TotalRowsInTarget} = {result.TotalMatched} זהות + {result.TotalDiscrepancyRows} שונות + " +
            $"{result.TotalMissingInSource} עודפות" +
            (result.TargetEmptyKeyRows > 0 ? $" + {result.TargetEmptyKeyRows} מפתח ריק" : "") +
            (result.IsInternallyConsistent ? " ✓" : " ✕ המאזן אינו מתאזן");

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


