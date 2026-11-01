using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// A small CSV document for the existing Factory tables. Untouched records (including
// comments, extra columns and other locales) retain their exact source text.
public sealed class FactoryCsvTable
{
    private readonly List<string> records = new List<string>();
    private readonly List<int> rowRecords = new List<int>();
    private readonly List<string[]> rows = new List<string[]>();
    private readonly Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.Ordinal);
    private int headerRecord;
    public string[] Header { get; private set; }
    public int RowCount => rows.Count;
    public string Text => string.Concat(records);

    public static FactoryCsvTable Read(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static FactoryCsvTable Parse(string text)
    {
        var table = new FactoryCsvTable();
        int start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        if (start == 1) table.records.Add("\uFEFF");
        while (start < text.Length)
        {
            int end = start;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
            string firstLine = text.Substring(start, end - start);
            bool ignored = string.IsNullOrWhiteSpace(firstLine) || firstLine.TrimStart().StartsWith("#", StringComparison.Ordinal);
            if (!ignored)
            {
                bool quoted = false;
                end = start;
                for (; end < text.Length; end++)
                {
                    if (text[end] == '"')
                    {
                        if (quoted && end + 1 < text.Length && text[end + 1] == '"') end++;
                        else quoted = !quoted;
                    }
                    else if (!quoted && (text[end] == '\r' || text[end] == '\n')) break;
                }
                if (quoted) throw new InvalidDataException("Unclosed CSV quote.");
            }
            int contentEnd = end;
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            int record = table.records.Count;
            table.records.Add(text.Substring(start, end - start));
            if (!ignored)
            {
                string[] fields = ParseRecord(text.Substring(start, contentEnd - start));
                if (table.Header == null)
                {
                    table.headerRecord = record;
                    table.Header = fields;
                    for (int i = 0; i < fields.Length; i++)
                        if (string.IsNullOrWhiteSpace(fields[i]) || !table.columns.TryAdd(fields[i].Trim(), i))
                            throw new InvalidDataException($"Invalid or duplicate CSV column '{fields[i]}'.");
                }
                else
                {
                    table.rowRecords.Add(record);
                    table.rows.Add(fields);
                }
            }
            start = end;
        }
        if (table.Header == null) throw new InvalidDataException("CSV table is empty.");
        return table;
    }

    public string Get(int row, string column)
    {
        if (!TryGet(row, column, out string value))
            throw new InvalidDataException($"CSV row {row + 2} is missing column '{column}'.");
        return value;
    }

    public bool TryGet(int row, string column, out string value)
    {
        if (!columns.TryGetValue(column, out int index) || index >= rows[row].Length)
        {
            value = string.Empty;
            return false;
        }
        value = rows[row][index];
        return true;
    }

    public void Set(int row, string column, string value)
    {
        if (!columns.TryGetValue(column, out int index)) throw new InvalidDataException($"Missing CSV column '{column}'.");
        string[] fields = rows[row];
        if (index >= fields.Length) Array.Resize(ref fields, Header.Length);
        fields[index] = value ?? string.Empty;
        rows[row] = fields;
        int record = rowRecords[row];
        string old = records[record];
        string ending = old.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" :
            old.EndsWith("\n", StringComparison.Ordinal) ? "\n" : old.EndsWith("\r", StringComparison.Ordinal) ? "\r" : "";
        records[record] = string.Join(",", fields.Select(Escape)) + ending;
    }

    public void EnsureColumn(string column)
    {
        if (columns.ContainsKey(column)) return;
        columns.Add(column, Header.Length);
        Header = Header.Concat(new[] { column }).ToArray();
        string ending = Ending(records[headerRecord]);
        records[headerRecord] = string.Join(",", Header.Select(Escape)) + ending;
    }

    public int Append(IReadOnlyDictionary<string, string> values)
    {
        foreach (string column in values.Keys) EnsureColumn(column);
        string newline = Text.Contains("\r\n") ? "\r\n" : "\n";
        if (records.Count > 0 && Ending(records[records.Count - 1]) == "") records[records.Count - 1] += newline;
        string[] fields = Header.Select(c => values.TryGetValue(c.Trim(), out string value) ? value : "").ToArray();
        rowRecords.Add(records.Count);
        rows.Add(fields);
        records.Add(string.Join(",", fields.Select(Escape)) + newline);
        return rows.Count - 1;
    }

    public void RemoveAt(int row)
    {
        records[rowRecords[row]] = "";
        rowRecords.RemoveAt(row);
        rows.RemoveAt(row);
    }

    public void SwapRows(int first, int second)
    {
        // Keep comments, unrelated recipe rows and line endings at their positions.
        int a = rowRecords[first], b = rowRecords[second];
        string aEnding = Ending(records[a]), bEnding = Ending(records[b]);
        string aContent = records[a].Substring(0, records[a].Length - aEnding.Length);
        string bContent = records[b].Substring(0, records[b].Length - bEnding.Length);
        records[a] = bContent + aEnding;
        records[b] = aContent + bEnding;
        (rows[first], rows[second]) = (rows[second], rows[first]);
    }

    private static string Ending(string record) => record.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" :
        record.EndsWith("\n", StringComparison.Ordinal) ? "\n" : record.EndsWith("\r", StringComparison.Ordinal) ? "\r" : "";

    public static string Escape(string value)
    {
        value = value ?? string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value.TrimStart().StartsWith("#", StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static string[] ParseRecord(string record)
    {
        var fields = new List<string>();
        int i = 0;
        while (true)
        {
            var field = new StringBuilder();
            if (i < record.Length && record[i] == '"')
            {
                i++;
                bool closed = false;
                while (i < record.Length)
                {
                    char c = record[i++];
                    if (c != '"') { field.Append(c); continue; }
                    if (i < record.Length && record[i] == '"') { field.Append('"'); i++; }
                    else { closed = true; break; }
                }
                if (!closed || i < record.Length && record[i] != ',')
                    throw new InvalidDataException("Invalid CSV quoted field.");
            }
            else
            {
                while (i < record.Length && record[i] != ',')
                {
                    if (record[i] == '"') throw new InvalidDataException("Unexpected CSV quote.");
                    field.Append(record[i++]);
                }
            }
            fields.Add(field.ToString());
            if (i == record.Length) break;
            i++; // comma; the next iteration also handles a trailing empty field
        }
        return fields.ToArray();
    }
}
