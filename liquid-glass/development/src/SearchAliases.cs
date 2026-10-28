using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public sealed partial class DanmuCatalog
    {
        async Task<string> Metadata(Provider provider, string path, object body, CancellationToken cancellation)
        {
            string key = DandanApiCache.Key("title-metadata-v1|" + path + "|" + (body == null ? "" : Json.Write(body)));
            return await Cache.Get(key, "search", "作品别名", () => FetchNetwork(provider, path, body, cancellation), cancellation, null,
                content => { var result = Json.Object(content); return path.Contains("/search/") ? Json.Array(result, "Data").Length > 0 : Json.Text(result, "Id") != ""; }).ConfigureAwait(false);
        }
        static string AliasKey(string text)
        { return Regex.Replace(SmartMatching.CleanTitle(text).ToLowerInvariant(), @"[\s\p{P}\p{S}]", ""); }
        static IEnumerable<string> Names(Dictionary<string, object> subject)
        {
            yield return Json.Text(subject, "Name"); yield return Json.Text(subject, "NameCn");
            foreach (var box in Json.Array(subject, "Infobox").OfType<Dictionary<string, object>>())
            {
                string key = Json.Text(box, "Key");
                if (!Regex.IsMatch(key, "别名|別名|英文名|原名|alias", RegexOptions.IgnoreCase)) continue;
                var rows = Json.Array(box, "Value").OfType<Dictionary<string, object>>().ToArray();
                if (rows.Length == 0) yield return Json.Text(box, "Value");
                else foreach (var row in rows) yield return Json.Text(row, "V");
            }
        }
        async Task<string[]> SearchAliases(string keyword, CancellationToken cancellation)
        {
            string clean = SmartMatching.CleanTitle(keyword), normalized = AliasKey(clean);
            if (!Regex.IsMatch(clean, "[a-zA-Z]{3}|[\u4e00-\u9fff]{2}")) return new string[0];
            string resolutionKey = DandanApiCache.Key("resolved-title-v1|" + normalized);
            var known = KnownSearchAliases(keyword); if(known.Length > 0) return known;
            try
            {
                var provider = new Provider { Id = "bangumi", Name = "作品别名", Url = "https://api.bgm.tv" };
                var result = Json.Object(await Metadata(provider, "/v0/search/subjects?limit=5", new { keyword = clean, filter = new { type = new[] { 2, 6 } } }, cancellation).ConfigureAwait(false));
                var aliases = new List<string>();
                foreach (var row in Json.Array(result, "Data").OfType<Dictionary<string, object>>().Take(3))
                {
                    var subject = row;
                    if (!Names(subject).Any(n => AliasKey(n) == normalized))
                    {
                        long id; if (!Int64.TryParse(Json.Text(row, "Id"), out id) || id <= 0) continue;
                        subject = Json.Object(await Metadata(provider, "/v0/subjects/" + id, null, cancellation).ConfigureAwait(false));
                    }
                    if (!Names(subject).Any(n => AliasKey(n) == normalized)) continue;
                    string chinese = Json.Text(subject, "NameCn");
                    if (Regex.IsMatch(chinese, "[\u4e00-\u9fff]")) aliases.Add(chinese);
                }
                var resolved = aliases.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
                if(resolved.Length > 0) Cache.Write(resolutionKey, "search", clean, Json.Write(resolved), resolved[0]);
                return resolved;
            }
            catch { cancellation.ThrowIfCancellationRequested(); return new string[0]; }
        }
        string[] KnownSearchAliases(string keyword)
        {
            try { string value=Cache.Read(DandanApiCache.Key("resolved-title-v1|"+AliasKey(keyword))); return value == null ? new string[0] : Json.Read<string[]>(value); }
            catch { return new string[0]; }
        }
    }
}
