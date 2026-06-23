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

    // פעולה (Action) המציגה את ממשק העלאת הקבצים להשוואה
    [HttpGet]
    public IActionResult CompareFilesSetup()
    {
        return View();
    }

    // פעולה (Action) המקבלת את שני הקבצים, שומרת אותם זמנית ומפנה למסך הסקירה והמיפוי
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareUploadedFiles(Microsoft.AspNetCore.Http.IFormFile file1, Microsoft.AspNetCore.Http.IFormFile file2)
    {
        if (file1 == null || file2 == null || file1.Length == 0 || file2.Length == 0)
        {
            TempData["ErrorMessage"] = "יש לבחור את שני הקבצים להשוואה ולוודא שאינם ריקים.";
            return RedirectToAction("CompareFilesSetup");
        }

        try
        {
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_uploads");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            // Generate clean unique names retaining extension
            var ext1 = Path.GetExtension(file1.FileName).ToLower();
            var ext2 = Path.GetExtension(file2.FileName).ToLower();

            // Validate that we got CSV or XLSX files
            if ((ext1 != ".csv" && ext1 != ".xlsx") || (ext2 != ".csv" && ext2 != ".xlsx"))
            {
                TempData["ErrorMessage"] = "המערכת תומכת בקובצי CSV או XLSX (Excel) בלבד.";
                return RedirectToAction("CompareFilesSetup");
            }

            var path1 = Path.Combine(tempDir, $"{Guid.NewGuid()}{ext1}");
            var path2 = Path.Combine(tempDir, $"{Guid.NewGuid()}{ext2}");

            using (var stream1 = new FileStream(path1, FileMode.Create))
            {
                await file1.CopyToAsync(stream1);
            }
            using (var stream2 = new FileStream(path2, FileMode.Create))
            {
                await file2.CopyToAsync(stream2);
            }

            // Save in session
            HttpContext.Session.SetString("CsvSourceFilePath", path1);
            HttpContext.Session.SetString("CsvTargetFilePath", path2);
            HttpContext.Session.SetString("CsvSourceFileName", file1.FileName);
            HttpContext.Session.SetString("CsvTargetFileName", file2.FileName);

            return RedirectToAction("CompareFilesSchemaReview");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"שגיאה בהעלאת הקבצים: {ex.Message}";
            return RedirectToAction("CompareFilesSetup");
        }
    }

    // פעולה (Action) המאפשרת להחליף בין קובץ המקור לקובץ היעד דינמית
    [HttpGet]
    public IActionResult SwapFiles()
    {
        var path1 = HttpContext.Session.GetString("CsvSourceFilePath");
        var path2 = HttpContext.Session.GetString("CsvTargetFilePath");
        var name1 = HttpContext.Session.GetString("CsvSourceFileName");
        var name2 = HttpContext.Session.GetString("CsvTargetFileName");

        if (!string.IsNullOrEmpty(path1) && !string.IsNullOrEmpty(path2))
        {
            HttpContext.Session.SetString("CsvSourceFilePath", path2);
            HttpContext.Session.SetString("CsvTargetFilePath", path1);
            HttpContext.Session.SetString("CsvSourceFileName", name2 ?? "");
            HttpContext.Session.SetString("CsvTargetFileName", name1 ?? "");
        }

        return RedirectToAction("CompareFilesSchemaReview");
    }

    // פעולה (Action) המציגה את מסך סקירת הסכמה ומיפוי השדות עבור קבצים
    [HttpGet]
    public IActionResult CompareFilesSchemaReview()
    {
        var path1 = HttpContext.Session.GetString("CsvSourceFilePath");
        var path2 = HttpContext.Session.GetString("CsvTargetFilePath");
        var name1 = HttpContext.Session.GetString("CsvSourceFileName");
        var name2 = HttpContext.Session.GetString("CsvTargetFileName");

        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2) || !System.IO.File.Exists(path1) || !System.IO.File.Exists(path2))
        {
            TempData["ErrorMessage"] = "קובצי ההשוואה לא נמצאו. אנא העלו אותם מחדש.";
            return RedirectToAction("CompareFilesSetup");
        }

        try
        {
            var headers1 = CsvParser.GetHeaders(path1!);
            var headers2 = CsvParser.GetHeaders(path2!);

            if (headers1.Count == 0 || headers2.Count == 0)
            {
                TempData["ErrorMessage"] = "לא נמצאו עמודות או כותרות באחד הקבצים או בשניהם.";
                return RedirectToAction("CompareFilesSetup");
            }

            var model = new FilesSchemaReviewViewModel
            {
                SourceFileName = name1 ?? Path.GetFileName(path1!),
                TargetFileName = name2 ?? Path.GetFileName(path2!),
                SourceColumns = headers1,
                TargetColumns = headers2
            };

            return View("CompareFilesSchemaReview", model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"שגיאה בטעינת עמודות הקבצים: {ex.Message}";
            return RedirectToAction("CompareFilesSetup");
        }
    }

    // פעולה (Action) המבצעת את ההשוואה בפועל לפי המיפוי והגדרות המשתמש מהמסך
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RunFilesComparison(
        List<string> selectedSourceFields,
        int maxRows = 1000)
    {
        var path1 = HttpContext.Session.GetString("CsvSourceFilePath");
        var path2 = HttpContext.Session.GetString("CsvTargetFilePath");
        var name1 = HttpContext.Session.GetString("CsvSourceFileName");
        var name2 = HttpContext.Session.GetString("CsvTargetFileName");

        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2) || !System.IO.File.Exists(path1) || !System.IO.File.Exists(path2))
        {
            TempData["ErrorMessage"] = "פג תוקף הקבצים שהועלו. אנא העלו מחדש.";
            return RedirectToAction("CompareFilesSetup");
        }

        try
        {
            var sqlRawData = CsvParser.ParseFile(path1!);
            var oracleRawData = CsvParser.ParseFile(path2!);

            if (sqlRawData.Count == 0 || oracleRawData.Count == 0)
            {
                TempData["ErrorMessage"] = "אחד הקבצים או שניהם התבררו כריקים בעת הרצת ההשוואה.";
                return RedirectToAction("CompareFilesSetup");
            }

            // Build final mapping lists
            var finalSourceFields = new List<string>();
            var finalTargetFields = new List<string>();
            var finalFieldRoles = new List<string>();

            if (selectedSourceFields != null)
            {
                foreach (var sf in selectedSourceFields)
                {
                    string? tf = Request.Form["targetMapping_" + sf];
                    string? role = Request.Form["roleMapping_" + sf];

                    if (!string.IsNullOrEmpty(tf) && !string.IsNullOrEmpty(role))
                    {
                        finalSourceFields.Add(sf);
                        finalTargetFields.Add(tf);
                        finalFieldRoles.Add(role);
                    }
                }
            }

            // Verify that at least one Key field was selected
            if (!finalFieldRoles.Contains("Key"))
            {
                TempData["ErrorMessage"] = "חובה לבחור לפחות עמודת מפתח אחת (Key) לביצוע ההשוואה.";
                return RedirectToAction("CompareFilesSchemaReview");
            }

            if (maxRows <= 0) maxRows = 1000;
            if (maxRows > 10000) maxRows = 10000;

            var sqlDataLimited = sqlRawData.Take(maxRows).ToList();
            var oracleDataLimited = oracleRawData.Take(maxRows).ToList();

            var smartResultsViewModel = _compareService.CompareInMemoryDatasets(
                sqlDataLimited,
                oracleDataLimited,
                name1 ?? Path.GetFileName(path1!),
                name2 ?? Path.GetFileName(path2!),
                finalSourceFields,
                finalTargetFields,
                finalFieldRoles);

            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"שגיאה בהרצת השוואת הקבצים: {ex.Message}";
            return RedirectToAction("CompareFilesSetup");
        }
    }


}


