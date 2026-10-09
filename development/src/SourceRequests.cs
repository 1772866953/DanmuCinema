using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DanmuCinema
{
    internal static class SourceRequests
    {
        // Each failed attempt has its own cancelled token and disposed request.
        // Explicit cancellation, credentials errors and rate limits are never retried.
        public static async Task<T> Run<T>(Func<CancellationToken, Task<T>> request, string source, CancellationToken cancellation, TimeSpan? duration = null)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(duration ?? TimeSpan.FromSeconds(15));
                    try { return await request(timeout.Token).ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (!(error is OperationCanceledException || error is HttpRequestException || error is TimeoutException) || attempt == 3) throw;
                        timeout.Cancel();
                        Log.Write("弹幕来源「" + source + "」：连接暂不可用，重试 " + (attempt + 1) + "/3。");
                    }
                }
                await Task.Delay(150 * attempt, cancellation).ConfigureAwait(false);
            }
        }
    }
}
