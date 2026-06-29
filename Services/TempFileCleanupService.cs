using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CompareD.Models;

namespace CompareD.Services
{
    // שירות רקע לניקוי יזום של קבצים זמניים יתומים מתיקיית temp_uploads
    public class TempFileCleanupService : BackgroundService
    {
        private readonly ILogger<TempFileCleanupService> _logger;
        private readonly string _tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "temp_uploads");

        public TempFileCleanupService(ILogger<TempFileCleanupService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Temp File Cleanup Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    CleanupFiles();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during temp file cleanup.");
                }

                // הרצה אחת לכל 15 דקות
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }

        private void CleanupFiles()
        {
            if (!Directory.Exists(_tempFolder)) return;

            var now = DateTime.Now;
            var directoryInfo = new DirectoryInfo(_tempFolder);
            var files = directoryInfo.GetFiles();

            int deletedCount = 0;
            foreach (var file in files)
            {
                // מחיקת קבצים שגילם עולה על 30 דקות
                if (now - file.LastWriteTime > TimeSpan.FromMinutes(30))
                {
                    try
                    {
                        var filePath = file.FullName;
                        file.Delete();
                        deletedCount++;
                        
                        // תיעוד המחיקה ב-Audit Log המערכתי
                        AuditLogger.LogAction("SYSTEM", "TempFileCleanup", $"Deleted orphan temporary file: {Path.GetFileName(filePath)}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete temporary file: {FilePath}", file.FullName);
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {DeletedCount} orphan temporary files from {TempFolder}.", deletedCount, _tempFolder);
            }
        }
    }
}
