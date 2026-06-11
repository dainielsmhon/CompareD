using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CompareD.Controllers;

// הגדרת בקר ההשוואה הראשי CompareController לטיפול בתהליכי החיבור והשוואת הנתונים
public class CompareController : Controller
{
    // פעולה (Action) המטפלת בקבלת מחרוזות החיבור ובדיקתן במקביל
    [HttpPost]
    public async Task<IActionResult> Connect(string sqlConnectionString, string oracleConnectionString)
    {
        // בדיקה שמחרוזות החיבור אינן ריקות
        if (string.IsNullOrWhiteSpace(sqlConnectionString) || string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            // הגדרת שגיאה מתאימה להצגה
            TempData["ErrorMessage"] = "יש להזין את שתי מחרוזות החיבור כדי להמשיך.";
            // חזרה לדף הבית
            return RedirectToAction("Index", "Home");
        }

        // רשימה לאחסון אובייקטים (טבלאות ותצוגות) מ-SQL Server
        var sqlObjects = new List<DatabaseObject>();
        // רשימה לאחסון אובייקטים (טבלאות ותצוגות) מ-Oracle
        var oracleObjects = new List<DatabaseObject>();
        // משתנה לאחסון הודעת שגיאה במידה ותתרחש
        string? errorMessage = null;

        try
        {
            // הרצת שתי משימות החיבור והבאת האובייקטים במקביל לחסכון בזמן
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        // הפעלת פונקציית העזר לקבלת טבלאות ותצוגות מ-SQL Server
                        sqlObjects = await GetSqlObjectsAsync(sqlConnectionString);
                    }
                    catch (Exception ex)
                    {
                        // זריקת שגיאה ממוקדת עבור SQL Server
                        throw new Exception($"שגיאה בחיבור ל-SQL Server: {ex.Message}");
                    }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        // הפעלת פונקציית העזר לקבלת טבלאות ותצוגות מ-Oracle
                        oracleObjects = await GetOracleObjectsAsync(oracleConnectionString);
                    }
                    catch (Exception ex)
                    {
                        // זריקת שגיאה ממוקדת עבור Oracle
                        throw new Exception($"שגיאה בחיבור ל-Oracle: {ex.Message}");
                    }
                })
            );
        }
        catch (Exception ex)
        {
            // קליטת הודעת השגיאה שתחזור למשתמש מהמשימה שנכשלה
            errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }

        // אם התרחשה שגיאה במהלך ההתחברות לאחד ממסדי הנתונים
        if (errorMessage != null)
        {
            // שמירת הודעת השגיאה ב-TempData להצגה בדף הבית
            TempData["ErrorMessage"] = errorMessage;
            // שמירת הערכים שכבר הוזנו כדי שהמשתמש לא יצטרך להקליד שוב
            TempData["SqlConnString"] = sqlConnectionString;
            TempData["OracleConnString"] = oracleConnectionString;
            // חזרה לדף הבית
            return RedirectToAction("Index", "Home");
        }

        // שמירת מחרוזות החיבור המוצלחות בסשן של המשתמש לצורך שימוש בשלבים הבאים
        HttpContext.Session.SetString("SqlConnectionString", sqlConnectionString);
        HttpContext.Session.SetString("OracleConnectionString", oracleConnectionString);

        // יצירת מודל נתונים להעברה לתצוגת בחירת האובייקטים
        var viewModel = new TableSelectionViewModel
        {
            SqlTables = sqlObjects,
            OracleTables = oracleObjects
        };

        // מעבר לתצוגת בחירת האובייקטים והעברת המודל המכיל את רשימות האובייקטים
        return View("SelectTables", viewModel);
    }

    // פעולה (Action) המטפלת בקבלת הטבלאות/תצוגות שנבחרו ושליפת העמודות שלהן במקביל
    [HttpPost]
    public async Task<IActionResult> SelectFields(string sqlTable, string oracleTable)
    {
        // שליפת מחרוזות החיבור מהסשן
        var sqlConnectionString = HttpContext.Session.GetString("SqlConnectionString");
        var oracleConnectionString = HttpContext.Session.GetString("OracleConnectionString");

        // אם מחרוזות החיבור או בחירת הטבלאות אינן קיימות בסשן או בבקשה
        if (string.IsNullOrEmpty(sqlConnectionString) || string.IsNullOrEmpty(oracleConnectionString) ||
            string.IsNullOrEmpty(sqlTable) || string.IsNullOrEmpty(oracleTable))
        {
            // הגדרת שגיאה להצגה
            TempData["ErrorMessage"] = "פג תוקף החיבור או שלא נבחרו טבלאות. נא להתחבר מחדש.";
            // הפניה לדף הבית
            return RedirectToAction("Index", "Home");
        }

        // רשימות לאחסון שמות העמודות
        var sqlColumns = new List<string>();
        var oracleColumns = new List<string>();
        // משתנה לאחסון הודעת שגיאה
        string? errorMessage = null;

        try
        {
            // שליפת העמודות משני מסדי הנתונים במקביל לחסכון בזמן
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        // שליפת עמודות עבור SQL Server
                        sqlColumns = await GetSqlColumnsAsync(sqlConnectionString, sqlTable);
                    }
                    catch (Exception ex)
                    {
                        // שגיאה ספציפית ל-SQL Server
                        throw new Exception($"שגיאה בשליפת עמודות מ-SQL Server: {ex.Message}");
                    }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        // שליפת עמודות עבור Oracle
                        oracleColumns = await GetOracleColumnsAsync(oracleConnectionString, oracleTable);
                    }
                    catch (Exception ex)
                    {
                        // שגיאה ספציפית ל-Oracle
                        throw new Exception($"שגיאה בשליפת עמודות מ-Oracle: {ex.Message}");
                    }
                })
            );
        }
        catch (Exception ex)
        {
            // שמירת הודעת השגיאה
            errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }

        // אם התרחשה שגיאה במהלך שליפת העמודות
        if (errorMessage != null)
        {
            // הגדרת שגיאה
            TempData["ErrorMessage"] = errorMessage;
            // הפניה מחדש לדף הבית לביצוע התחברות חדשה ותקינה
            return RedirectToAction("Index", "Home");
        }

        // שמירת שמות הטבלאות הנבחרות בסשן להמשך הדרך
        HttpContext.Session.SetString("SelectedSqlTable", sqlTable);
        HttpContext.Session.SetString("SelectedOracleTable", oracleTable);

        // יצירת ה-ViewModel להעברת הנתונים לממשק המיפוי
        var viewModel = new FieldMappingViewModel
        {
            SqlTable = sqlTable,
            OracleTable = oracleTable,
            SqlColumns = sqlColumns,
            OracleColumns = oracleColumns
        };

        // החזרת תצוגת מיפוי השדות MapFields יחד עם המודל שנוצר
        return View("MapFields", viewModel);
    }

    // פעולה (Action) המבצעת את אלגוריתם ההשוואה בפועל
    [HttpPost]
    public async Task<IActionResult> CompareData(
        string sqlTable, 
        string oracleTable, 
        string mappingMode, 
        List<string> sourceFields, 
        List<string> targetFields, 
        List<string> fieldRoles,
        int maxRows)
    {
        // שליפת מחרוזות החיבור מהסשן
        var sqlConnectionString = HttpContext.Session.GetString("SqlConnectionString");
        var oracleConnectionString = HttpContext.Session.GetString("OracleConnectionString");

        // אם אין חיבורים פעילים
        if (string.IsNullOrEmpty(sqlConnectionString) || string.IsNullOrEmpty(oracleConnectionString))
        {
            TempData["ErrorMessage"] = "פג תוקף החיבור למסדי הנתונים. נא להתחבר מחדש.";
            return RedirectToAction("Index", "Home");
        }

        // רשימות להגדרת מפתחות ועמודות ערך להשוואה
        var keys = new List<(string SqlField, string OracleField)>();
        var compares = new List<(string SqlField, string OracleField)>();

        try
        {
            // טיפול במקרה של השוואה אוטומטית מלאה
            if (mappingMode == "Auto")
            {
                // שליפת העמודות של שתי הטבלאות
                var sqlCols = await GetSqlColumnsAsync(sqlConnectionString, sqlTable);
                var oracleCols = await GetOracleColumnsAsync(oracleConnectionString, oracleTable);

                // מציאת עמודות משותפות בעלות שם זהה (ללא הבדל רישיות)
                var matchedCols = new List<string>();
                foreach (var sc in sqlCols)
                {
                    var oc = oracleCols.FirstOrDefault(c => string.Equals(c, sc, StringComparison.OrdinalIgnoreCase));
                    if (oc != null)
                    {
                        matchedCols.Add(sc);
                    }
                }

                // אם לא נמצאו עמודות תואמות כלל
                if (matchedCols.Count == 0)
                {
                    throw new Exception("לא נמצאו עמודות בעלות שם זהה להשוואה אוטומטית.");
                }

                // ברירת המחדל למפתח תהיה העמודה הראשונה, והשאר שדות להשוואה
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

                // חלוקת השדות לפי התפקיד שנבחר בממשק
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

                // וידוא שהוגדר לפחות שדה מפתח אחד
                if (keys.Count == 0)
                {
                    throw new Exception("חובה להגדיר לפחות שדה מפתח אחד לביצוע השוואה ידנית.");
                }
            }

            // בניית שאילתת SQL Server מאובטחת המבוססת על העמודות המבוקשות והגבלת רשומות
            var sqlColsToSelect = keys.Select(k => k.SqlField).Union(compares.Select(c => c.SqlField)).Distinct().ToList();
            string sqlSelectString = string.Join(", ", sqlColsToSelect.Select(c => $"[{c}]"));
            string sqlQuery = $"SELECT TOP ({maxRows}) {sqlSelectString} FROM [{sqlTable}]";

            // בניית שאילתת Oracle מאובטחת המבוססת על העמודות המבוקשות והגבלת רשומות
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

                        // יצירת מפתח מורכב מכל עמודות המפתח
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

                        // יצירת מפתח מורכב מכל עמודות המפתח
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

            // מעבר על רשומות המקור (SQL Server) להשוואה מול היעד
            foreach (var sqlKvp in sqlData)
            {
                string compositeKey = sqlKvp.Key;
                var sqlRow = sqlKvp.Value;

                // אם הרשומה קיימת גם ב-Oracle
                if (oracleData.TryGetValue(compositeKey, out var oracleRow))
                {
                    bool isRowMatch = true;
                    var fieldsList = new List<FieldComparisonDetail>();

                    // השוואת כל שדה שנבחר להשוואה
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
                    // רשומה קיימת ב-SQL Server אך חסרה ב-Oracle
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

            // מעבר על רשומות היעד (Oracle) למציאת רשומות שאינן קיימות במקור
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

            // יצירת מודל תוצאות להעברה לתצוגה
            var resultsViewModel = new ComparisonResultViewModel
            {
                SqlTable = sqlTable,
                OracleTable = oracleTable,
                TotalMatched = matchedCount,
                TotalDifferences = diffCount,
                TotalMissingInOracle = missingInOracleCount,
                TotalMissingInSql = missingInSqlCount,
                Details = details
            };

            // החזרת תצוגת התוצאות Results יחד עם הנתונים שחושבו
            return View("Results", resultsViewModel);
        }
        catch (Exception ex)
        {
            // במקרה של שגיאה, החזרת הודעה והפניה לדף הבית
            TempData["ErrorMessage"] = $"נכשלה הרצת השוואת הנתונים: {ex.Message}";
            return RedirectToAction("Index", "Home");
        }
    }

    // פונקציית עזר להבאת טבלאות ותצוגות מ-SQL Server באמצעות ADO.NET נקי ובשאילתה מאובטחת
    private async Task<List<DatabaseObject>> GetSqlObjectsAsync(string connectionString)
    {
        // יצירת רשימה ריקה לאובייקטי מסד הנתונים
        var objects = new List<DatabaseObject>();
        // יצירת חיבור SQL Server
        using (var connection = new SqlConnection(connectionString))
        {
            // פתיחת החיבור בצורה אסינכרונית
            await connection.OpenAsync();
            // הגדרת שאילתת SQL סטטית ומאובטחת לחלוטין (חסינה להזרקות קוד) לשליפת טבלאות ותצוגות
            string query = "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW') ORDER BY TABLE_NAME";
            // יצירת פקודה להרצה
            using (var command = new SqlCommand(query, connection))
            // הרצת השאילתה וקבלת קורא נתונים אסינכרוני
            using (var reader = await command.ExecuteReaderAsync())
            {
                // קריאת כל הרשומות שחזרו מהשאילתה
                while (await reader.ReadAsync())
                {
                    // קבלת סוג האובייקט ונירמול שלו ל-TABLE או VIEW
                    string rawType = reader.GetString(1);
                    string normalizedType = rawType == "VIEW" ? "VIEW" : "TABLE";
                    
                    // הוספת האובייקט לרשימה עם שמו וסוגו
                    objects.Add(new DatabaseObject
                    {
                        Name = reader.GetString(0),
                        Type = normalizedType
                    });
                }
            }
        }
        // החזרת רשימת האובייקטים
        return objects;
    }

    // פונקציית עזר להבאת טבלאות ותצוגות מ-Oracle באמצעות ADO.NET נקי ובשאילתה מאובטחת
    private async Task<List<DatabaseObject>> GetOracleObjectsAsync(string connectionString)
    {
        // יצירת רשימה ריקה לאובייקטי מסד הנתונים
        var objects = new List<DatabaseObject>();
        // יצירת חיבור Oracle
        using (var connection = new OracleConnection(connectionString))
        {
            // פתיחת החיבור בצורה אסינכרונית
            await connection.OpenAsync();
            // הגדרת שאילתת Oracle סטטית ומאובטחת לחלוטין המאחדת טבלאות ותצוגות של המשתמש המחובר
            string query = "SELECT TABLE_NAME, 'TABLE' AS TABLE_TYPE FROM USER_TABLES UNION ALL SELECT VIEW_NAME AS TABLE_NAME, 'VIEW' AS TABLE_TYPE FROM USER_VIEWS ORDER BY TABLE_NAME";
            // יצירת פקודה להרצה
            using (var command = new OracleCommand(query, connection))
            // הרצת השאילתה וקבלת קורא נתונים אסינכרוני
            using (var reader = await command.ExecuteReaderAsync())
            {
                // קריאת כל הרשומות שחזרו מהשאילתה
                while (await reader.ReadAsync())
                {
                    // הוספת האובייקט לרשימה עם שמו וסוגו
                    objects.Add(new DatabaseObject
                    {
                        Name = reader.GetString(0),
                        Type = reader.GetString(1)
                    });
                }
            }
        }
        // החזרת רשימת האובייקטים
        return objects;
    }

    // פונקציית עזר לשליפת עמודות מ-SQL Server באמצעות שאילתהParameterized ומאובטחת לחלוטין
    private async Task<List<string>> GetSqlColumnsAsync(string connectionString, string tableName)
    {
        // רשימה לאחסון שמות העמודות
        var columns = new List<string>();
        // יצירת החיבור
        using (var connection = new SqlConnection(connectionString))
        {
            // פתיחה אסינכרונית
            await connection.OpenAsync();
            // שאילתת SQL עם פרמטר למניעת הזרקת קוד
            string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName ORDER BY COLUMN_NAME";
            // הגדרת פקודה
            using (var command = new SqlCommand(query, connection))
            {
                // הוספת הפרמטר בצורה בטוחה
                command.Parameters.AddWithValue("@tableName", tableName);
                // ביצוע השאילתה
                using (var reader = await command.ExecuteReaderAsync())
                {
                    // קריאת העמודות
                    while (await reader.ReadAsync())
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        // החזרת רשימת העמודות
        return columns;
    }

    // פונקציית עזר לשליפת עמודות מ-Oracle באמצעות שאילתהParameterized ומאובטחת לחלוטין
    private async Task<List<string>> GetOracleColumnsAsync(string connectionString, string tableName)
    {
        // רשימה לאחסון שמות העמודות
        var columns = new List<string>();
        // יצירת החיבור
        using (var connection = new OracleConnection(connectionString))
        {
            // פתיחה אסינכרונית
            await connection.OpenAsync();
            // שאילתת Oracle עם פרמטר למניעת הזרקת קוד. אנו פונים ל-USER_TAB_COLUMNS
            string query = "SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :tableName ORDER BY COLUMN_NAME";
            // הגדרת פקודה
            using (var command = new OracleCommand(query, connection))
            {
                // הוספת הפרמטר בצורה בטוחה. שים לב שבמטא-דאטה של אורקל שמות הטבלאות נשמרים באותיות גדולות (Uppercase)
                command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                // ביצוע השאילתה
                using (var reader = await command.ExecuteReaderAsync())
                {
                    // קריאת העמודות
                    while (await reader.ReadAsync())
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }
        }
        // החזרת רשימת העמודות
        return columns;
    }
}

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
