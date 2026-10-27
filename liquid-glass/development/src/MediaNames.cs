using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DanmuCinema
{
    public static class MediaNames
    {
        public static bool Matches(Dictionary<string, object> item, string search)
        {
            if (String.IsNullOrWhiteSpace(search)) return true;
            string text = String.Join(" ", new[] { Json.Text(item, "Name"), Json.Text(item, "SeriesName"), Json.Text(item, "OriginalTitle"), Json.Text(item, "Path") });
            foreach (string word in search.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }
        public static string SearchTitle(Dictionary<string, object> item)
        {
            string title = Json.Text(item, "SeriesName");
            string path = Json.Text(item, "Path");
            if (String.IsNullOrWhiteSpace(title) && !String.IsNullOrWhiteSpace(path))
            {
                string folder = Path.GetFileName(WorkFolder(path));
                if (Regex.IsMatch(folder ?? "", @"[\u4e00-\u9fff]")) title = folder;
            }
            if (String.IsNullOrWhiteSpace(title)) title = Json.Text(item, "Name");
            // Broadens discovery only. The user must still choose the correct season.
            return Regex.Replace(title, @"\s*(?:S\d+|Season\s*\d+)\s*$", "", RegexOptions.IgnoreCase).Trim();
        }
        // Releases often live under an extra directory made entirely of tags,
        // e.g. [Group][01-12][1080P][subtitle]. It is not the anime title.
        public static string WorkFolder(string path)
        {
            string directory = Path.GetDirectoryName(path);
            for (int depth = 0; depth < 5 && !String.IsNullOrEmpty(directory); depth++)
            {
                string name = Path.GetFileName(directory), clean = SmartMatching.CleanTitle(name);
                bool tags = String.IsNullOrWhiteSpace(clean) || Regex.IsMatch(clean, @"^(?:\d{1,3}\s*[-~]\s*\d{1,3}|SPs?|OVA|OAD|NCOP|NCED|Extras?|特典|字幕|简繁内封字幕)$", RegexOptions.IgnoreCase);
                if (!tags) return directory;
                string parent = Path.GetDirectoryName(directory);
                if (String.IsNullOrEmpty(parent)) return directory;
                directory = parent;
            }
            return directory;
        }
        public static string CommentId(Dictionary<string, object> episode)
        {
            string id = Json.Text(episode, "CommentId");
            return String.IsNullOrWhiteSpace(id) ? Json.Text(episode, "cid") : id;
        }
        public static string EpisodeLabel(Dictionary<string, object> item)
        {
            string season = Json.Text(item, "ParentIndexNumber"), episode = Json.Text(item, "IndexNumber");
            string label = String.IsNullOrEmpty(season) ? "季号未识别" : season + "季";
            label += " · " + (String.IsNullOrEmpty(episode) ? "集号未识别" : episode + "集");
            return Json.Text(item, "SeriesName") + "  " + label + "  " + Json.Text(item, "Name");
        }
    }
}
