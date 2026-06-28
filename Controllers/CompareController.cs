using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CompareD.Services;

namespace CompareD.Controllers;

// Main comparison controller handling dynamic DB connections and file comparisons
[Authorize]
public class CompareController : Controller
{
    private readonly ICompareService _compareService;

    // Maximum allowed upload file size: 50 MB
    private const long MaxUploadSizeBytes = 50L * 1024 * 1024;

    public CompareController(ICompareService compareService)
    {
        _compareService = compareService;
    }

    // Accepts dynamic credentials from the UI form, builds connection strings at runtime,
    // and validates both DB connections. Credentials are stored only in Session (ephemeral).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(
        string sourceServer, string sourceDatabase, string sourceUsername, string sourcePassword,
        string targetServer, string targetDatabase, string targetUsername, string targetPassword)
    {
        // Validate that all required fields are present
        if (string.IsNullOrWhiteSpace(sourceServer) || string.IsNullOrWhiteSpace(sourceDatabase) ||
            string.IsNullOrWhiteSpace(sourceUsername) || string.IsNullOrWhiteSpace(sourcePassword) ||
            string.IsNullOrWhiteSpace(targetServer) || string.IsNullOrWhiteSpace(targetDatabase) ||
            string.IsNullOrWhiteSpace(targetUsername) || string.IsNullOrWhiteSpace(targetPassword))
        {
            TempData["ErrorMessage"] = "יש למלא את כל שדות החיבור עבור המקור והיעד.";
            return RedirectToAction("Index", "Home");
        }

        // Build SQL Server connection string dynamically from form input (Source is always SQL Server)
        string sqlConnectionString =
            $"Server={sourceServer};Database={sourceDatabase};User Id={sourceUsername};Password={sourcePassword};TrustServerCertificate=True;";

        // Build Oracle connection string dynamically from form input (Target is always Oracle)
        string oracleConnectionString =
            $"User Id={targetUsername};Password={targetPassword};Data Source={targetServer}/{targetDatabase};";

        var sqlObjects = new List<DatabaseObject>();
        var oracleObjects = new List<DatabaseObject>();
        string? errorMessage = null;

        try
        {
            // Test both connections in parallel for performance
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    try
                    {
                        sqlObjects = await _compareService.GetSqlObjectsAsync(sqlConnectionString);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"שגיאה בחיבור ל-SQL Server: {ex.Message}");
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
                        throw new Exception($"שגיאה בחיבור ל-Oracle: {ex.Message}");
                    }
                })
            );
        }
        catch (Exception ex)
        {
            errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }

        if (errorMessage != null)
        {
            TempData["ErrorMessage"] = errorMessage;
            return RedirectToAction("Index", "Home");
        }

        // Store connection strings in Session only — never written to disk
        HttpContext.Session.SetString("SqlConnectionString", sqlConnectionString);
        HttpContext.Session.SetString("OracleConnectionString", oracleConnectionString);

        var viewModel = new TableSelectionViewModel
        {
            SqlTables = sqlObjects,
            OracleTables = oracleObjects
        };

        return View("SelectTables", viewModel);
    }

    // Demo Mode: bypasses real DB validation by injecting mock connection strings directly into Session.
    // Routes the user directly to the mock table selection screen.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConnectMock()
    {
        const string mockConn = "MockConnectionString";

        var sqlObjects = await _compareService.GetSqlObjectsAsync(mockConn);
        var oracleObjects = await _compareService.GetOracleObjectsAsync(mockConn);

        // Inject mock identifiers into session (ephemeral, no real credentials)
        HttpContext.Session.SetString("SqlConnectionString", mockConn);
        HttpContext.Session.SetString("OracleConnectionString", mockConn);

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
            TempData["ErrorMessage"] = $"נכשלה הרצת השוואת הנתונים: {ex.Message}";
            return RedirectToAction("Index", "Home");
        }
        finally
        {
            // Clear connection strings from Session immediately after use (ephemeral security)
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

        // DoS Protection: reject files exceeding the 50 MB per-file limit
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
                targetFields: finalTargetFields,
                fieldRoles: finalFieldRoles);

            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"שגיאה בהרצת השוואת הקבצים: {ex.Message}";
            return RedirectToAction("CompareFilesSetup");
        }
        finally
        {
            // Delete temp files immediately after loading data into memory (Storage Cleanup)
            if (!string.IsNullOrEmpty(path1) && System.IO.File.Exists(path1))
            {
                try { System.IO.File.Delete(path1); } catch {}
            }
            if (!string.IsNullOrEmpty(path2) && System.IO.File.Exists(path2))
            {
                try { System.IO.File.Delete(path2); } catch {}
            }

            // Clear file paths from Session immediately after deletion
            HttpContext.Session.Remove("CsvSourceFilePath");
            HttpContext.Session.Remove("CsvTargetFilePath");
            HttpContext.Session.Remove("CsvSourceFileName");
            HttpContext.Session.Remove("CsvTargetFileName");
        }
    }


}


