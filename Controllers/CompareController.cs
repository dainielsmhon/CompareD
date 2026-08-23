using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using CompareD.Models;
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
    private readonly FileLimits _fileLimits;
    private readonly ConnectRateLimiter _connectRateLimiter;

    public CompareController(
        ICompareService compareService, 
        ILogger<CompareController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<FileLimits> fileLimitsOptions,
        ConnectRateLimiter connectRateLimiter)
    {
        _compareService = compareService;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector("CompareD.ConnectionStrings");
        _fileLimits = fileLimitsOptions.Value;
        _connectRateLimiter = connectRateLimiter;
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

    private void ClearConnectionStringsFromSession()
    {
        HttpContext.Session.Remove("SourceConnectionString");
        HttpContext.Session.Remove("TargetConnectionString");
        HttpContext.Session.Remove("SourceProvider");
        HttpContext.Session.Remove("TargetProvider");
        HttpContext.Session.Remove("SelectedSourceTable");
        HttpContext.Session.Remove("SelectedTargetTable");
    }

    private static string BuildConnectionString(string provider, string server, string database, string host, string port, string sid, string username, string password)
    {
        if (provider == "SQLServer") {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = username,
                Password = password,
                ApplicationIntent = ApplicationIntent.ReadOnly,
                TrustServerCertificate = true
            };
            return builder.ConnectionString;
        } else if (provider == "Oracle") {
            string actualHost = string.IsNullOrWhiteSpace(host) ? server : host;
            return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={actualHost})(PORT={port}))(CONNECT_DATA=(SID={sid})));User Id={username};Password={password};";
        }
        return string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> TestConnection(string provider, string server, string database, string host, string port, string sid, string username, string password)
    {
        try
        {
            string connectionString = BuildConnectionString(provider, server, database, host, port, sid, username, password);
            if (provider == "SQLServer") {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
            } else if (provider == "Oracle") {
                using var conn = new OracleConnection(connectionString);
                await conn.OpenAsync();
            } else {
                return Json(new { success = false, message = "ספק מסד הנתונים אינו מוכר." });
            }
            return Json(new { success = true, message = "חיבור בוצע בהצלחה!" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestConnection failed for provider: {Provider}, server: {Server}", provider, server);
            string hebrewMessage;
            string msg = ex.Message;
            if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) || msg.Contains("Timeout"))
                hebrewMessage = "פסק הזמן חלף (Timeout). בדקו שהשרת פועל ונגיש.";
            else if (msg.Contains("Login failed") || msg.Contains("password") || msg.Contains("ORA-01017"))
                hebrewMessage = "שם משתמש או סיסמה שגויים. אנא בדקו את פרטי ההתחברות.";
            else if (msg.Contains("server was not found") || msg.Contains("not accessible") || msg.Contains("ORA-12541") || msg.Contains("ORA-12170"))
                hebrewMessage = "השרת לא נמצא או אינו נגיש. בדקו את כתובת השרת והפורט.";
            else if (msg.Contains("Cannot open database") || msg.Contains("ORA-12514"))
                hebrewMessage = "מסד הנתונים לא נמצא. בדקו את שם הדטאבייס / Service Name.";
            else if (msg.Contains("network") || msg.Contains("Named Pipes") || msg.Contains("TCP"))
                hebrewMessage = "שגיאת רשת. בדקו שה-SQL Server/Oracle מקשיבים לחיבורים מרחוק.";
            else
                hebrewMessage = "לא ניתן להתחבר למסד הנתונים. בדקו את פרטי החיבור ונסו שוב.";
            return Json(new { success = false, message = hebrewMessage });
        }
    }


    // מקבל אישורי גישה דינמיים מטופס ממשק המשתמש, בונה מחרוזות חיבור בזמן ריצה,
    // ומאמת את שני חיבורי מסד הנתונים. אישורי הגישה נשמרים בסשן בלבד (ארעי).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(
        string sourceProvider, string sourceServer, string sourceDatabase, string sourceHost, string sourcePort, string sourceSid, string sourceUsername, string sourcePassword,
        string targetProvider, string targetServer, string targetDatabase, string targetHost, string targetPort, string targetSid, string targetUsername, string targetPassword)
    {
        var rateLimitKey = User.Identity?.Name ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        if (!_connectRateLimiter.IsAllowed(rateLimitKey))
        {
            TempData["ErrorMessage"] = "יותר מדי ניסיונות חיבור. נא לנסות שוב בעוד מספר דקות.";
            return View("~/Views/Home/Index.cshtml");
        }

        string sourceConnectionString = BuildConnectionString(sourceProvider, sourceServer, sourceDatabase, sourceHost, sourcePort, sourceSid, sourceUsername, sourcePassword);
        string targetConnectionString = BuildConnectionString(targetProvider, targetServer, targetDatabase, targetHost, targetPort, targetSid, targetUsername, targetPassword);

        var sourceObjects = new List<DatabaseObject>();
        var targetObjects = new List<DatabaseObject>();
        try
        {
            sourceObjects = await _compareService.GetDatabaseObjectsAsync(sourceConnectionString, sourceProvider);
            targetObjects = await _compareService.GetDatabaseObjectsAsync(targetConnectionString, targetProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection error in Connect action");
            TempData["ErrorMessage"] = "שגיאה בשליפת הטבלאות. ייתכן שהשרת איטי או עמוס. פרטים: " + ex.Message;
            return View("~/Views/Home/Index.cshtml");
        }

        AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "DatabaseConnect", $"Connected successfully");

        HttpContext.Session.SetString("SourceConnectionString", ProtectConnectionString(sourceConnectionString));
        HttpContext.Session.SetString("TargetConnectionString", ProtectConnectionString(targetConnectionString));
        HttpContext.Session.SetString("SourceProvider", sourceProvider);
        HttpContext.Session.SetString("TargetProvider", targetProvider);

        var viewModel = new TableSelectionViewModel
        {
            SourceTables = sourceObjects,
            TargetTables = targetObjects
        };

        return View("SelectTables", viewModel);
    }

    // פעולה (Action) המטפלת בקבלת הטבלאות/תצוגות שנבחרו וביצוע השוואת סכמה להצגה במסך שלב 4
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SchemaReview(string sourceTable, string targetTable)
    {
        var protectedSource = HttpContext.Session.GetString("SourceConnectionString");
        var protectedTarget = HttpContext.Session.GetString("TargetConnectionString");
        var sourceConnectionString = UnprotectConnectionString(protectedSource);
        var targetConnectionString = UnprotectConnectionString(protectedTarget);
        var sourceProvider = HttpContext.Session.GetString("SourceProvider") ?? "SQLServer";
        var targetProvider = HttpContext.Session.GetString("TargetProvider") ?? "Oracle";

        if (string.IsNullOrEmpty(sourceConnectionString) || string.IsNullOrEmpty(targetConnectionString) ||
            string.IsNullOrEmpty(sourceTable) || string.IsNullOrEmpty(targetTable))
        {
            TempData["ErrorMessage"] = "פג תוקף החיבור המאובטח או שלא נבחרו טבלאות. נא להתחבר מחדש.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            var schemaReviewModel = await _compareService.CompareSchemaAsync(
                sourceConnectionString,
                sourceProvider,
                targetConnectionString,
                targetProvider,
                sourceTable,
                targetTable);

            HttpContext.Session.SetString("SelectedSourceTable", sourceTable);
            HttpContext.Session.SetString("SelectedTargetTable", targetTable);

            return View("SchemaReview", schemaReviewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching schema details");
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "SchemaReviewError",
                $"שגיאה בטעינת סכמה עבור טבלאות {sourceTable}/{targetTable} | {ex.Message}", "Failed");
            // רישום שגיאת מערכת עם תרגום עברי
            CompareD.Models.SystemLogger.LogError(500, ex.Message,
                $"/Compare/SchemaReview?sql={sourceTable}&oracle={targetTable}",
                User.Identity?.Name ?? "Unknown");
            TempData["ErrorMessage"] = "התרחשה שגיאה בעת טעינת סקירת הסכמה ומבנה הטבלאות.";
            ClearConnectionStringsFromSession();
            return RedirectToAction("Index", "Home");
        }
    }

    // פעולה (Action) המבצעת את אלגוריתם ההשוואה החכם בפועל (שלבים 5 ו-6)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareData(
        string sourceTable, 
        string targetTable, 
        string mappingMode, 
        List<string> sourceFields, 
        List<string> targetFields, 
        List<string> fieldRoles,
        int maxRows,
        string filterActive,
        List<string> filterColumn,
        List<string> filterOperator,
        List<string> filterValue)
    {
        var protectedSource = HttpContext.Session.GetString("SourceConnectionString");
        var protectedTarget = HttpContext.Session.GetString("TargetConnectionString");
        var sourceConnectionString = UnprotectConnectionString(protectedSource);
        var targetConnectionString = UnprotectConnectionString(protectedTarget);
        var sourceProvider = HttpContext.Session.GetString("SourceProvider") ?? "SQLServer";
        var targetProvider = HttpContext.Session.GetString("TargetProvider") ?? "Oracle";

        if (string.IsNullOrEmpty(sourceConnectionString) || string.IsNullOrEmpty(targetConnectionString))
        {
            TempData["ErrorMessage"] = "פג תוקף החיבור המאובטח למסדי הנתונים. נא להתחבר מחדש.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var smartResultsViewModel = await _compareService.SmartCompareAsync(
                sourceConnectionString,
                sourceProvider,
                targetConnectionString,
                targetProvider,
                sourceTable,
                targetTable,
                sourceFields,
                targetFields,
                fieldRoles,
                maxRows,
                filterActive,
                filterColumn,
                filterOperator,
                filterValue);

            stopwatch.Stop();

            if (CompareD.Models.SystemSettingsStore.Get().IsHealthMonitorEnabled)
                CompareD.Models.HealthMonitor.RecordProcessing(stopwatch.ElapsedMilliseconds, User.Identity?.Name ?? "Unknown");

            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "DatabaseCompare",
                $"השוואת DB: {sourceTable} vs {targetTable} | Mode:{mappingMode} | Rows:{maxRows} | Time:{stopwatch.ElapsedMilliseconds}ms", "Success");

            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during smart data comparison");
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "DatabaseCompareError",
                $"כישלון השוואת DB: {sourceTable}/{targetTable} | {ex.Message}", "Failed");
            // רישום שגיאת מערכת
            CompareD.Models.SystemLogger.LogError(500, ex.Message, "/Compare/CompareData", User.Identity?.Name ?? "Unknown");
            TempData["ErrorMessage"] = "הרצת השוואת הנתונים נכשלה עקב שגיאה פנימית.";
            return RedirectToAction("Index", "Home");
        }
        finally
        {
            ClearConnectionStringsFromSession();
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

        // הגנת DoS: דחיית קבצים החורגים מהמגבלה המוגדרת
        long maxUploadSizeBytes = (long)_fileLimits.MaxPerFileMb * 1024 * 1024;
        if (file1.Length > maxUploadSizeBytes || file2.Length > maxUploadSizeBytes)
        {
            TempData["ErrorMessage"] = $"גודל הקובץ חורג מהמגבלה המותרת של {_fileLimits.MaxPerFileMb}MB. אנא הגבילו את גודל הקבצים.";
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

            // תיעוד העלאת קבצים מוצלחת ב-Audit Log
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FilesUpload", $"Uploaded files for comparison: {file1.FileName} ({file1.Length} bytes), {file2.FileName} ({file2.Length} bytes)");

            return RedirectToAction("CompareFilesSchemaReview");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing and uploading files");
            // רישום כישלון העלאת קבצים ב-Audit Log עם סטטוס Failed - זה מה שחסר!
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FileUploadError",
                $"כישלון העלאת קבצים | קובץ1: {file1?.FileName ?? "null"} | קובץ2: {file2?.FileName ?? "null"} | שגיאה: {ex.Message}", "Failed");
            // רישום שגיאת מערכת עם קוד 500
            CompareD.Models.SystemLogger.LogError(500, ex.Message, "/Compare/CompareUploadedFiles", User.Identity?.Name ?? "Unknown");
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
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewFilterData(
        string sourceTable, 
        string targetTable,
        List<string> filterColumn,
        List<string> filterOperator,
        List<string> filterValue)
    {
        var protectedSource = HttpContext.Session.GetString("SourceConnectionString");
        var protectedTarget = HttpContext.Session.GetString("TargetConnectionString");
        var sourceConnectionString = UnprotectConnectionString(protectedSource);
        var targetConnectionString = UnprotectConnectionString(protectedTarget);
        var sourceProvider = HttpContext.Session.GetString("SourceProvider") ?? "SQLServer";
        var targetProvider = HttpContext.Session.GetString("TargetProvider") ?? "Oracle";

        if (string.IsNullOrEmpty(sourceConnectionString) || string.IsNullOrEmpty(targetConnectionString))
            return Json(new { success = false, error = "פג תוקף החיבור המאובטח." });

        try
        {
            var previewData = await _compareService.GetPreviewDataAsync(
                sourceConnectionString, sourceProvider,
                targetConnectionString, targetProvider,
                sourceTable, targetTable,
                filterColumn, filterOperator, filterValue);
                
            return Json(new { success = true, data = previewData });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

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

            var sourceHeaders = CsvParser.GetHeaders(path1!);
            var targetHeaders = CsvParser.GetHeaders(path2!);
            var allowedRoles = new HashSet<string>(StringComparer.Ordinal) { "Key", "Compare" };

            // בניית רשימות המיפוי הסופיות
            var finalSourceFields = new List<string>();
            var finalTargetFields = new List<string>();
            var finalFieldRoles = new List<string>();

            if (selectedSourceFields != null)
            {
                foreach (var sf in selectedSourceFields)
                {
                    if (!sourceHeaders.Contains(sf, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? tf = Request.Form["targetMapping_" + sf];
                    string? role = Request.Form["roleMapping_" + sf];

                    if (string.IsNullOrEmpty(tf) || string.IsNullOrEmpty(role))
                    {
                        continue;
                    }

                    if (!targetHeaders.Contains(tf, StringComparer.OrdinalIgnoreCase) || !allowedRoles.Contains(role))
                    {
                        continue;
                    }

                    finalSourceFields.Add(sf);
                    finalTargetFields.Add(tf);
                    finalFieldRoles.Add(role);
                }
            }

            // וידוא שנבחר לפחות שדה מפתח אחד
            if (!finalFieldRoles.Contains("Key"))
            {
                TempData["ErrorMessage"] = "חובה לבחור לפחות עמודת מפתח אחת (Key) לביצוע ההשוואה.";
                return RedirectToAction("CompareFilesSchemaReview");
            }

            // תיעוד הרצת השוואת קבצים ב-Audit Log
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FilesCompare", $"Compared Csv/Excel files. Source: {name1 ?? Path.GetFileName(path1!)}, Target: {name2 ?? Path.GetFileName(path2!)}. MaxRows: {maxRows}");

            // מדידת זמן עיבוד השוואת הקבצים לצורך Health Monitor
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var smartResultsViewModel = _compareService.CompareInMemoryDatasets(
                sqlDataLimited,
                oracleDataLimited,
                name1 ?? Path.GetFileName(path1!),
                name2 ?? Path.GetFileName(path2!),
                finalSourceFields,
                targetFields: finalTargetFields,
                fieldRoles: finalFieldRoles);

            sw.Stop();

            // רישום זמן עיבוד ב-Health Monitor לנתוני דאשבורד אמיתיים
            if (CompareD.Models.SystemSettingsStore.Get().IsHealthMonitorEnabled)
                CompareD.Models.HealthMonitor.RecordProcessing(sw.ElapsedMilliseconds, User.Identity?.Name ?? "Unknown");

            // תיעוד השוואת קבצים מוצלחת ב-Audit Log
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FilesCompare",
                $"השוואת קבצים: {name1 ?? Path.GetFileName(path1!)} vs {name2 ?? Path.GetFileName(path2!)} | שורות:{maxRows} | זמן:{sw.ElapsedMilliseconds}ms", "Success");

            return View("Results", smartResultsViewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running in-memory files comparison");
            // רישום כישלון השוואת קבצים ב-Audit Log עם סטטוס Failed
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FilesCompareError",
                $"כישלון השוואת קבצים | {ex.Message}", "Failed");
            // רישום שגיאת מערכת
            CompareD.Models.SystemLogger.LogError(500, ex.Message, "/Compare/RunFilesComparison", User.Identity?.Name ?? "Unknown");
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

        [HttpPost]
        public IActionResult DownloadReport([FromForm] string content, [FromForm] string fileName, [FromForm] string contentType)
        {
            if (string.IsNullOrEmpty(content)) return BadRequest();
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var fullBytes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(bom, bytes));
            return File(fullBytes, contentType, fileName);
        }
    }
