using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Undo records only this transient object; CSV and generated assets never participate
// in draft undo/redo. Serialized text also survives an editor domain reload.
public sealed class FactoryContentDraft : ScriptableObject
{
    public const string TableDirectory = "Assets/Data/FactoryTables";

    [Serializable]
    public sealed class Table
    {
        public string path;
        public string original;
        public string text;
    }

    [SerializeField] private List<Table> tables = new List<Table>();
    public IReadOnlyList<Table> Tables => tables;
    public bool IsModified => tables.Any(t => t.text != t.original);

    public void Load()
    {
        var loaded = new List<Table>();
        foreach (string path in Directory.GetFiles(TableDirectory, "*.csv", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            FactoryCsvTable.Parse(text);
            loaded.Add(new Table { path = path.Replace('\\', '/'), original = text, text = text });
        }
        tables = loaded;
        Undo.ClearUndo(this);
    }

    public void SetCell(int tableIndex, int row, string column, string value)
    {
        var table = FactoryCsvTable.Parse(tables[tableIndex].text);
        if (table.Get(row, column) == value) return;
        table.Set(row, column, value);
        Undo.RegisterCompleteObjectUndo(this, "编辑 Factory 内容");
        tables[tableIndex].text = table.Text;
    }

    public FactoryDatabaseAsset ReadCandidate()
    {
        var documents = tables.ToDictionary(t => t.path, t => t.text, StringComparer.Ordinal);
        return FactoryDatabaseCsvImporter.ReadCandidate(path => FactoryCsvTable.Parse(documents[path]));
    }

    public void Save()
    {
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        if (!IsModified) return;
        // Check every source: a changed dependency must not silently produce a stale database.
        foreach (Table table in tables)
            if (File.ReadAllText(table.path, Encoding.UTF8) != table.original)
                throw new IOException($"源表已被其他操作修改，请放弃草稿并重新读取：{table.path}");

        FactoryDatabaseAsset candidate = ReadCandidate();
        try
        {
            var changed = tables.Where(t => t.text != t.original)
                .ToDictionary(t => t.path, t => t.text, StringComparer.Ordinal);
            // Import callbacks cannot observe half-written tables. Generation happens once,
            // from the fully validated in-memory candidate, before releasing imports.
            AssetDatabase.StartAssetEditing();
            try
            {
                FactoryCsvSaveTransaction.Commit(changed, () => FactoryDatabaseCsvImporter.Generate(candidate));
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (Table table in tables) table.original = table.text;
            Undo.ClearUndo(this);
        }
        finally { DestroyImmediate(candidate); }
    }
}

public static class FactoryCsvSaveTransaction
{
    // Backups live only for this save. Preserve bytes, encoding and .meta files on rollback.
    public static void Commit(IReadOnlyDictionary<string, string> changes, Action generate)
    {
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in changes.Keys)
        {
            try { originals.Add(path, File.ReadAllBytes(path)); }
            catch (Exception e) { throw new IOException($"无法读取待保存的 CSV：{path}", e); }
        }
        var attempted = new List<string>();
        string current = "生成数据库";
        try
        {
            foreach (var change in changes)
            {
                current = change.Key;
                attempted.Add(current);
                File.WriteAllText(current, change.Value, new UTF8Encoding(false));
            }
            current = "生成数据库";
            generate();
        }
        catch (Exception e)
        {
            var errors = new List<string>();
            foreach (string path in attempted)
            {
                try
                {
                    // A failed write can leave the original file intact (e.g. a locked file).
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(originals[path]))
                        File.WriteAllBytes(path, originals[path]);
                }
                catch (Exception restore) { errors.Add(path + ": " + restore.Message); }
            }
            throw new IOException($"Factory 保存失败：{current}。" +
                (errors.Count == 0 ? "本次涉及的 CSV 已恢复。" : "恢复失败：" + string.Join("; ", errors)), e);
        }
    }
}
