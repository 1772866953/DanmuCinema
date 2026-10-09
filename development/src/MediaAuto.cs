using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DanmuCinema
{
    // Shared by the desktop and the server resolver; never writes to media.
    public static class MediaAuto
    {
        public static readonly string[] Modes = { "mixed", "movies", "tvshows" };
        public static readonly string[] Labels = { "自动识别 · 电影 / 电视剧 / 动漫", "电影", "电视剧 / 动漫" };
        public static bool Valid(string mode) { return Modes.Contains(mode); }
        public static string Label(string mode) { int i = Array.IndexOf(Modes, String.IsNullOrEmpty(mode) ? "mixed" : mode); return i < 0 ? mode : Labels[i]; }
        public static bool Video(string path) { return Regex.IsMatch(Path.GetExtension(path) ?? "", @"^\.(?:mkv|mp4|avi|mov|m4v|ts|m2ts|wmv|webm)$", RegexOptions.IgnoreCase); }
        static bool Extra(string path) { return Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"\b(?:SP\d*|OVA|OAD|NCOP|NCED|PV|Trailer)\b|特别篇|特別篇|预告", RegexOptions.IgnoreCase); }
        public static int Episode(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? "";
            if (Extra(path)) return 0;
            var brackets = Regex.Matches(name, @"\[(\d{1,3})\]");
            if (brackets.Count == 1) return Int32.Parse(brackets[0].Groups[1].Value);
            var match = Regex.Match(name, @"S\d{1,2}E(\d{1,3})(?!\d)|(?:^|[\s._-])EP?\s*(\d{1,3})(?!\d)|第\s*(\d{1,3})\s*[集话話]|\s-\s(\d{1,3})(?=\s|$|\[)|[（(](\d{1,3})[)）]\s*$", RegexOptions.IgnoreCase);
            foreach (Group group in match.Groups.Cast<Group>().Skip(1)) if (group.Success) return Int32.Parse(group.Value);
            return 0;
        }
        public static int Season(string path)
        {
            var match = Regex.Match(path ?? "", @"(?:\bSeason\s*|(?<![a-z])S)(\d{1,2})(?!\d)|第\s*([一二三四五六七八九十\d]+)\s*[季期]", RegexOptions.IgnoreCase);
            int n; if (match.Groups[1].Success) return Int32.Parse(match.Groups[1].Value);
            string text = match.Groups[2].Value;
            if (Int32.TryParse(text, out n)) return n;
            if (text.Length == 1) return "一二三四五六七八九十".IndexOf(text, StringComparison.Ordinal) + 1;
            return 0;
        }
        static string Title(string path)
        {
            string name = Regex.Replace(Path.GetFileNameWithoutExtension(path), @"\[[^\]]*\]|【[^】]*】", " ");
            name = Regex.Replace(name, @"S\d{1,2}E\d{1,3}|(?:^|[\s._-])EP?\s*\d{1,3}|第\s*\d{1,3}\s*[集话話]|\s-\s\d{1,3}|[（(]\d{1,3}[)）]\s*$", " ", RegexOptions.IgnoreCase);
            return Regex.Replace(name, @"[\s\p{P}\p{S}]", "").ToLowerInvariant();
        }
        public static bool TechnicalFolder(string name)
        {
            string clean = Regex.Replace(name ?? "", @"\[[^\]]*\]|【[^】]*】", "").Trim();
            return clean.Length == 0 || Regex.IsMatch(clean, @"^(?:Season\s*\d+|S\d+|第[一二三四五六七八九十\d]+季|\d{1,3}[-~]\d{1,3}|\d{3,4}P|BDRip|BluRay|SPs?|Extras?)$", RegexOptions.IgnoreCase);
        }
        static IEnumerable<string> Files(string folder, int depth)
        {
            if (depth > 3 || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) yield break;
            foreach (string path in Directory.EnumerateFiles(folder).Take(1000)) if (Video(path) && !Extra(path)) yield return path;
            foreach (string child in Directory.EnumerateDirectories(folder).Take(128))
                if (TechnicalFolder(Path.GetFileName(child))) foreach (string file in Files(child, depth + 1)) yield return file;
        }
        public static bool SeriesFolder(string path)
        {
            try
            {
                var files = Files(path, 0).Take(1000).ToArray();
                if (files.Length == 0 || files.Any(x => Episode(x) <= 0)) return false;
                // Distinct filenames with the same title and different episode
                // numbers are strong evidence; unrelated numbered films are not.
                var groups = files.GroupBy(Title).ToArray();
                if (groups.Length != 1 || groups[0].Key.Length == 0) return false;
                return files.Select(Episode).Distinct().Count() >= 2 || Season(path) > 0 || files.Any(x => Regex.IsMatch(Path.GetFileName(x), @"S\d{1,2}E\d{1,3}|(?:^|[\s._-])EP?\d{1,3}", RegexOptions.IgnoreCase));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
