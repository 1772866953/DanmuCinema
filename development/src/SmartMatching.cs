using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DanmuCinema
{
    public static class SmartMatching
    {
        static int Number(string value) { int number; return Int32.TryParse(value, out number) && number > 0 ? number : 0; }
        public static int Season(Dictionary<string, object> item)
        {
            int number = Number(Json.Text(item, "ParentIndexNumber"));
            if (number > 0) return number;
            foreach (string value in new[] { Json.Text(item, "SeriesName"), Json.Text(item, "Name"), Path.GetFileNameWithoutExtension(Json.Text(item, "Path")) ?? "", Path.GetDirectoryName(Json.Text(item, "Path")) ?? "" })
            { number = SeasonTitle(value); if (number > 0) return number; }
            return 0;
        }
        public static int SeasonTitle(string title)
        {
            var match = Regex.Match(title ?? "", @"(?:\bSeason\s*|(?<![a-z])S)(\d{1,2})(?!\d)|第\s*([一二三四五六七八九十\d]+)\s*[季期]|\b(\d{1,2})(?:st|nd|rd|th)\s+season\b", RegexOptions.IgnoreCase);
            if (match.Groups[1].Success) return Number(match.Groups[1].Value);
            if (match.Groups[3].Success) return Number(match.Groups[3].Value);
            string chinese = match.Groups[2].Value;
            int n = Number(chinese); if (n > 0) return n;
            if (chinese.Length == 1) { int index = "一二三四五六七八九十".IndexOf(chinese, StringComparison.Ordinal); if (index >= 0) return index + 1; }
            return 0;
        }
        public static int Episode(Dictionary<string, object> item)
        {
            int number;
            // A trailing episode number is more reliable than scraper metadata for
            // releases such as "Hyakkano - 25 (1)" through "Hyakkano - 25 (12)".
            string filename = Path.GetFileNameWithoutExtension(Json.Text(item, "Path")) ?? "";
            if (Regex.IsMatch(filename, @"\b(?:SP\s*\d*|OVA|OAD|NCOP|NCED)\b|特别篇|特別篇|E\d+\s*[-~]\s*E?\d+", RegexOptions.IgnoreCase)) return 0;
            var trailing = Regex.Match(filename, @"[（(](\d{1,3})[)）]\s*$");
            if (trailing.Success) return Number(trailing.Groups[1].Value);
            var bracketed = Regex.Matches(filename, @"\[(\d{1,3})\]");
            if (bracketed.Count == 1) return Number(bracketed[0].Groups[1].Value);
            number = Number(Json.Text(item, "IndexNumber")); if (number > 0) return number;
            foreach (string value in new[] { Path.GetFileNameWithoutExtension(Json.Text(item, "Path")) ?? "", Json.Text(item, "Name") })
            {
                if (Regex.IsMatch(value, @"(?:E\d+\s*[-~]\s*E?\d+|\d+\s*[-~]\s*\d+|SP\s*\d*|OVA|OAD|特别篇|特別篇)", RegexOptions.IgnoreCase)) continue;
                var match = Regex.Match(value, @"S\d{1,2}E(\d{1,3})(?!\d)|(?:^|[\s._-])(?:EP?|第)\s*(\d{1,3})(?:话|話|集)?(?:\b|$)|[（(](\d{1,3})[)）]\s*$|(?:^|\s-\s)(\d{1,3})(?=\s|$|\[)", RegexOptions.IgnoreCase);
                foreach (Group group in match.Groups.Cast<Group>().Skip(1)) if (group.Success) return Number(group.Value);
                var suffix = Regex.Match(value, @"(?:^|[\s._-])(\d{1,3})(?:\s*\[[^\]]*\])*\s*$");
                if (suffix.Success) return Number(suffix.Groups[1].Value);
            }
            return 0;
        }
        public static string CleanTitle(string title)
        {
            string text = DanmuCatalog.Chinese(title ?? "", false);
            text = Regex.Replace(text, @"\[[^\]]*(?:\]|$)|【[^】]*(?:】|$)", " ");
            text = Regex.Replace(text, @"\b(?:\d{3,4}p|BDRip|WEB[- ]?DL|BluRay|HEVC|AVC|x26[45]|H\.?26[45]|MKV|MP4)\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:8|10|12)[- ]?bit\b|\b(?:FLAC|AAC|DTS(?:-HD)?|TRUEHD|EAC3|AC3)\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?:S\d{1,2}E\d{1,3}|\bSeason\s*\d+|(?<![a-z])S\d{1,2}(?!\d)|第\s*[一二三四五六七八九十\d]+\s*[季期]|\b\d+(?:st|nd|rd|th)\s+season\b)", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?:\s+(?:II|III|IV)|\s*\(\d{1,3}\)|\s+-\s+\d{1,3}|\s+\d{1,3})\s*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '_', '.');
        }
        public static int RemoteNumber(Dictionary<string, object> item)
        {
            if (Regex.IsMatch(Json.Text(item, "Title"), @"特别篇|特別篇|\b(?:SP|OVA|OAD)\b", RegexOptions.IgnoreCase)) return 0;
            var match = Regex.Match(Json.Text(item, "Number"), @"^\s*(?:第)?\s*(\d{1,3})(?:\.0+)?\s*(?:话|話|集)?\s*$");
            return match.Success ? Number(match.Groups[1].Value) : 0;
        }
        public static string[] Queries(string keyword)
        {
            string clean = CleanTitle(keyword);
            // History often stores a release folder name. Query the actual title
            // first, using the same official cache key as a manually typed title.
            var list = new List<string> { clean, keyword.Trim() };
            var chinese = Regex.Match(clean, @"[\u4e00-\u9fff]{6,}");
            if (chinese.Success) list.Add(chinese.Value.Substring(0, 4));
            else { var words = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (words.Length > 2) list.Add(String.Join(" ", words.Take(2))); }
            return list.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        }
        static string Normalize(string title) { return Regex.Replace(CleanTitle(title).ToLowerInvariant(), @"[\s\p{P}\p{S}]", ""); }
        public static double Score(string keyword, Dictionary<string, object> candidate, int season)
        {
            string a = Normalize(keyword), b = Normalize(Json.Text(candidate, "Name"));
            if (a.Length == 0 || b.Length == 0) return 0;
            double score;
            if (a == b) score = 100;
            else if (a.Contains(b) || b.Contains(a)) score = 75 + 20.0 * Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
            else
            {
                var left = new HashSet<string>(Enumerable.Range(0, Math.Max(0, a.Length - 1)).Select(i => a.Substring(i, 2)));
                var right = new HashSet<string>(Enumerable.Range(0, Math.Max(0, b.Length - 1)).Select(i => b.Substring(i, 2)));
                score = left.Count + right.Count == 0 ? 0 : 100.0 * left.Intersect(right).Count() * 2 / (left.Count + right.Count);
            }
            int candidateSeason = SeasonTitle(Json.Text(candidate, "Name"));
            if (season > 0) score += candidateSeason == season ? 12 : candidateSeason > 0 ? -50 : season > 1 ? -20 : 0;
            return Math.Max(0, Math.Min(100, score));
        }
        public static bool SameSeason(Dictionary<string, object> selected, Dictionary<string, object> candidate)
        {
            string path = Json.Text(selected, "Path"), other = Json.Text(candidate, "Path");
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(other) || IsStandaloneMovie(selected) || IsStandaloneMovie(candidate)) return false;
            bool sameFolder = String.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(other), StringComparison.OrdinalIgnoreCase);
            bool sameWorkFolder = String.Equals(MediaNames.WorkFolder(path), MediaNames.WorkFolder(other), StringComparison.OrdinalIgnoreCase);
            string series = Json.Text(selected, "SeriesId");
            bool sameSeries = !String.IsNullOrEmpty(series) && series == Json.Text(candidate, "SeriesId");
            int season = Season(selected), otherSeason = Season(candidate);
            if (season > 0 && otherSeason > 0 && season != otherSeason) return false;
            if (sameFolder || sameWorkFolder)
            {
                // Movie libraries label anime episodes as movies too. In that case
                // require an episode filename and the same cleaned release title,
                // instead of accepting unrelated numbered films in one folder.
                if (!sameSeries && (!sameFolder || Json.Text(selected, "Type") == "Movie" || Json.Text(candidate, "Type") == "Movie"))
                {
                    string title = FileSeriesTitle(path);
                    return title.Length > 0 && title == FileSeriesTitle(other);
                }
                return true;
            }
            return sameSeries && season > 0 && season == otherSeason;
        }
        public static bool IsEpisodic(Dictionary<string, object> item)
        {
            if (item == null) return false;
            if (Json.Text(item, "Type") == "Episode" || !String.IsNullOrWhiteSpace(Json.Text(item, "SeriesId"))) return true;
            string path = Json.Text(item, "Path");
            if (String.IsNullOrWhiteSpace(path)) return false;
            // Ignore movie scraper IndexNumber values; episodic evidence must
            // come from the original filename, not an unrelated metadata field.
            return Episode(new Dictionary<string, object> { { "Path", path } }) > 0 ||
                Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"\b(?:SP\s*\d*|OVA|OAD|NCOP|NCED)\b|特别篇|特別篇", RegexOptions.IgnoreCase);
        }
        public static bool IsStandaloneMovie(Dictionary<string, object> item)
        { return item != null && Json.Text(item, "Type") == "Movie" && !IsEpisodic(item); }
        static string FileSeriesTitle(string path)
        {
            string title = CleanTitle(Path.GetFileNameWithoutExtension(path));
            title = Regex.Replace(title, @"(?:^|[\s._-])(?:EP?|第)\s*\d{1,3}(?:话|話|集)?(?=\W|$)", " ", RegexOptions.IgnoreCase);
            title = Regex.Replace(title, @"[\s._-]+\d{1,3}\s*$", "");
            return Normalize(title);
        }
    }
    public sealed class BatchEntry
    {
        public Dictionary<string, object> Local, Remote;
        public int Number;
        public bool Selected;
        public string Status;
    }
    public static class BatchMatching
    {
        public static List<BatchEntry> Plan(IEnumerable<Dictionary<string, object>> local, IEnumerable<Dictionary<string, object>> remote)
        {
            var videos = local.GroupBy(x => Json.Text(x, "Path"), StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
            var episodes = remote.ToArray();
            var plan = new List<BatchEntry>();
            foreach (var video in videos)
            {
                int number = SmartMatching.Episode(video);
                var matches = episodes.Where(x => number > 0 && SmartMatching.RemoteNumber(x) == number).ToArray();
                string error = number == 0 ? "集号未识别" : videos.Count(x => SmartMatching.Episode(x) == number) > 1 ? "本地集号重复，请选择单集下载" : matches.Length > 1 ? "在线集号重复" : matches.Length == 0 ? "没有对应在线集数" : !File.Exists(Json.Text(video, "Path")) ? "本地文件不存在" : "待下载";
                plan.Add(new BatchEntry { Local = video, Remote = matches.Length == 1 ? matches[0] : null, Number = number, Selected = error == "待下载", Status = error });
            }
            return plan.OrderBy(x => x.Number == 0 ? Int32.MaxValue : x.Number).ToList();
        }
    }
}
