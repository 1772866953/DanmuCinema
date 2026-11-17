using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    public static class MediaCovers
    {
        static readonly SemaphoreSlim frames = new SemaphoreSlim(2,2);
        public static LibraryEntry First(IEnumerable<LibraryEntry> entries)
        {
            return entries.OrderBy(x => { int n;return Int32.TryParse(Json.Text(x.Item,"ParentIndexNumber"),out n)&&n>0?n:Int32.MaxValue; })
                .ThenBy(x => { int n;return Int32.TryParse(Json.Text(x.Item,"IndexNumber"),out n)&&n>0?n:Int32.MaxValue; })
                .ThenBy(x=>x.Name,NaturalNames.Instance).FirstOrDefault();
        }
        public static async Task<byte[]> Resolve(Dictionary<string,object> item, bool series,
            Func<string,string,Task<byte[]>> poster, Func<Task<byte[]>> frame)
        {
            var choices=new List<Tuple<string,string>>();
            if(series)choices.Add(Tuple.Create(Json.Text(item,"SeriesId"),Json.Text(item,"SeriesPrimaryImageTag")));
            choices.Add(Tuple.Create(Json.Text(item,"Id"),Json.Text(Json.Child(item,"ImageTags"),"Primary")));
            foreach(var choice in choices.Where(x=>x.Item1!=""&&x.Item2!="").Distinct())
                try{var image=await poster(choice.Item1,choice.Item2);if(image!=null&&image.Length>0&&image.Length<=4*1024*1024)return image;}catch(OperationCanceledException){throw;}catch{}
            return await frame();
        }
        public static async Task<byte[]> Frame(string path, double seconds, CancellationToken cancellation)
        {
            if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||path.IndexOfAny(new[]{'"','\r','\n','\0'})>=0||!File.Exists(path))return null;
            string executable=Path.Combine(Paths.Runtime,"ffmpeg.exe");if(!File.Exists(executable))return null;
            await frames.WaitAsync(cancellation).ConfigureAwait(false);
            try{using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                using(var process=new Process{StartInfo=new ProcessStartInfo(executable,"-nostdin -v error -threads 1 -ss "+Math.Max(0,Math.Min(30,seconds)).ToString("0.###",CultureInfo.InvariantCulture)+" -i \""+path+"\" -map 0:v:0 -frames:v 1 -vf scale=800:-2 -threads 1 -f image2pipe -vcodec mjpeg -q:v 3 pipe:1"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true}}){
                    process.Start();using(timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}})){
                        var error=process.StandardError.ReadToEndAsync();using(var buffer=new MemoryStream()){
                            await process.StandardOutput.BaseStream.CopyToAsync(buffer).ConfigureAwait(false);await Task.Run(()=>process.WaitForExit()).ConfigureAwait(false);await error.ConfigureAwait(false);
                            timeout.Token.ThrowIfCancellationRequested();return process.ExitCode==0&&buffer.Length>0&&buffer.Length<=4*1024*1024?buffer.ToArray():null;
                        }
                    }
                }
            }}catch(OperationCanceledException){if(cancellation.IsCancellationRequested)throw;return null;}catch{return null;}
            finally{frames.Release();}
        }
    }
}
