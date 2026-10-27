using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DanmuCinema
{
    public static class SearchHistory
    {
        static readonly object sync = new object();
        static string FilePath { get { return Path.Combine(Paths.Data, "search-history.json"); } }
        static Dictionary<string, string[]> Read()
        {
            try { return File.Exists(FilePath) ? Json.Read<Dictionary<string, string[]>>(File.ReadAllText(FilePath)) ?? new Dictionary<string, string[]>() : new Dictionary<string, string[]>(); }
            catch { Log.Write("搜索历史无法读取。"); return new Dictionary<string, string[]>(); }
        }
        public static string[] List(string scope)
        {
            lock (sync) { string[] rows; return Read().TryGetValue(scope, out rows) ? (rows ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)).ToArray() : new string[0]; }
        }
        static void Change(string scope, Func<string[], string[]> change)
        {
            lock (sync)
            {
                var all = Read(); string[] rows; all.TryGetValue(scope, out rows);
                all[scope] = change(rows ?? new string[0]);
                Directory.CreateDirectory(Paths.Data); SettingsStore.AtomicWrite(FilePath, Json.Write(all), false);
            }
        }
        public static void Add(string scope, string text)
        {
            text = (text ?? "").Trim(); if (text.Length == 0 || text.Length > 200) return;
            string term = text;
            Change(scope, rows => new[] { term }.Concat(rows.Where(x => !String.Equals(x, term, StringComparison.OrdinalIgnoreCase))).Take(100).ToArray());
        }
        public static void Remove(string scope, string text) { Change(scope, rows => rows.Where(x => !String.Equals(x, text, StringComparison.OrdinalIgnoreCase)).ToArray()); }
        public static void Clear(string scope) { Change(scope, rows => new string[0]); }
    }

}
