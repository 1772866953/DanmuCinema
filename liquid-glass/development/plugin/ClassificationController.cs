using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DanmuCinema.Playback
{
    public sealed class ClassificationItem { public Guid Id { get;set; } public string Path { get;set; } public bool Anime { get;set; } }
    public sealed class ClassificationRequest { public ClassificationItem[] Items { get;set; } }
    public sealed class ClassificationOwnership { public string[] Genres { get;set; } = Array.Empty<string>(); public string[] Tags { get;set; } = Array.Empty<string>(); }

    [ApiController]
    [Authorize(Policy="RequiresElevation")]
    public sealed class ClassificationController : ControllerBase
    {
        readonly ILibraryManager library;
        readonly IItemPersistenceService persistence;
        readonly string journal;
        static readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public ClassificationController(ILibraryManager library,IItemPersistenceService persistence,IApplicationPaths paths)
        {this.library=library;this.persistence=persistence;journal=System.IO.Path.Combine(paths.DataPath,"danmucinema-classification.json");}

        [HttpPost("DanmuCinema/Classification")]
        public async Task<ActionResult> Apply([FromBody]ClassificationRequest request,CancellationToken cancellation)
        {
            if(request?.Items==null||request.Items.Length>500)return BadRequest();
            await gate.WaitAsync(cancellation);
            try {
                var owned=System.IO.File.Exists(journal)?JsonSerializer.Deserialize<Dictionary<Guid,ClassificationOwnership>>(await System.IO.File.ReadAllTextAsync(journal,cancellation)):new Dictionary<Guid,ClassificationOwnership>();
                var targets=new Dictionary<Guid,(BaseItem Item,bool Anime)>();int skipped=0;
                foreach(var row in request.Items) {
                    var source=library.GetItemById(row.Id);
                    if(source is not Video || !String.Equals(source.Path,row.Path,StringComparison.OrdinalIgnoreCase)){skipped++;continue;}
                    targets[source.Id]=(source,row.Anime);
                    foreach(var parent in source.GetParents().Where(x=>x is Series || x is Season)) {
                        if(targets.TryGetValue(parent.Id,out var previous))targets[parent.Id]=(parent,previous.Anime||row.Anime);
                        else targets[parent.Id]=(parent,row.Anime);
                    }
                }
                var changed=new List<BaseItem>();
                foreach(var target in targets.Values) {
                    var item=target.Item;owned.TryGetValue(item.Id,out var prior);prior??=new ClassificationOwnership();
                    string[] genres=item.Genres??Array.Empty<string>(),tags=item.Tags??Array.Empty<string>();
                    string[] newGenres,newTags;
                    if(target.Anime) {
                        var addGenres=new[]{"动画","动漫","Animation"}.Except(genres,StringComparer.OrdinalIgnoreCase).ToArray();
                        var addTags=new[]{"动漫","DanmuCinema:Anime"}.Except(tags,StringComparer.OrdinalIgnoreCase).ToArray();
                        newGenres=genres.Concat(addGenres).ToArray();newTags=tags.Concat(addTags).ToArray();
                        owned[item.Id]=new ClassificationOwnership{Genres=prior.Genres.Union(addGenres,StringComparer.OrdinalIgnoreCase).ToArray(),Tags=prior.Tags.Union(addTags,StringComparer.OrdinalIgnoreCase).ToArray()};
                    } else {
                        newGenres=genres.Except(prior.Genres,StringComparer.OrdinalIgnoreCase).ToArray();newTags=tags.Except(prior.Tags,StringComparer.OrdinalIgnoreCase).ToArray();owned.Remove(item.Id);
                    }
                    if(genres.SequenceEqual(newGenres)&&tags.SequenceEqual(newTags))continue;
                    item.Genres=newGenres;item.Tags=newTags;changed.Add(item);
                }
                // Direct database persistence intentionally bypasses metadata
                // savers: no NFO, poster, subtitle or video is written in media.
                if(changed.Count>0)persistence.SaveItems(changed,cancellation);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(journal));
                string temporary=journal+".tmp";await System.IO.File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(owned),cancellation);System.IO.File.Move(temporary,journal,true);
                return Ok(new{Updated=changed.Count,Skipped=skipped});
            } finally {gate.Release();}
        }
    }
}
