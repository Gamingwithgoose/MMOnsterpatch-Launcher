using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Goose.Monsterpatch.Screenshots;
using UnityEngine;
using UnityEngine.Networking;

internal static class UnityTransportTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static async Task Drive(Task task, UnityScreenshotHttpHandler handler, MonoBehaviour host)
    {
        for (int i = 0; i < 1000 && !task.IsCompleted; i++) { handler.Pump(); host.Tick(); await Task.Delay(1); }
        Check(task.IsCompleted, "Transport did not settle.");
    }
    private static void Reset() { UnityWebRequest.Last = null; UnityWebRequest.Hang = false; UnityWebRequest.Status = 201; }
    private static void Test(string name, Func<Task> body) { Reset(); body().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + name); }

    internal static void RunAll()
    {
        Test("Unity bridge preserves image bytes, bearer header, and redirect protection", async () =>
        {
            var host = new MonoBehaviour();
            using (var handler = new UnityScreenshotHttpHandler(host))
            using (var http = new HttpClient(handler))
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "opaque-test");
                var task = http.PostAsync("https://example.test/api/community/media", new ByteArrayContent(new byte[] { 1, 2, 3 }));
                await Drive(task, handler, host);
                using (var response = await task)
                {
                    Check(response.StatusCode == HttpStatusCode.Created, "Lost status.");
                    Check((await response.Content.ReadAsStringAsync()).Contains("success"), "Lost response body.");
                }
                var request = UnityWebRequest.Last;
                Check(request.RequestHeaders["Authorization"] == "Bearer opaque-test", "Lost credential.");
                Check(request.uploadHandler.data.Length == 3 && request.uploadHandler.data[2] == 3, "Changed image bytes.");
                Check(request.redirectLimit == 0 && request.Disposed, "Redirect or disposal error.");
            }
        });
        Test("Unity bridge cancels before dispatch without sending a request", async () =>
        {
            var host = new MonoBehaviour(); var cts = new CancellationTokenSource();
            using (var handler = new UnityScreenshotHttpHandler(host))
            using (var http = new HttpClient(handler))
            {
                var task = http.GetAsync("https://example.test/api/account/me", cts.Token);
                cts.Cancel(); await Drive(task, handler, host);
                Check(task.IsCanceled && UnityWebRequest.Last == null, "Cancelled request was sent.");
            }
        });
        Test("Unity bridge aborts and disposes an in-flight cancelled request", async () =>
        {
            UnityWebRequest.Hang = true;
            var host = new MonoBehaviour(); var cts = new CancellationTokenSource();
            using (var handler = new UnityScreenshotHttpHandler(host))
            using (var http = new HttpClient(handler))
            {
                var task = http.GetAsync("https://example.test/api/account/me", cts.Token);
                handler.Pump(); Check(UnityWebRequest.Last != null, "Not dispatched.");
                cts.Cancel(); await Drive(task, handler, host);
                Check(task.IsCanceled && UnityWebRequest.Last.Aborted && UnityWebRequest.Last.Disposed, "Cancellation leaked a request.");
            }
        });
        Test("Unity bridge preserves HTTP rejection for account error handling", async () =>
        {
            UnityWebRequest.Status = 403; var host = new MonoBehaviour();
            using (var handler = new UnityScreenshotHttpHandler(host))
            using (var http = new HttpClient(handler))
            {
                var task = http.GetAsync("https://example.test/api/account/me"); await Drive(task, handler, host);
                using (var response = await task) Check(response.StatusCode == HttpStatusCode.Forbidden, "Lost rejection status.");
            }
        });
        Test("Unity bridge reports connection failure rather than a null response", async () =>
        {
            UnityWebRequest.Status = 0; var host = new MonoBehaviour();
            using (var handler = new UnityScreenshotHttpHandler(host))
            using (var http = new HttpClient(handler))
            {
                var task = http.GetAsync("https://example.test/api/account/me"); await Drive(task, handler, host);
                Check(task.IsFaulted && task.Exception.GetBaseException() is HttpRequestException, "Wrong connection failure.");
            }
        });
        Test("Unity bridge disposal settles pending requests", async () =>
        {
            var host = new MonoBehaviour(); var handler = new UnityScreenshotHttpHandler(host);
            using (var http = new HttpClient(handler))
            {
                var task = http.GetAsync("https://example.test/api/account/me"); handler.Dispose();
                await Drive(task, handler, host); Check(task.IsCanceled, "Pending request not cancelled.");
            }
        });
    }
}
