using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CompareD.Services;

namespace CompareD.Controllers;

// הגדרת בקר ההשוואה הראשי CompareController לטיפול בתהליכי החיבור והשוואת הנתונים
public class CompareController : Controller
{
    private readonly ICompareService _compareService;

    // בנאי הבקר המקבל את שירות ההשוואה דרך Dependency Injection
    public CompareController(ICompareService compareService)
    {
        _compareService = compareService;
    }

    // פעולה (Action) המטפלת בקבלת מחרוזות החיבור ובדיקתן במקביל
    [HttpPost]
    [ValidateAntiForgeryToken]
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
                        // הפעלת פונקציית השירות לקבלת טבלאות ותצוגות מ-SQL Server
                        sqlObjects = await _compareService.GetSqlObjectsAsync(sqlConnectionString);
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
                        // הפעלת פונקציית השירות לקבלת טבלאות ותצוגות מ-Oracle
                        oracleObjects = await _compareService.GetOracleObjectsAsync(oracleConnectionString);
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
    [ValidateAntiForgeryToken]
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
                        // שליפת עמודות עבור SQL Server באמצעות השירות
                        sqlColumns = await _compareService.GetSqlColumnsAsync(sqlConnectionString, sqlTable);
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
                        // שליפת עמודות עבור Oracle באמצעות השירות
                        oracleColumns = await _compareService.GetOracleColumnsAsync(oracleConnectionString, oracleTable);
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
    [ValidateAntiForgeryToken]
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

        try
        {
            // קריאה לשירות ההשוואה לביצוע השוואה בפועל
            var resultsViewModel = await _compareService.CompareDataAsync(
                sqlConnectionString,
                oracleConnectionString,
                sqlTable,
                oracleTable,
                mappingMode,
                sourceFields,
                targetFields,
                fieldRoles,
                maxRows);

            // החזרת תצוגת התוצאות Results יחד עם הנתונים שחושבו בשירות
            return View("Results", resultsViewModel);
        }
        catch (Exception ex)
        {
            // במקרה של שגיאה, החזרת הודעה והפניה לדף הבית
            TempData["ErrorMessage"] = $"נכשלה הרצת השוואת הנתונים: {ex.Message}";
            return RedirectToAction("Index", "Home");
        }
    }
}
