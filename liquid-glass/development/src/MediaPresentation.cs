using System;
using System.Collections.Generic;
using System.IO;

namespace DanmuCinema
{
    // Display labels only. Identity, filenames, matching and sort keys stay intact.
    public static class MediaPresentation
    {
        public static string Category(Dictionary<string,object> item)
        {
            if(Json.Text(item,"DanmuCinemaCategory")=="动漫")return "动漫";
            return SmartMatching.IsEpisodic(item)?"电视剧":Json.Text(item,"Type")=="Movie"?"电影":"视频";
        }
        public static string Category(LibraryEntry entry)
        {
            if(!entry.IsFolder)return Category(entry.Item);
            var kinds=new HashSet<string>(Array.ConvertAll(entry.Members,x=>Category(x.Item)));
            return kinds.Count==1?System.Linq.Enumerable.First(kinds):"混合内容";
        }
        public static string Title(Dictionary<string, object> item)
        {
            string series = Json.Text(item, "SeriesName"); int episode = SmartMatching.Episode(item);
            if (!String.IsNullOrWhiteSpace(series)) return series + (episode > 0 ? " · 第 " + episode.ToString("D2") + " 集" : " · " + Json.Text(item, "Name"));
            string name = Json.Text(item, "Name"); if (String.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(Json.Text(item, "Path"));
            string cleaned = SmartMatching.CleanTitle(name);
            if (String.IsNullOrWhiteSpace(cleaned)) cleaned = name;
            return cleaned + (episode > 0 && SmartMatching.IsEpisodic(item) ? " · 第 " + episode.ToString("D2") + " 集" : "");
        }
    }
}
