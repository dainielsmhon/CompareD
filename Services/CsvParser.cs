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

    // שליפת מדגם ערכים לכל עמודה, לצורך זיהוי טיפוס ההשוואה של הקובץ.
    // נקרא מספר שורות מוגבל בלבד: הזיהוי הוא הצעת ברירת מחדל, ולא שווה
    // לקרוא קובץ של חצי מיליון שורות רק כדי לבחור ערך בתיבת בחירה.
    public static Dictionary<string, List<string>> GetColumnSamples(string filePath, int maxSampleRows = 200)
    {
        var samples = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        int rowCount = 0;
        foreach (var row in ParseFile(filePath))
        {
            foreach (var pair in row)
            {
                if (!samples.TryGetValue(pair.Key, out var values))
                {
                    values = new List<string>();
                    samples[pair.Key] = values;
                }
                values.Add(pair.Value?.ToString() ?? string.Empty);
            }

            rowCount++;
            if (rowCount >= maxSampleRows) break;
        }

        return samples;
    }

    // מספרי השורות המוסתרות בגיליון הראשון, ומספר השורה הראשונה בגיליון.
    //
    // הסתרת שורות בגיליון - ידנית או דרך סינון אוטומטי - אינה מוחקת אותן.
    // הן נשארות בקובץ עם התכונה hidden="1", וכל קורא xlsx מחזיר אותן ככל
    // שורה אחרת. בלי המידע הזה, קובץ שהוסתר בו יום שלם נראה כאילו הוא מכיל
    // אותו, והשוואה מולו מייצרת עשרות "שורות חסרות" מדומות.
    //
    // הקריאה היא ישירות מ-XML של הגיליון, כי MiniExcel אינו חושף את מצב
    // הנראות של השורה. נכשלת בשקט ומחזירה רשימה ריקה - זו הערת אזהרה
    // ואפשרות נוחות, לא נתון שנכונות ההשוואה תלויה בו.
    public static (HashSet<int> Hidden, int FirstRow) GetHiddenRowInfo(string filePath)
    {
        var hidden = new HashSet<int>();
        int firstRow = 1;

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return (hidden, firstRow);

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);

            // הגיליון הראשון בלבד - הוא זה שנקרא בהשוואה עצמה
            var sheet = archive.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (sheet == null) return (hidden, firstRow);

            bool firstSeen = false;
            using var entryStream = sheet.Open();
            using var reader = System.Xml.XmlReader.Create(entryStream,
                new System.Xml.XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true });

            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;
                if (!string.Equals(reader.Name, "row", StringComparison.Ordinal)) continue;

                if (!int.TryParse(reader.GetAttribute("r"), out int rowNumber)) continue;

                if (!firstSeen)
                {
                    firstRow = rowNumber;
                    firstSeen = true;
                }

                string? flag = reader.GetAttribute("hidden");
                if (flag == "1" || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
                    hidden.Add(rowNumber);
            }
        }
        catch
        {
            // קובץ פגום או מבנה לא צפוי - אין אבחון, ההשוואה נמשכת כרגיל
        }

        return (hidden, firstRow);
    }

    // ספירת השורות המוסתרות בגיליון, למעט שורת הכותרת
    public static int CountHiddenRows(string filePath)
    {
        var (hidden, firstRow) = GetHiddenRowInfo(filePath);
        return hidden.Count(r => r > firstRow);
    }

    // הזרמת קובץ (CSV או XLSX) שורה-אחר-שורה בהתאם לסוג הקובץ.
    //
    // skipHiddenRows מחריג שורות שהוסתרו בגיליון. MiniExcel שומר על מיקום
    // השורות ומחזיר שורה ריקה גם עבור שורה שאינה קיימת ב-XML, ולכן המיקום
    // בזרם מתורגם למספר שורה בגיליון באופן מדויק: שורת הכותרת היא השורה
    // הראשונה בגיליון, והשורה ה-i בזרם היא firstRow + 1 + i.
    public static IEnumerable<Dictionary<string, object>> ParseFile(string filePath, bool skipHiddenRows = false)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".xlsx")
        {
            var hiddenRows = new HashSet<int>();
            int firstRow = 1;
            if (skipHiddenRows)
            {
                (hiddenRows, firstRow) = GetHiddenRowInfo(filePath);
            }

            // פתיחת ה-stream בתוך yield return תסגור את הקובץ אוטומטית ברגע שהצרכן יפסיק להריץ לולאה על ה-IEnumerable
            using (var stream = File.OpenRead(filePath))
            {
                int index = 0;
                foreach (var row in ParseXlsx(stream))
                {
                    int sheetRow = firstRow + 1 + index;
                    index++;

                    if (hiddenRows.Count > 0 && hiddenRows.Contains(sheetRow)) continue;

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
