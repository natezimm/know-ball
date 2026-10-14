using System.Text;

namespace SportsTrivia.Infrastructure.Sources;

/// <summary>A small RFC 4180 reader: quoted fields may hold commas, quotes ("") and line breaks.</summary>
internal static class CsvReader
{
    /// <summary>Reads a file with a header row into one dictionary-like row per record.</summary>
    public static async Task<List<CsvRow>> ReadAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);
        return Parse(text);
    }

    public static List<CsvRow> Parse(string text)
    {
        var records = SplitRecords(text);
        if (records.Count == 0) return [];

        var header = records[0];
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++) columns.TryAdd(header[i].Trim().TrimStart('﻿'), i);

        var rows = new List<CsvRow>(records.Count - 1);
        for (var i = 1; i < records.Count; i++)
        {
            // A blank line parses as one empty field.
            if (records[i].Count == 1 && records[i][0].Length == 0) continue;
            rows.Add(new CsvRow(columns, records[i]));
        }
        return rows;
    }

    private static List<List<string>> SplitRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }
}

/// <summary>One record, read by column name. Blank cells and the literal "NA" are null.</summary>
internal sealed class CsvRow(Dictionary<string, int> columns, List<string> cells)
{
    public string? Str(string column)
    {
        if (!columns.TryGetValue(column, out var i) || i >= cells.Count) return null;
        var value = cells[i].Trim();
        return value.Length == 0 || value == "NA" ? null : value;
    }

    public int? Int(string column) =>
        int.TryParse(Str(column), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    public double? Dbl(string column) =>
        double.TryParse(Str(column), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    public bool Flag(string column) => string.Equals(Str(column), "TRUE", StringComparison.OrdinalIgnoreCase);
}
