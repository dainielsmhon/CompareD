using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompareD.Services;

namespace CompareD.Controllers;

// בקר ההשוואה הראשי המטפל בחיבורי מסד נתונים דינמיים ובהשוואות קבצים
[Authorize]
public class CompareController : Controller
{
    private readonly ICompareService _compareService;
    private readonly ILogger<CompareController> _logger;
    private readonly IDataProtector _protector;

    // גודל קובץ העלאה מקסימלי מותר: 50 מגה-בייט
    private const long MaxUploadSizeBytes = 50L * 1024 * 1024;

    public CompareController(
        ICompareService compareService, 
        ILogger<CompareController> logger,
        IDataProtectionProvider dataProtectionProvider)
    {
        _compareService = compareService;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector("CompareD.ConnectionStrings");
    }

    // פונקציות עזר להצפנה ופענוח של מחרוזות החיבור ב-Session
    private string ProtectConnectionString(string connectionString)
    {
        return _protector.Protect(connectionString);
    }

    private string? UnprotectConnectionString(string? protectedString)
    {
        if (string.IsNullOrEmpty(protectedString)) return null;
        try
        {
            return _protector.Unprotect(protectedString);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    // מקבל אישורי גישה דינמיים מטופס ממשק המשתמש, בונה מחרוזות חיבור בזמן ריצה,
    // ומאמת את שני חיבורי מסד הנתונים. אישורי הגישה נשמרים בסשן בלבד (ארעי).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(
        string sourceServer, string sourceDatabase, string sourceUsername, string sourcePassword,
        string targetServer, string targetDatabase, string targetUsername, string targetPassword)
    {
        // אימות שכל שדות החובה קיימים
        if (string.IsNullOrWhiteSpace(sourceServer) || string.IsNullOrWhiteSpace(sourceDatabase) ||
            string.IsNullOrWhiteSpace(sourceUsername) || string.IsNullOrWhiteSpace(sourcePassword) ||
            string.IsNullOrWhiteSpace(targetServer) || string.IsNullOrWhiteSpace(targetDatabase) ||
            string.IsNullOrWhiteSpace(targetUsername) || string.IsNullOrWhiteSpace(targetPassword))
        {
            TempData["ErrorMessage"] = "יש למלא את כל שדות החיבור עבור המקור והיעד.";
            return RedirectToAction("Index", "Home");
        }

        // בניית מחרוזת חיבור ל-SQL Server באופן דינמי מקלט הטופס (המקור הוא תמיד SQL Server)
        // הערה ביטחונית והחרגה: מאחר שהמערכת רצה ברשת ארגונית פנימית מוגנת, שרתי ה-SQL Server משתמשים בתעודות חתימה עצמית (Self-Signed Certificates).
        // לכן, השימוש ב-TrustServerCertificate=True מוגדר כברירת מחדל כדי לאפשר התחברות תקינה. בסביבת ייצור קשיחה מחוץ לרשת הפנימית,
        // מומלץ להגדיר ערך זה כ-False ולהתקין את תעודות השרת הנדרשות.
        string sqlConnectionString =
            $"Server={sourceServer};Database={sourceDatabase};User Id={sourceUsername};Password={sourcePassword};TrustServerCertificate=True;";

        // בניית מחרוזת חיבור ל-Oracle באופן דינמי מקלט הטופס (היעד הוא תמיד Oracle)
        string oracleConnectionString =
            $"User Id={targetUsername};Password={targetPassword};Data Source={targetServer}/{targetDatabase};";

        var sqlObjects = new List<DatabaseObject>();
        var oracleObjects = new List<DatabaseObject>();
        string? errorMessage = null;

        try
        {
            // בדיקת שני החיבורים במקביל לשיפור הביצועים
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        sqlObjects = await _compareService.GetSqlObjectsAsync(sqlConnectionString);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to SQL Server using dynamic string");
                        throw new Exception("שגיאה בחיבור ל-SQL Server");
                    }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        oracleObjects = await _compareService.GetOracleObjectsAsync(oracleConnectionString);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to connect to Oracle using dynamic string");
                        throw new Exception("שגיאה בחיבור ל-Oracle");
                    }
                })
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection error in Connect action");
            errorMessage = "התרחשה שגיאה בעת התחברות למסדי הנתונים. אנא בדקו את פרטי החיבור שהזנתם ונסו שוב.";
        }

        if (errorMessage != null)
        {
            TempData["ErrorMessage"] = errorMessage;
            return RedirectToAction("Index", "Home");
        }

        // שמירת מחרוזות החיבור בסשן בצורה מוצפנת ומאובטחת (Data Protection)
        HttpContext.Session.SetString("SqlConnectionString", ProtectConnectionString(sqlConnectionString));
        HttpContext.Session.SetString("OracleConnectionString", ProtectConnectionString(oracleConnectionString));

        var viewModel = new TableSelectionViewModel
        {
            SqlTables = sqlObjects,
            OracleTables = oracleObjects
        };

        return View("SelectTables", viewModel);
    }

    // פעולה (Action) המטפלת בקבלת הטבלאות/תצוגות שנבחרו וביצוע השוואת סכמה להצגה במסך שלב 4
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SchemaReview(string sqlTable, string oracleTable)
    {
        // שליפת מחרוזות החיבור הפעילות מתוך ה-Session של המשתמש ופענוחן
        var protectedSql = HttpContext.Session.GetString("SqlConnectionString");
        var protectedOracle = HttpContext.Session.GetString("OracleConnectionString");
        var sqlConnectionString = UnprotectConnectionString(protectedSql);
        var oracleConnectionString = UnprotectConnectionString(protectedOracle);

        // בדיקה האם פג תוקף הסשן או שלא הועברו טבלאות בבקשה
        if (string.IsNullOrEmpty(sqlConnectionString) || string.IsNullOrEmpty(oracleConnectionString) ||
            string.IsNullOrEmpty(sqlTable) || string.IsNullOrEmpty(oracleTable))
        {
            // הגדרת הודעת שגיאה מתאימה למשתמש
            TempData["ErrorMessage"] = "פג תוקף החיבור המאובטח או שלא נבחרו טבלאות. נא להתחבר מחדש.";
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
            _logger.LogError(ex, "Error fetching schema details for SQL: {SqlTable}, Oracle: {OracleTable}", sqlTable, oracleTable);
            TempData["ErrorMessage"] = "התרחשה שגיאה בעת טעינת סקירת הסכמה ומבנה הטבלאות.";
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
        // שליפת מחרוזות החיבור מה-Session של המשתמש ופענוחן בצורה מאובטחת
        var protectedSql = HttpContext.Session.GetString("SqlConnectionString");
        var protectedOracle = HttpContext.Session.GetString("OracleConnectionString");
        var sqlConnectionString = UnprotectConnectionString(protectedSql);
        var oracleConnectionString = UnprotectConnectionString(protectedOracle);

        // בדיקה האם פג תוקף החיבור למסדי הנתונים
        if (string.IsNullOrEmpty(sqlConnectionString) || string.IsNullOrEmpty(oracleConnectionString))
        {
            // הגדרת הודעה והפניה לדף הבית
            TempData["ErrorMessage"] = "פג תוקף החיבור המאובטח למסדי הנתונים. נא להתחבר מחדש.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            var smartResultsViewModel = await _compareService.SmartCompareAsync(
                sqlConnectionString,
                oracleConnectionString,
                sqlTable,
                oracleTable,
                sourceFields,
                targetFields,
                fieldRoles,
                maxRows);

            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during smart data comparison for SQL: {SqlTable}, Oracle: {OracleTable}", sqlTable, oracleTable);
            TempData["ErrorMessage"] = "הרצת השוואת הנתונים נכשלה עקב שגיאה פנימית.";
            return RedirectToAction("Index", "Home");
        }
        finally
        {
            // ניקוי מחרוזות החיבור מהסשן מייד לאחר השימוש (אבטחה ארעית)
            HttpContext.Session.Remove("SqlConnectionString");
            HttpContext.Session.Remove("OracleConnectionString");
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

        // הגנת DoS: דחיית קבצים החורגים ממגבלת 50 מגה-בייט לקובץ
        if (file1.Length > MaxUploadSizeBytes || file2.Length > MaxUploadSizeBytes)
        {
            TempData["ErrorMessage"] = $"גודל הקובץ חורג מהמגבלה המותרת של 50MB. אנא הגבילו את גודל הקבצים.";
            return RedirectToAction("CompareFilesSetup");
        }

        try
        {
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_uploads");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            // יצירת שמות ייחודיים נקיים תוך שמירה על הסיומת
            var ext1 = Path.GetExtension(file1.FileName).ToLower();
            var ext2 = Path.GetExtension(file2.FileName).ToLower();

            // אימות שקיבלנו קובצי CSV או XLSX
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

            // שמירה בסשן
            HttpContext.Session.SetString("CsvSourceFilePath", path1);
            HttpContext.Session.SetString("CsvTargetFilePath", path2);
            HttpContext.Session.SetString("CsvSourceFileName", file1.FileName);
            HttpContext.Session.SetString("CsvTargetFileName", file2.FileName);

            return RedirectToAction("CompareFilesSchemaReview");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing and uploading files");
            TempData["ErrorMessage"] = "שגיאה בעיבוד או בהעלאת הקבצים. נא לוודא שהקובץ אינו פגום או פתוח בתוכנה אחרת.";
            return RedirectToAction("CompareFilesSetup");
        }
    }

    // פעולת POST המאפשרת להחליף בין קובץ המקור לקובץ היעד דינמית (שינוי מצב שרת)
    [HttpPost]
    [ValidateAntiForgeryToken]
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
            _logger.LogError(ex, "Error reading file headers for schema mapping");
            TempData["ErrorMessage"] = "שגיאה בטעינת עמודות וכותרות הקבצים. אנא בדקו את תקינות קבצי ה-CSV/Excel.";
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
            if (maxRows <= 0) maxRows = 1000;
            if (maxRows > 10000) maxRows = 10000;

            // אופטימיזציית ביצועים וזיכרון: שליפה והזרמה (Streaming) של קובצי המקור והיעד בהתאם למספר השורות המבוקש בלבד
            var sqlDataLimited = CsvParser.ParseFile(path1!).Take(maxRows).ToList();
            var oracleDataLimited = CsvParser.ParseFile(path2!).Take(maxRows).ToList();

            if (sqlDataLimited.Count == 0 || oracleDataLimited.Count == 0)
            {
                TempData["ErrorMessage"] = "אחד הקבצים או שניהם התבררו כריקים בעת הרצת ההשוואה.";
                return RedirectToAction("CompareFilesSetup");
            }

            // בניית רשימות המיפוי הסופיות
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

            // וידוא שנבחר לפחות שדה מפתח אחד
            if (!finalFieldRoles.Contains("Key"))
            {
                TempData["ErrorMessage"] = "חובה לבחור לפחות עמודת מפתח אחת (Key) לביצוע ההשוואה.";
                return RedirectToAction("CompareFilesSchemaReview");
            }

            var smartResultsViewModel = _compareService.CompareInMemoryDatasets(
                sqlDataLimited,
                oracleDataLimited,
                name1 ?? Path.GetFileName(path1!),
                name2 ?? Path.GetFileName(path2!),
                finalSourceFields,
                targetFields: finalTargetFields,
                fieldRoles: finalFieldRoles);

            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running in-memory files comparison");
            TempData["ErrorMessage"] = "שגיאה התרחשה במהלך הרצת השוואת הקבצים.";
            return RedirectToAction("CompareFilesSetup");
        }
        finally
        {
            // מחיקת קבצים זמניים מייד לאחר טעינת הנתונים לזיכרון (ניקוי שטח אחסון)
            if (!string.IsNullOrEmpty(path1) && System.IO.File.Exists(path1))
            {
                try { System.IO.File.Delete(path1); } catch {}
            }
            if (!string.IsNullOrEmpty(path2) && System.IO.File.Exists(path2))
            {
                try { System.IO.File.Delete(path2); } catch {}
            }

            // ניקוי נתיבי הקבצים מהסשן מייד לאחר המחיקה
            HttpContext.Session.Remove("CsvSourceFilePath");
            HttpContext.Session.Remove("CsvTargetFilePath");
            HttpContext.Session.Remove("CsvSourceFileName");
            HttpContext.Session.Remove("CsvTargetFileName");
        }
    }


}
