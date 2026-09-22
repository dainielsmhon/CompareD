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
    private readonly IConfiguration _configuration;

    public CompareController(
        ICompareService compareService,
        ILogger<CompareController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<FileLimits> fileLimitsOptions,
        ConnectRateLimiter connectRateLimiter,
        IConfiguration configuration)
    {
        _compareService = compareService;
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector("CompareD.ConnectionStrings");
        _fileLimits = fileLimitsOptions.Value;
        _connectRateLimiter = connectRateLimiter;
        _configuration = configuration;
    }

    // פונקציית עזר לבדיקה האם המשתמש הנוכחי מוגדר כמנהל מורשה בקובץ התצורה
    private bool IsUserAuthorized()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return false;

        var authorizedUsers = _configuration.GetSection("AdminSettings:AuthorizedUsers").Get<List<string>>();
        if (authorizedUsers == null || authorizedUsers.Count == 0) return false;

        return authorizedUsers.Any(u => string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
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

    // אימות רכיבי מחרוזת החיבור לאורקל: תווים אלו מאפשרים שבירת מבנה ה-DESCRIPTION
    // או הזרקת פרמטרים נוספים למחרוזת החיבור (Connection String Injection)
    private static void ValidateOracleConnectionParts(string host, string port, string sid)
    {
        char[] forbiddenChars = new[] { ';', '(', ')', '=', '\'', '"' };

        if (string.IsNullOrWhiteSpace(host) || host.IndexOfAny(forbiddenChars) >= 0)
            throw new ArgumentException("כתובת השרת (Host) של אורקל אינה תקינה.");

        // הפורט חייב להיות מספר שלם בטווח החוקי של פורטי TCP
        if (!int.TryParse(port?.Trim(), out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
            throw new ArgumentException("מספר הפורט של אורקל אינו תקין.");

        if (string.IsNullOrWhiteSpace(sid) || sid.IndexOfAny(forbiddenChars) >= 0)
            throw new ArgumentException("שם ה-SID / Service של אורקל אינו תקין.");
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

            // אימות הרכיבים לפני הבנייה כדי למנוע הזרקה למחרוזת החיבור
            ValidateOracleConnectionParts(actualHost, port, sid);

            // שימוש ב-Builder הרשמי של אורקל, בדיוק כפי שנעשה בצד SQL Server למעלה,
            // במקום הרכבה ידנית של המחרוזת שאינה מבצעת escaping לשם המשתמש ולסיסמה
            var builder = new OracleConnectionStringBuilder
            {
                DataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={actualHost.Trim()})(PORT={port.Trim()}))(CONNECT_DATA=(SID={sid.Trim()})))",
                UserID = username,
                Password = password
            };
            return builder.ConnectionString;
        }
        return string.Empty;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(string provider, string server, string database, string host, string port, string sid, string username, string password)
    {
        // הגבלת קצב על בדיקת החיבור: ללא הגבלה הפעולה מאפשרת סריקת פורטים ברשת הפנימית
        // וניסיונות brute-force על פרטי ההזדהות של מסדי הנתונים
        var rateLimitKey = User.Identity?.Name ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        if (!_connectRateLimiter.IsAllowed(rateLimitKey))
        {
            return Json(new { success = false, message = "יותר מדי ניסיונות בדיקת חיבור. נא לנסות שוב בעוד מספר דקות." });
        }

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
        catch (ArgumentException aex)
        {
            // שגיאות אימות פרטי החיבור נכתבות בעברית ומיועדות להצגה למשתמש
            _logger.LogWarning(aex, "TestConnection validation failed for provider: {Provider}", provider);
            return Json(new { success = false, message = aex.Message });
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

        string sourceConnectionString;
        string targetConnectionString;
        try
        {
            sourceConnectionString = BuildConnectionString(sourceProvider, sourceServer, sourceDatabase, sourceHost, sourcePort, sourceSid, sourceUsername, sourcePassword);
            targetConnectionString = BuildConnectionString(targetProvider, targetServer, targetDatabase, targetHost, targetPort, targetSid, targetUsername, targetPassword);
        }
        catch (ArgumentException aex)
        {
            // שגיאות אימות פרטי החיבור נכתבות בעברית ומיועדות להצגה למשתמש
            _logger.LogWarning(aex, "Connection string validation failed in Connect action");
            TempData["ErrorMessage"] = aex.Message;
            return View("~/Views/Home/Index.cshtml");
        }

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
            // הודעה גנרית בלבד: פרטי שגיאת מסד הנתונים נשמרים בלוגים ואינם נחשפים בממשק
            TempData["ErrorMessage"] = "שגיאה בשליפת הטבלאות. ייתכן שהשרת איטי או עמוס. נא לנסות שוב.";
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
            TargetTables = targetObjects,
            SourceProviderName = ProviderDisplay.Name(sourceProvider),
            TargetProviderName = ProviderDisplay.Name(targetProvider)
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

            // שמות הספקים לפי בחירת המשתמש, כדי שהמסך לא יקבע מראש
            // מי SQL Server ומי Oracle
            schemaReviewModel.SourceProviderName = ProviderDisplay.Name(sourceProvider);
            schemaReviewModel.TargetProviderName = ProviderDisplay.Name(targetProvider);

            HttpContext.Session.SetString("SelectedSourceTable", sourceTable);
            HttpContext.Session.SetString("SelectedTargetTable", targetTable);

            // רישום השלב שהצליח, ולא רק הכשל.
            //
            // עד כה נרשם כאן SchemaReviewError בלבד, ולכן שלב המיפוי היה
            // בלתי נראה בלוג הביקורת: עובד שהגיע לסקירת הסכימה ונטש שם
            // לא הותיר אף רשומה, ומשפך השלבים לא היה יכול להראות אותו.
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "SchemaReview",
                $"טען סכימה להשוואה: {sourceTable} מול {targetTable}");

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

    // בניית רשימת השדות המחושבים מתוך המערכים המקבילים שהוגשו בטופס.
    // כל שורת שדה מחושב שולחת את העמודות כמחרוזת מופרדת בפסיקים,
    // כדי לשמר התאמה בין המערכים גם כשמספר העמודות משתנה בין שורה לשורה.
    private static List<CompositeFieldDefinition> BuildCompositeFields(
        List<string>? names,
        List<string>? sourceKinds, List<string>? sourceColumns,
        List<string>? targetKinds, List<string>? targetColumns,
        List<string>? roles)
    {
        var result = new List<CompositeFieldDefinition>();
        if (names == null || names.Count == 0) return result;

        for (int i = 0; i < names.Count; i++)
        {
            string name = (names[i] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var srcCols = SplitCompositeColumns(sourceColumns, i);
            var tgtCols = SplitCompositeColumns(targetColumns, i);

            // שדה מחושב חסר עמודות באחד הצדדים אינו בר-השוואה ולכן נדלג עליו
            if (srcCols.Count == 0 || tgtCols.Count == 0) continue;

            result.Add(new CompositeFieldDefinition
            {
                LogicalName = name,
                SourceColumns = srcCols,
                SourceKind = (sourceKinds != null && sourceKinds.Count > i) ? sourceKinds[i] : "DateParts",
                TargetColumns = tgtCols,
                TargetKind = (targetKinds != null && targetKinds.Count > i) ? targetKinds[i] : "DateParts",
                Role = (roles != null && roles.Count > i && roles[i] == "Compare") ? "Compare" : "Key"
            });
        }

        return result;
    }

    // פיצול מחרוזת העמודות המופרדת בפסיקים לרשימה נקייה
    private static List<string> SplitCompositeColumns(List<string>? columnsCsv, int index)
    {
        if (columnsCsv == null || columnsCsv.Count <= index) return new List<string>();

        return (columnsCsv[index] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
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
        List<string> valueKinds,
        int maxRows,
        string filterActive,
        List<string> filterColumn,
        List<string> filterOperator,
        List<string> filterValue,
        List<string> compositeName,
        List<string> compositeSourceKind,
        List<string> compositeSourceColumns,
        List<string> compositeTargetKind,
        List<string> compositeTargetColumns,
        List<string> compositeRole)
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

        // ארבע רשימות המיפוי נקראות באינדקס מקביל (שדה מקור, שדה יעד, תפקיד,
        // טיפוס), ולכן אורך שונה ביניהן מפיל את ההשוואה על חריגת אינדקס -
        // שגיאת 500 גנרית במקום הודעה שאומרת מה לא תקין. הטופס תמיד שולח
        // אותן מיושרות, ולכן זו הגנה מפני בקשה שנשלחה שלא דרך המסך.
        if (sourceFields == null || sourceFields.Count == 0 ||
            targetFields == null || targetFields.Count != sourceFields.Count ||
            fieldRoles == null || fieldRoles.Count != sourceFields.Count ||
            (valueKinds != null && valueKinds.Count != 0 && valueKinds.Count != sourceFields.Count))
        {
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "DatabaseCompareError",
                "רשימות המיפוי שהתקבלו אינן באותו אורך", "Failed");
            TempData["ErrorMessage"] = "טבלת המיפוי שהתקבלה אינה שלמה. נא לחזור למסך המיפוי ולהגיש אותו מחדש.";
            return RedirectToAction("Index", "Home");
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // בניית השדות המחושבים שהוגדרו במסך המיפוי
            var compositeFields = BuildCompositeFields(
                compositeName, compositeSourceKind, compositeSourceColumns,
                compositeTargetKind, compositeTargetColumns, compositeRole);

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
                filterValue,
                valueKinds,
                compositeFields);

            stopwatch.Stop();

            // סימון מצב הבדיקה ושמות הספקים: הדוח משותף לשני המסלולים
            // ומתאים לפיהם את מחוון השלבים ואת הניסוח
            smartResultsViewModel.IsDatabaseComparison = true;
            smartResultsViewModel.SourceProviderName = ProviderDisplay.Name(sourceProvider);
            smartResultsViewModel.TargetProviderName = ProviderDisplay.Name(targetProvider);

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

            // החלפת כיוון ההשוואה משנה את משמעות הדוח (מה נחשב "חסר ביעד"
            // ומה "חסר במקור"), ולכן היא נרשמת בלוג ולא נשארת שקופה.
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "SwapFiles",
                $"החליף כיוון: המקור כעת {name2}, היעד {name1}");
        }

        return RedirectToAction("CompareFilesSchemaReview");
    }

    // פעולה (Action) המחזירה תצוגה מקדימה מסוננת של שני המסדים, כולל נתוני אבחון.
    // מוגדרת כ-POST בלבד: אימות טוקן ה-Antiforgery אינו תקף על בקשות GET.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewFilterData(
        string sourceTable,
        string targetTable,
        List<string> sourceFields,
        List<string> targetFields,
        List<string> fieldRoles,
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
                sourceFields, targetFields, fieldRoles,
                filterColumn, filterOperator, filterValue);

            // הגבלת חשיפה: טקסט השאילתות שנבנו מוחזר למנהלי מערכת מורשים בלבד.
            // שאר נתוני האבחון (ספירות שורות וטיפוסי עמודות) מוחזרים לכל משתמש מאומת.
            if (!IsUserAuthorized())
            {
                previewData.Remove("sqlQuery");
                previewData.Remove("oracleQuery");
            }

            // התצוגה המקדימה היא חלק משלב המיפוי, ולכן נרשמת כמוהו
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "PreviewData",
                $"תצוגה מקדימה: {sourceTable} מול {targetTable}");

            return Json(new { success = true, data = previewData });
        }
        catch (ArgumentException aex)
        {
            // שגיאות אימות (עמודה, אופרטור או ערך שאינם תקינים) נכתבות בעברית ומיועדות למשתמש
            _logger.LogWarning(aex, "PreviewFilterData validation failed");
            return Json(new { success = false, error = aex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building filtered preview data");
            CompareD.Models.SystemLogger.LogError(500, ex.Message, "/Compare/PreviewFilterData", User.Identity?.Name ?? "Unknown");
            // הודעה גנרית בלבד: פרטי שגיאת מסד הנתונים נשמרים בלוגים ואינם נחשפים בממשק
            return Json(new { success = false, error = "שגיאה בטעינת התצוגה המקדימה. נא לבדוק את תנאי הסינון ולנסות שוב." });
        }
    }

    // הפעולה הזו הייתה ללא הצהרת פועל ולכן נענתה לכל שיטת HTTP (POST/PUT/DELETE).
    // הגבלה ל-GET מונעת הפעלתה בשיטות שלא נועדה להן.
    [HttpGet]
    public IActionResult CompareFilesSchemaReview()
    {
        var path1 = HttpContext.Session.GetString("CsvSourceFilePath");
        var path2 = HttpContext.Session.GetString("CsvTargetFilePath");
        var name1 = HttpContext.Session.GetString("CsvSourceFileName");
        var name2 = HttpContext.Session.GetString("CsvTargetFileName");

        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2) || !System.IO.File.Exists(path1) || !System.IO.File.Exists(path2))
        {
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "SessionExpired",
                "הקבצים שהועלו לא נמצאו בעת טעינת מסך המיפוי", "Failed");
            TempData["ErrorMessage"] = "קובצי ההשוואה לא נמצאו. אנא העלו אותם מחדש.";
            return RedirectToAction("CompareFilesSetup");
        }

        try
        {
            var headers1 = CsvParser.GetHeaders(path1!);
            var headers2 = CsvParser.GetHeaders(path2!);

            if (headers1.Count == 0 || headers2.Count == 0)
            {
                AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "NoColumnsFound",
                    "אחד הקבצים אינו מכיל שורת כותרות או שורות נתונים", "Failed");
                TempData["ErrorMessage"] = "לא נמצאו עמודות או כותרות באחד הקבצים או בשניהם.";
                return RedirectToAction("CompareFilesSetup");
            }

            var model = new FilesSchemaReviewViewModel
            {
                SourceFileName = name1 ?? Path.GetFileName(path1!),
                TargetFileName = name2 ?? Path.GetFileName(path2!),
                SourceColumns = headers1,
                TargetColumns = headers2,
                // שורות מוסתרות בגיליון - מוצגות כאן כדי שההחלטה לגביהן
                // תתקבל לפני ההשוואה ולא אחריה
                SourceHiddenRows = CsvParser.CountHiddenRows(path1!),
                TargetHiddenRows = CsvParser.CountHiddenRows(path2!)
            };

            // זיהוי טיפוס ההשוואה לכל עמודת מקור מתוך מדגם ערכים בקובץ.
            // הצעה בלבד: היא נטענת כברירת מחדל בבורר ואפשר לעקוף אותה.
            var sourceSamples = CsvParser.GetColumnSamples(path1!);
            foreach (var column in headers1)
            {
                var values = sourceSamples.TryGetValue(column, out var found)
                    ? found
                    : new List<string>();
                model.SuggestedKinds[column] =
                    CompareD.Services.CompareService.DetectValueKindFromSamples(values);

                // אותו מדגם משמש גם לבדיקת ייחודיות המפתח במסך. נשלחות
                // עד 100 שורות בלבד: די כדי לזהות מפתח כפול, ובלי לנפח
                // את הדף בתוכן הקובץ.
                model.SourceSampleValues[column] = values.Count > 100
                    ? values.GetRange(0, 100)
                    : new List<string>(values);
            }

            model.SourceSampleRows = model.SourceSampleValues.Count == 0
                ? 0
                : model.SourceSampleValues.Values.Max(v => v.Count);

            // רישום השלב שהצליח, מאותה סיבה כמו במסלול המסד: בלי רשומה
            // על הצלחה, עובד שהגיע למסך המיפוי ונטש שם אינו קיים בלוג.
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FilesSchemaReview",
                $"טען עמודות מהקבצים: {model.SourceFileName} מול {model.TargetFileName}");

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
        List<string> compositeName,
        List<string> compositeSourceKind,
        List<string> compositeSourceColumns,
        List<string> compositeTargetKind,
        List<string> compositeTargetColumns,
        List<string> compositeRole,
        int maxRows = 10000,
        string filterActive = "false",
        List<string>? filterColumn = null,
        List<string>? filterOperator = null,
        List<string>? filterValue = null,
        // החרגת שורות שהוסתרו בגיליון, במקום למחוק אותן מהקובץ בכל הרצה
        string skipHiddenRows = "false")
    {
        var path1 = HttpContext.Session.GetString("CsvSourceFilePath");
        var path2 = HttpContext.Session.GetString("CsvTargetFilePath");
        var name1 = HttpContext.Session.GetString("CsvSourceFileName");
        var name2 = HttpContext.Session.GetString("CsvTargetFileName");

        if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2) || !System.IO.File.Exists(path1) || !System.IO.File.Exists(path2))
        {
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "SessionExpired",
                "הקבצים שהועלו אינם קיימים עוד בסשן בעת הרצת ההשוואה", "Failed");
            TempData["ErrorMessage"] = "פג תוקף הקבצים שהועלו. אנא העלו מחדש.";
            return RedirectToAction("CompareFilesSetup");
        }

        try
        {
            if (maxRows <= 0) maxRows = 10000;
            if (maxRows > 10000) maxRows = 10000;

            // תרגום שם עמודת הסינון לשם העמודה המקבילה בקובץ היעד, לפי המיפוי במסך.
            // בלי זה סינון לפי YOM היה מסונן בקובץ המקור בלבד.
            string TranslateToTarget(string sourceColumn)
            {
                string? mapped = Request.Form["targetMapping_" + sourceColumn];
                return string.IsNullOrEmpty(mapped) ? sourceColumn : mapped!;
            }

            bool applyFilter = string.Equals(filterActive, "true", StringComparison.OrdinalIgnoreCase)
                && filterColumn != null && filterColumn.Count > 0;

            // עמודות המפתח, כפי שסומנו במסך המיפוי. נדרשות כאן, לפני החיתוך,
            // כדי שהמיון יהיה לפי המפתח.
            var keyColumnsForOrdering = new List<string>();
            if (selectedSourceFields != null)
            {
                foreach (var field in selectedSourceFields)
                {
                    if (string.Equals(Request.Form["roleMapping_" + field], "Key", StringComparison.Ordinal))
                        keyColumnsForOrdering.Add(field);
                }
            }

            // סדר הפעולות זהה למסלול המסד: סינון, מיון לפי המפתח, ורק אז חיתוך.
            //
            // המיון הוא התיקון המשמעותי כאן. עד כה נלקחו N השורות הראשונות בסדר
            // הפיזי בקובץ, ומכיוון ששני הקבצים מסודרים אחרת, שני החלונות כיסו
            // טווחי מפתח שונים - וכל מפתח שנפל בחלון של קובץ אחד בלבד דווח כחוסר.
            // כך נוצר דיווח סימטרי של מאות "חוסרים" שכולם פסולת של החיתוך.
            // במסלול המסד זה לא קרה מפני שיש שם ORDER BY על שדות המפתח לפני TOP.
            // תקרת ביטחון לכמות השורות שנטענות לזיכרון.
            // הסינון והמיון מחייבים להחזיק את הקובץ כולו, בשונה מההזרמה שהייתה
            // כאן קודם. העלאה מוגבלת ל-50MB לקובץ, וקובץ כזה יכול להכיל מאות
            // אלפי שורות - ולכן בלי תקרה השרת עלול להיחנק על קובץ חריג.
            const int MaxRowsToMaterialize = 200_000;

            // שורות שהוסתרו בגיליון מוחרגות רק אם המשתמש ביקש זאת במסך המיפוי.
            // ברירת המחדל היא לכלול אותן: החרגה שקטה של נתונים הייתה עלולה
            // להסתיר חוסר אמיתי ולהפוך דוח שנכשל לדוח שעובר.
            bool excludeHidden = string.Equals(skipHiddenRows, "true", StringComparison.OrdinalIgnoreCase);

            var sourceRows = CsvParser.ParseFile(path1!, excludeHidden).Take(MaxRowsToMaterialize + 1).ToList();
            var targetRows = CsvParser.ParseFile(path2!, excludeHidden).Take(MaxRowsToMaterialize + 1).ToList();

            if (sourceRows.Count > MaxRowsToMaterialize || targetRows.Count > MaxRowsToMaterialize)
            {
                AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FileTooLarge",
                    $"קובץ בהיקף של יותר מ-{MaxRowsToMaterialize:N0} שורות", "Failed");
                TempData["ErrorMessage"] =
                    $"אחד הקבצים מכיל יותר מ-{MaxRowsToMaterialize:N0} שורות. " +
                    "בהיקף כזה לא ניתן למיין ולסנן את הקבצים בזיכרון, וההשוואה הייתה מדווחת חוסרים שאינם אמיתיים. " +
                    "יש לצמצם את טווח הנתונים בייצוא מהמסד לפני ההעלאה.";
                return RedirectToAction("CompareFilesSetup");
            }

            if (applyFilter)
            {
                // עמודת סינון שאינה קיימת בקובץ מוחזרת כהודעה למשתמש ולא
                // מסתיימת ב"אחד הקבצים ריק". ההודעה מצביעה על תנאי הסינון,
                // שהוא המקום שבו הבעיה נמצאת.
                try
                {
                    sourceRows = CompareService.ApplyRowFilter(sourceRows, filterColumn, filterOperator, filterValue);
                    targetRows = CompareService.ApplyRowFilter(targetRows, filterColumn, filterOperator, filterValue, TranslateToTarget);
                }
                catch (ArgumentException aex)
                {
                    AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "FilesCompareError",
                        $"תנאי סינון לא תקין: {aex.Message}", "Failed");
                    TempData["ErrorMessage"] = aex.Message;
                    return RedirectToAction("CompareFilesSchemaReview");
                }
            }

            // סך השורות לפני החיתוך - נדרש כדי לדווח במפורש שההשוואה נחתכה
            int sourceTotalRows = sourceRows.Count;
            int targetTotalRows = targetRows.Count;

            sourceRows = CompareService.OrderRowsByKey(sourceRows, keyColumnsForOrdering);
            targetRows = CompareService.OrderRowsByKey(targetRows,
                keyColumnsForOrdering.Select(TranslateToTarget).ToList());

            var sqlDataLimited = sourceRows.Take(maxRows).ToList();
            var oracleDataLimited = targetRows.Take(maxRows).ToList();

            if (sqlDataLimited.Count == 0 || oracleDataLimited.Count == 0)
            {
                AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "EmptyFile",
                    "אחד הקבצים אינו מכיל שורות נתונים", "Failed");
                TempData["ErrorMessage"] = "אחד הקבצים או שניהם התבררו כריקים בעת הרצת ההשוואה.";
                return RedirectToAction("CompareFilesSetup");
            }

            var sourceHeaders = CsvParser.GetHeaders(path1!);
            var targetHeaders = CsvParser.GetHeaders(path2!);
            var allowedRoles = new HashSet<string>(StringComparer.Ordinal) { "Key", "Compare" };

            // רשימת טיפוסי הנרמול המותרים לשדה בודד
            var allowedKinds = new HashSet<string>(StringComparer.Ordinal) { "Text", "Number", "Date" };

            // בניית רשימות המיפוי הסופיות
            var finalSourceFields = new List<string>();
            var finalTargetFields = new List<string>();
            var finalFieldRoles = new List<string>();
            var finalValueKinds = new List<string>();

            // עמודות שסומנו כמפתח אך לא מופו לעמודת יעד - נאספות כדי לתת הודעה מדויקת
            var unmappedKeyColumns = new List<string>();

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
                    string? kind = Request.Form["kindMapping_" + sf];

                    if (string.IsNullOrEmpty(tf) || string.IsNullOrEmpty(role))
                    {
                        // תיעוד המקרה שבו עמודה סומנה כמפתח אך נבחר לה "אל תשווה",
                        // כדי שההודעה למשתמש תסביר מדוע המפתח לא נכנס להשוואה
                        if (string.Equals(role, "Key", StringComparison.Ordinal))
                        {
                            unmappedKeyColumns.Add(sf);
                        }
                        continue;
                    }

                    if (!targetHeaders.Contains(tf, StringComparer.OrdinalIgnoreCase) || !allowedRoles.Contains(role))
                    {
                        continue;
                    }

                    finalSourceFields.Add(sf);
                    finalTargetFields.Add(tf);
                    finalFieldRoles.Add(role);
                    finalValueKinds.Add(!string.IsNullOrEmpty(kind) && allowedKinds.Contains(kind) ? kind : "Text");
                }
            }

            // בניית השדות המחושבים ואימות שכל העמודות המרכיבות קיימות בקבצים
            var compositeFields = BuildCompositeFields(
                compositeName, compositeSourceKind, compositeSourceColumns,
                compositeTargetKind, compositeTargetColumns, compositeRole);

            foreach (var composite in compositeFields)
            {
                if (composite.SourceColumns.Any(c => !sourceHeaders.Contains(c, StringComparer.OrdinalIgnoreCase)) ||
                    composite.TargetColumns.Any(c => !targetHeaders.Contains(c, StringComparer.OrdinalIgnoreCase)))
                {
                    TempData["ErrorMessage"] = $"השדה המחושב '{composite.LogicalName}' מפנה לעמודה שאינה קיימת באחד הקבצים.";
                    return RedirectToAction("CompareFilesSchemaReview");
                }
            }

            // וידוא שנבחר לפחות שדה מפתח אחד - בשורות המיפוי או בשדה מחושב
            bool hasKey = finalFieldRoles.Contains("Key") || compositeFields.Any(c => c.Role == "Key");
            if (!hasKey)
            {
                if (unmappedKeyColumns.Count > 0)
                {
                    // הודעה מדויקת במקום הודעה גנרית שמבלבלת אחרי שהמשתמש כן בחר מפתח
                    AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "UnmappedKeyColumn",
                        $"עמודות שסומנו כמפתח ללא עמודת יעד: {string.Join(", ", unmappedKeyColumns)}", "Failed");
                    TempData["ErrorMessage"] = $"העמודות {string.Join(", ", unmappedKeyColumns)} סומנו כמפתח אך לא מופו לעמודת יעד (\"אל תשווה\"). יש לבחור להן עמודת יעד או להגדיר מפתח אחר.";
                }
                else
                {
                    AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "NoKeySelected",
                        "לא סומן אף שדה מפתח במסך המיפוי", "Failed");
                    TempData["ErrorMessage"] = "חובה לבחור לפחות עמודת מפתח אחת (Key) לביצוע ההשוואה.";
                }
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
                fieldRoles: finalFieldRoles,
                valueKinds: finalValueKinds,
                compositeFields: compositeFields,
                maxRowsRequested: maxRows,
                sourceTotalRows: sourceTotalRows,
                targetTotalRows: targetTotalRows);

            sw.Stop();

            // שורות שהוסתרו בגיליון נכללו בהשוואה, כי הסתרה אינה מחיקה.
            // מדווח בדוח כדי שממצאי חוסר שנובעים מהן לא ייראו כפער נתונים.
            smartResultsViewModel.IsDatabaseComparison = false;
            smartResultsViewModel.SourceHiddenRows = CsvParser.CountHiddenRows(path1!);
            smartResultsViewModel.TargetHiddenRows = CsvParser.CountHiddenRows(path2!);
            smartResultsViewModel.HiddenRowsExcluded = excludeHidden;

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

        // רשימת סוגי התוכן המותרים להורדה (Whitelist סגור).
        // המפתח הוא ה-Content-Type והערך הוא סיומת הקובץ הנדרשת עבורו.
        private static readonly Dictionary<string, string> AllowedReportContentTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "text/csv", ".csv" },
                { "application/msword", ".doc" }
            };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadReport([FromForm] string content, [FromForm] string fileName, [FromForm] string contentType)
        {
            if (string.IsNullOrEmpty(content)) return BadRequest();

            // אימות סוג התוכן מול הרשימה המותרת: ללא אימות ניתן להחזיר תוכן שרירותי
            // עם Content-Type שנבחר על ידי המשתמש, מהדומיין של האפליקציה
            string baseContentType = (contentType ?? string.Empty).Split(';')[0].Trim();
            if (!AllowedReportContentTypes.TryGetValue(baseContentType, out var requiredExtension))
            {
                return BadRequest();
            }

            // ניקוי שם הקובץ מתווי נתיב למניעת כתיבה או חשיפה מחוץ להורדה המיועדת
            string safeFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                !safeFileName.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest();
            }

            // הורדת הדוח היא השלב האחרון בתהליך, ולכן היא נרשמת: בלעדיה
            // אי אפשר לדעת אם העובד סיים את הבדיקה או רק ראה אותה על המסך.
            AuditLogger.LogAction(User.Identity?.Name ?? "Unknown", "DownloadReport",
                $"הוריד דוח: {safeFileName}");

            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var fullBytes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(bom, bytes));
            return File(fullBytes, baseContentType, safeFileName);
        }
    }
