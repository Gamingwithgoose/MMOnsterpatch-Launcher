using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Goose.Monsterpatch.Screenshots
{
    // The community protocol stays testable as ordinary HTTP messages, while the
    // game uses Unity's own TLS/network stack instead of MonoWebRequestHandler.
    internal sealed class UnityScreenshotHttpHandler : HttpMessageHandler
    {
        private sealed class Pending
        {
            internal string Url, Method;
            internal byte[] Body;
            internal readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal CancellationToken Cancellation;
            internal readonly TaskCompletionSource<HttpResponseMessage> Completion =
                new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly MonoBehaviour host;
        private readonly ConcurrentQueue<Pending> pending = new ConcurrentQueue<Pending>();
        private volatile bool disposed;

        internal UnityScreenshotHttpHandler(MonoBehaviour host)
        {
            if (host == null) throw new ArgumentNullException("host");
            this.host = host;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (disposed) throw new ObjectDisposedException("UnityScreenshotHttpHandler");
            cancellation.ThrowIfCancellationRequested();
            var work = new Pending { Url = request.RequestUri.AbsoluteUri, Method = request.Method.Method, Cancellation = cancellation };
            foreach (var header in request.Headers) work.Headers[header.Key] = string.Join(", ", header.Value);
            if (request.Content != null)
            {
                work.Body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                foreach (var header in request.Content.Headers)
                    if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        work.Headers[header.Key] = string.Join(", ", header.Value);
            }
            cancellation.ThrowIfCancellationRequested();
            pending.Enqueue(work);
            // Disposal can race with enqueue; cancel here as well as in Dispose/Pump.
            if (disposed) work.Completion.TrySetCanceled();
            return await work.Completion.Task.ConfigureAwait(false);
        }

        // Called only from the screenshot component's main-thread Update.
        internal void Pump()
        {
            Pending work;
            while (pending.TryDequeue(out work))
            {
                if (disposed || work.Cancellation.IsCancellationRequested) work.Completion.TrySetCanceled();
                else host.StartCoroutine(SendOnMainThread(work));
            }
        }

        private IEnumerator SendOnMainThread(Pending work)
        {
            UnityWebRequest request = null;
            Exception setupError = null;
            try
            {
                request = new UnityWebRequest(work.Url, work.Method);
                request.downloadHandler = new DownloadHandlerBuffer();
                if (work.Body != null) request.uploadHandler = new UploadHandlerRaw(work.Body);
                request.redirectLimit = 0;
                request.timeout = 45;
                foreach (var header in work.Headers) request.SetRequestHeader(header.Key, header.Value);
                request.SendWebRequest();
            }
            catch (Exception ex) { setupError = ex; }
            if (setupError != null)
            {
                if (request != null) request.Dispose();
                work.Completion.TrySetException(new HttpRequestException("The game could not start the website request.", setupError));
                yield break;
            }

            using (request)
            {
                try
                {
                while (!request.isDone)
                {
                    if (disposed || work.Cancellation.IsCancellationRequested)
                    {
                        request.Abort();
                        work.Completion.TrySetCanceled();
                        yield break;
                    }
                    yield return null;
                }
                if (disposed || work.Cancellation.IsCancellationRequested)
                {
                    work.Completion.TrySetCanceled();
                    yield break;
                }
                try
                {
                    if (request.responseCode < 100)
                        throw new HttpRequestException("The game could not connect securely to the website.");
                    var response = new HttpResponseMessage((HttpStatusCode)request.responseCode);
                    response.Content = new ByteArrayContent(request.downloadHandler.data ?? new byte[0]);
                    var headers = request.GetResponseHeaders();
                    if (headers != null)
                        foreach (var header in headers)
                            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    if (!work.Completion.TrySetResult(response)) response.Dispose();
                }
                catch (Exception ex) { work.Completion.TrySetException(ex); }
                }
                finally { work.Completion.TrySetCanceled(); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            Pending work;
            while (pending.TryDequeue(out work)) work.Completion.TrySetCanceled();
            // Active coroutines abort/dispose on their next frame, on the main thread.
            base.Dispose(disposing);
        }
    }
}
