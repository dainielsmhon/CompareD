using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompareD.Services;

namespace CompareD.Controllers;

// הגדרת בקר ההשוואה הראשי CompareController לטיפול בתהליכי החיבור והשוואת הנתונים
public class CompareController : Controller
{
    private readonly ICompareService _compareService;
    private readonly DatabaseProfilesOptions _profiles;

    // בנאי הבקר המקבל את שירות ההשוואה ופרופילי החיבור באמצעות Dependency Injection
    public CompareController(ICompareService compareService, IOptions<DatabaseProfilesOptions> profiles)
    {
        _compareService = compareService;
        _profiles = profiles.Value;
    }

    // פעולה (Action) המטפלת בקבלת פרופילי החיבור ובדיקתם במקביל
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(string sqlProfileName, string oracleProfileName)
    {
        // בדיקה ששמות הפרופילים אינם ריקים
        if (string.IsNullOrWhiteSpace(sqlProfileName) || string.IsNullOrWhiteSpace(oracleProfileName))
        {
            // הגדרת שגיאה מתאימה להצגה
            TempData["ErrorMessage"] = "יש לבחור את שני פרופילי החיבור כדי להמשיך.";
            // חזרה לדף הבית
            return RedirectToAction("Index", "Home");
        }

        // שליפת מחרוזות החיבור המתאימות מתוך הגדרות השרת מאחורי הקלעים
        var sqlProfile = _profiles.SqlProfiles.FirstOrDefault(p => p.Name == sqlProfileName);
        var oracleProfile = _profiles.OracleProfiles.FirstOrDefault(p => p.Name == oracleProfileName);

        if (sqlProfile == null || oracleProfile == null)
        {
            TempData["ErrorMessage"] = "אחד מפרופילי החיבור שנבחרו אינו תקין או שאינו קיים במערכת.";
            TempData["SelectedSqlProfile"] = sqlProfileName;
            TempData["SelectedOracleProfile"] = oracleProfileName;
            return RedirectToAction("Index", "Home");
        }

        string sqlConnectionString = sqlProfile.ConnectionString;
        string oracleConnectionString = oracleProfile.ConnectionString;

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
            TempData["SelectedSqlProfile"] = sqlProfileName;
            TempData["SelectedOracleProfile"] = oracleProfileName;
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

    // פעולה (Action) המטפלת בקבלת הטבלאות/תצוגות שנבחרו וביצוע השוואת סכמה להצגה במסך שלב 4
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SchemaReview(string sqlTable, string oracleTable)
    {
        // שליפת מחרוזות החיבור הפעילות מתוך ה-Session של המשתמש
        var sqlConnectionString = HttpContext.Session.GetString("SqlConnectionString");
        var oracleConnectionString = HttpContext.Session.GetString("OracleConnectionString");

        // בדיקה האם פג תוקף הסשן או שלא הועברו טבלאות בבקשה
        if (string.IsNullOrEmpty(sqlConnectionString) || string.IsNullOrEmpty(oracleConnectionString) ||
            string.IsNullOrEmpty(sqlTable) || string.IsNullOrEmpty(oracleTable))
        {
            // הגדרת הודעת שגיאה מתאימה למשתמש
            TempData["ErrorMessage"] = "פג תוקף החיבור או שלא נבחרו טבלאות. נא להתחבר מחדש.";
            // הפניה מחדש לדף הבית
            return RedirectToAction("Index", "Home");
        }

        try
        {
            // קריאה לשירות ההשוואה לביצוע השוואת סכמה מקיפה בין הטבלאות במקביל
            var schemaReviewModel = await _compareService.CompareSchemaAsync(
                sqlConnectionString,
                oracleConnectionString,
                sqlTable,
                oracleTable);

            // שמירת שמות הטבלאות שנבחרו בסשן להמשך שלבי העבודה הבאים
            HttpContext.Session.SetString("SelectedSqlTable", sqlTable);
            HttpContext.Session.SetString("SelectedOracleTable", oracleTable);

            // החזרת View סקירת הסכמה SchemaReview יחד עם המודל שנבנה
            return View("SchemaReview", schemaReviewModel);
        }
        catch (Exception ex)
        {
            // במקרה של שגיאה במהלך שליפת הסכמות, נשמור את הודעת השגיאה
            TempData["ErrorMessage"] = $"שגיאה בטעינת סקירת הסכמה: {ex.Message}";
            // החזרה לדף הבית לחיבור מחדש
            return RedirectToAction("Index", "Home");
        }
    }


    // פעולה (Action) המבצעת את אלגוריתם ההשוואה החכם בפועל (שלבים 5 ו-6)
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
        // שליפת מחרוזות החיבור מה-Session של המשתמש בצורה מאובטחת
        var sqlConnectionString = HttpContext.Session.GetString("SqlConnectionString");
        var oracleConnectionString = HttpContext.Session.GetString("OracleConnectionString");

        // בדיקה האם פג תוקף החיבור למסדי הנתונים
        if (string.IsNullOrEmpty(sqlConnectionString) || string.IsNullOrEmpty(oracleConnectionString))
        {
            // הגדרת הודעה והפניה לדף הבית
            TempData["ErrorMessage"] = "פג תוקף החיבור למסדי הנתונים. נא להתחבר מחדש.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            // קריאה לשירות ההשוואה החכם לביצוע הרצת ההשוואה עם מנגנון הקיבוץ והנרמול
            var smartResultsViewModel = await _compareService.SmartCompareAsync(
                sqlConnectionString,
                oracleConnectionString,
                sqlTable,
                oracleTable,
                sourceFields,
                targetFields,
                fieldRoles,
                maxRows);

            // החזרת תצוגת התוצאות Results יחד עם הנתונים והגרפים שחושבו
            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            // במקרה של שגיאה במהלך ההשוואה, החזרת הודעה ממוקדת
            TempData["ErrorMessage"] = $"נכשלה הרצת השוואת הנתונים: {ex.Message}";
            // הפניה לדף הבית
            return RedirectToAction("Index", "Home");
        }
    }
}
