using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DanmuCinema
{
    public enum LibrarySort { Name, Modified, Size, Type, Bitrate }
    public enum DanmuMatchScope { Single, Season, Selection }

    public sealed class LibraryEntry
    {
        public Dictionary<string, object> Item;
        public string Key, Name, Type, SourceLabel;
        public long? Size;
        public DateTime? ModifiedUtc;
        public double Bitrate;
        public bool HasXml;
        public static string Identity(Dictionary<string, object> item)
        {
            string id = Json.Text(item, "Id");
            return id != "" ? "id:" + id : "path:" + Json.Text(item, "Path");
        }
        public static LibraryEntry Read(Dictionary<string, object> item, IDictionary<string, DanmuAssociation> associations)
        {
            var entry = new LibraryEntry { Item = item, Key = Identity(item), Type = Json.Text(item, "Type"), SourceLabel = "" };
            string path = Json.Text(item, "Path");
            entry.Name = String.IsNullOrWhiteSpace(path) ? Json.Text(item, "Name") : Path.GetFileName(path);
            var source = Json.Array(item, "MediaSources").OfType<Dictionary<string, object>>().FirstOrDefault();
            long size; double bitrate;
            if (Int64.TryParse(Json.Text(source, "Size"), out size) && size >= 0) entry.Size = size;
            if (Double.TryParse(Json.Text(source, "Bitrate"), NumberStyles.Float, CultureInfo.InvariantCulture, out bitrate)) entry.Bitrate = bitrate;
            DateTime date;
            if (DateTime.TryParse(Json.Text(item, "DateLastSaved"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)) entry.ModifiedUtc = date.ToUniversalTime();
            try
            {
                if (!String.IsNullOrWhiteSpace(path))
                {
                    var file = new FileInfo(path);
                    if (file.Exists) { entry.Size = file.Length; entry.ModifiedUtc = file.LastWriteTimeUtc; }
                    var xml = new FileInfo(Path.ChangeExtension(path, ".xml"));
                    entry.HasXml = xml.Exists;
                    DanmuAssociation association;
                    if (entry.HasXml && associations.TryGetValue(DanmuAssociations.Key(path), out association) && association.XmlLength == xml.Length && association.XmlWriteTicks == xml.LastWriteTimeUtc.Ticks)
                        entry.SourceLabel = association.Source;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            return entry;
        }
        public string DanmuLabel { get { return !HasXml ? "未匹配 · 选择 ▾" : SourceLabel == "" ? "已有 XML · 选择 ▾" : SourceLabel + " ▾"; } }
    }
    public sealed class LibrarySelection
    {
        readonly HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int Count { get { return keys.Count; } }
        public string[] Keys { get { return keys.ToArray(); } }
        public bool Contains(string key) { return keys.Contains(key); }
        public void Set(string key, bool selected) { if (selected) keys.Add(key); else keys.Remove(key); }
        public void Clear() { keys.Clear(); }
        public void Retain(IEnumerable<string> existing) { keys.IntersectWith(existing); }
        public void ReplaceVisible(IEnumerable<string> visible, IEnumerable<string> selected)
        {
            keys.ExceptWith(visible); keys.UnionWith(selected);
        }
        public static IEnumerable<string> DragRange(IList<string> ordered, int anchor, int end, IEnumerable<string> previous, bool additive)
        {
            var selected = new HashSet<string>(additive ? previous : new string[0], StringComparer.OrdinalIgnoreCase);
            if (ordered.Count > 0)
            {
                int first = Math.Max(0, Math.Min(anchor, end)), last = Math.Min(ordered.Count - 1, Math.Max(anchor, end));
                for (int index = first; index <= last; index++) selected.Add(ordered[index]);
            }
            return selected;
        }
    }
    public sealed class MediaLibrary
    {
        public readonly LibrarySelection Selection = new LibrarySelection();
        public LibraryEntry[] Entries { get; private set; }
        public MediaLibrary() { Entries = new LibraryEntry[0]; }
        public void Replace(IEnumerable<Dictionary<string, object>> items)
        { ReplaceEntries(Build(items)); }
        public static LibraryEntry[] Build(IEnumerable<Dictionary<string, object>> items)
        {
            var associations = DanmuAssociations.Read();
            return items.GroupBy(LibraryEntry.Identity, StringComparer.OrdinalIgnoreCase).Select(x => LibraryEntry.Read(x.First(), associations)).ToArray();
        }
        public void ReplaceEntries(LibraryEntry[] entries)
        {
            Entries = entries;
            Selection.Retain(Entries.Select(x => x.Key));
        }
        public LibraryEntry[] View(string filter, LibrarySort sort, bool descending)
        {
            var entries = Entries.Where(x => MediaNames.Matches(x.Item, filter));
            // Typed sorting keeps 10 GB above 2 GB and missing metadata at the end.
            Func<LibraryEntry, IComparable> value = x => sort == LibrarySort.Modified ? (IComparable)x.ModifiedUtc : sort == LibrarySort.Size ? (IComparable)x.Size : sort == LibrarySort.Bitrate ? (IComparable)x.Bitrate : sort == LibrarySort.Type ? x.Type : x.Name;
            var comparer = Comparer<IComparable>.Create((a, b) => a is string && b is string ? StringComparer.CurrentCultureIgnoreCase.Compare((string)a, (string)b) : a.CompareTo(b));
            var known = entries.Where(x => value(x) != null);
            var ordered = descending ? known.OrderByDescending(value, comparer) : known.OrderBy(value, comparer);
            return ordered.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Concat(entries.Where(x => value(x) == null).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        }
        public Dictionary<string, object>[] SelectedItems { get { return Entries.Where(x => Selection.Contains(x.Key)).Select(x => x.Item).ToArray(); } }
        public static void RequireSameSeason(IEnumerable<Dictionary<string, object>> items)
        {
            var selected = items.ToArray();
            if (selected.Length == 0) throw new InvalidOperationException("请先勾选影片。");
            if (selected.Any(x => Json.Text(x, "Type") == "Movie") || selected.Skip(1).Any(x => !SmartMatching.SameSeason(selected[0], x)))
                throw new InvalidOperationException("批量选择同一弹幕来源时，请只勾选同一番剧、同一季度的文件；电影请单独匹配。");
        }
    }
    public sealed class DanmuAssociation
    {
        public string VideoPath { get; set; }
        public string Source { get; set; }
        public string Provider { get; set; }
        public string Anime { get; set; }
        public string Episode { get; set; }
        public string CommentId { get; set; }
        public long XmlWriteTicks { get; set; }
        public long XmlLength { get; set; }
    }
    public static class DanmuAssociations
    {
        static readonly object sync = new object();
        static string FilePath { get { return Path.Combine(Paths.Data, "danmu-associations.json"); } }
        public static string Key(string path) { return Path.GetFullPath(path); }
        public static Dictionary<string, DanmuAssociation> Read()
        {
            lock (sync)
            {
                var result = new Dictionary<string, DanmuAssociation>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(FilePath)) foreach (var record in Json.Read<DanmuAssociation[]>(File.ReadAllText(FilePath)))
                        if (!String.IsNullOrWhiteSpace(record.VideoPath)) result[Key(record.VideoPath)] = record;
                }
                catch { Log.Write("弹幕来源记录无法读取，已有 XML 仍可正常使用。"); }
                return result;
            }
        }
        public static void Save(string path, DanmuAssociation association)
        {
            lock (sync)
            {
                var xml = new FileInfo(Path.ChangeExtension(path, ".xml"));
                if (!xml.Exists) throw new FileNotFoundException("弹幕 XML 尚未保存。");
                association.VideoPath = Key(path); association.XmlLength = xml.Length; association.XmlWriteTicks = xml.LastWriteTimeUtc.Ticks;
                var records = Read(); records[association.VideoPath] = association;
                Directory.CreateDirectory(Paths.Data); SettingsStore.AtomicWrite(FilePath, Json.Write(records.Values.ToArray()));
            }
        }
    }
}
