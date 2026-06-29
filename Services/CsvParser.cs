using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MiniExcelLibs;

namespace CompareD.Services;

public static class CsvParser
{
    // הזרמת נתונים (Streaming) שורה-אחר-שורה מקובץ CSV במקום טעינת כל הקובץ ל-RAM בבת אחת
    public static IEnumerable<Dictionary<string, object>> Parse(Stream stream)
    {
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine)) yield break;

            var headers = ParseLine(headerLine);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var values = ParseLine(line);
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                {
                    var header = headers[i];
                    var val = i < values.Count ? values[i] : "";
                    row[header] = val;
                }
                yield return row;
            }
        }
    }

    // פירוק שורה לקולונות תוך טיפול בגרשיים ופסיקים
    private static List<string> ParseLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim().Trim('"'));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim().Trim('"'));
        return result;
    }

    // הזרמת נתונים שורה-אחר-שורה מקובץ Excel XLSX באמצעות MiniExcel.Query שמבצעת Streaming
    public static IEnumerable<Dictionary<string, object>> ParseXlsx(Stream stream)
    {
        var rows = MiniExcel.Query(stream, useHeaderRow: true);
        foreach (var row in rows)
        {
            var dict = row as IDictionary<string, object>;
            if (dict != null)
            {
                var rowDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in dict)
                {
                    rowDict[kvp.Key] = kvp.Value ?? "";
                }
                yield return rowDict;
            }
        }
    }

    // קריאת כותרות (Headers) בגישת O(1) Memory ללא טעינת הקובץ כולו לזיכרון
    public static List<string> GetHeaders(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".xlsx")
        {
            using (var stream = File.OpenRead(filePath))
            {
                var firstRow = MiniExcel.Query(stream, useHeaderRow: true).FirstOrDefault() as IDictionary<string, object>;
                if (firstRow != null)
                {
                    return firstRow.Keys.ToList();
                }
            }
        }
        else
        {
            // אופטימיזציית ביצועים: קריאת השורה הראשונה בלבד מקובץ ה-CSV מבלי לקרוא את שאר השורות לזיכרון
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                var headerLine = reader.ReadLine();
                if (!string.IsNullOrEmpty(headerLine))
                {
                    return ParseLine(headerLine);
                }
            }
        }
        return new List<string>();
    }

    // הזרמת קובץ (CSV או XLSX) שורה-אחר-שורה בהתאם לסוג הקובץ
    public static IEnumerable<Dictionary<string, object>> ParseFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".xlsx")
        {
            // פתיחת ה-stream בתוך yield return תסגור את הקובץ אוטומטית ברגע שהצרכן יפסיק להריץ לולאה על ה-IEnumerable
            using (var stream = File.OpenRead(filePath))
            {
                foreach (var row in ParseXlsx(stream))
                {
                    yield return row;
                }
            }
        }
        else
        {
            using (var stream = File.OpenRead(filePath))
            {
                foreach (var row in Parse(stream))
                {
                    yield return row;
                }
            }
        }
    }
}
