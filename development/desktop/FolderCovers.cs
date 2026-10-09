using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanmuCinema.Desktop
{
    public sealed partial class ShellWindow
    {
        CancellationTokenSource coverCancellation;
        async void LoadFolderCovers()
        {
            if (coverCancellation != null) { coverCancellation.Cancel(); coverCancellation.Dispose(); }
            coverCancellation = new CancellationTokenSource(); var token = coverCancellation.Token; var snapshot = rows;
            if (snapshot == null) return;
            try
            {
                // Small local thumbnails only, requested serially and cached by tag.
                // No official danmaku/remote metadata request is made for artwork.
                foreach (var row in snapshot.Where(x => x.Entry.IsFolder))
                {
                    token.ThrowIfCancellationRequested(); var item = row.Entry.Members.Select(x => x.Item).FirstOrDefault(x => !String.IsNullOrEmpty(Json.Text(x, "SeriesPrimaryImageTag")) || Json.Child(x, "ImageTags") != null);
                    if (item == null) continue;
                    string id = Json.Text(item, "SeriesId"), tag = Json.Text(item, "SeriesPrimaryImageTag");
                    if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(tag)) { id = Json.Text(item, "Id"); tag = Json.Text(Json.Child(item, "ImageTags"), "Primary"); }
                    if (String.IsNullOrEmpty(tag)) continue;
                    string key = DandanApiCache.Key("poster:" + controller.Settings.ServerId + ":" + id + ":" + tag);
                    try
                    {
                        byte[] bytes;
                        string cached = await Task.Run(() => controller.Gateway.Catalog.Cache.Read(key), token);
                        if (cached != null) bytes = Convert.FromBase64String(cached);
                        else
                        {
                            if (!controller.Services.OwnsProcess || String.IsNullOrEmpty(controller.Services.Api.Token)) continue;
                            bytes = await controller.Services.Api.Poster(id, tag, token); if (bytes == null || bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024) continue;
                            token.ThrowIfCancellationRequested(); var encoded = Convert.ToBase64String(bytes); await Task.Run(() => controller.Gateway.Catalog.Cache.Write(key, "poster", row.DisplayName, encoded, row.DisplayName), token);
                        }
                        token.ThrowIfCancellationRequested();
                        var image = new BitmapImage(); using (var stream = new MemoryStream(bytes)) { image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 72; image.StreamSource = stream; image.EndInit(); image.Freeze(); }
                        if (!releasing && Object.ReferenceEquals(rows, snapshot)) row.SetCover(image);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Missing artwork leaves the vector placeholder. */ }
                }
            }
            catch (OperationCanceledException) { }
        }
        void StopFolderCovers() { if (coverCancellation != null) { coverCancellation.Cancel(); coverCancellation.Dispose(); coverCancellation = null; } }
    }
}
