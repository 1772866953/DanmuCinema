using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;

namespace DanmuCinema.Playback
{
    // Only augment automatic/mixed libraries. Explicit movie and TV libraries
    // continue to use the server's native resolvers.
    public sealed class AutoMediaResolver : IItemResolver
    {
        public ResolverPriority Priority => ResolverPriority.Plugin;
        public BaseItem ResolvePath(ItemResolveArgs args)
        {
            if (args.Parent == null || args.Parent.IsRoot || args.IsPhysicalRoot || args.IsVf || args.GetCollectionType() != null) return null;
            var series = args.Parent as Series ?? args.Parent.GetParents().OfType<Series>().FirstOrDefault();
            if (args.IsDirectory)
            {
                if (series != null || args.Parent is Season || MediaAuto.TechnicalFolder(Path.GetFileName(args.Path))) return null;
                if (MediaAuto.SeriesFolder(args.Path)) return new Series { Path = args.Path, Name = Path.GetFileName(args.Path) };
                return null;
            }
            if (series == null || !MediaAuto.Video(args.Path)) return null;
            int number = MediaAuto.Episode(args.Path); if (number <= 0) return null;
            var season = args.Parent as Season ?? args.Parent.GetParents().OfType<Season>().FirstOrDefault();
            int seasonNumber = season?.IndexNumber ?? MediaAuto.Season(args.Path);
            return new Episode { Path = args.Path, Name = "第 " + number.ToString("D2") + " 集", IndexNumber = number,
                ParentIndexNumber = seasonNumber > 0 ? seasonNumber : 1, SeriesId = series.Id, SeriesName = series.Name,
                SeasonId = season?.Id ?? System.Guid.Empty, SeasonName = season?.Name,
                VideoType = VideoType.VideoFile, Container = Path.GetExtension(args.Path).TrimStart('.') };
        }
    }
}
