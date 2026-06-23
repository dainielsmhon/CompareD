using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
}
