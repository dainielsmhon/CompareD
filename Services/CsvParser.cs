using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MiniExcelLibs;

namespace CompareD.Services;

public static class CsvParser
{
    public static List<Dictionary<string, object>> Parse(Stream stream)
    {
        var result = new List<Dictionary<string, object>>();
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine)) return result;

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
                result.Add(row);
            }
        }
        return result;
    }

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

    public static List<Dictionary<string, object>> ParseXlsx(Stream stream)
    {
        var result = new List<Dictionary<string, object>>();
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
                result.Add(rowDict);
            }
        }
        return result;
    }

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
            using (var stream = File.OpenRead(filePath))
            {
                var result = Parse(stream);
                if (result.Count > 0)
                {
                    return result[0].Keys.ToList();
                }
            }
        }
        return new List<string>();
    }

    public static List<Dictionary<string, object>> ParseFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".xlsx")
        {
            using (var stream = File.OpenRead(filePath))
            {
                return ParseXlsx(stream);
            }
        }
        else
        {
            using (var stream = File.OpenRead(filePath))
            {
                return Parse(stream);
            }
        }
    }
}
