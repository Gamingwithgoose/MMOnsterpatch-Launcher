using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;

namespace Goose.Monsterpatch.Screenshots
{
    // No Unity dependencies: all waiting happens off the game's main thread.
    internal sealed class ScreenshotPostDraft
    {
        public string MediaUrl = "";
        public string PostId = "";
        public bool OutcomeUncertain;
    }

    internal sealed class ScreenshotCommunityClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly Uri origin;
        private readonly string expectedAccountId;
        internal const int MaximumImageBytes = 8 * 1024 * 1024;

        public ScreenshotCommunityClient(string baseUrl, string token, string accountId, HttpMessageHandler handler = null)
        {
            Uri parsed;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out parsed) || parsed.Scheme != "https" ||
                !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
                throw new InvalidOperationException("The account service must use a valid HTTPS address.");
            // Session tokens are opaque. The community backend issues unprefixed
            // base64url tokens; only the older backend used la_/aio_ prefixes.
            // The authenticated /me request below validates the credential itself.
            if (string.IsNullOrWhiteSpace(token) || token.Trim().StartsWith("acct_", StringComparison.Ordinal) || token.Trim() == accountId)
                throw new InvalidOperationException("Your launcher session is unavailable. Sign in through the launcher again.");
            if (string.IsNullOrWhiteSpace(accountId)) throw new InvalidOperationException("The launcher account identity is missing.");
            origin = new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/");
            expectedAccountId = accountId;
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            http.Timeout = TimeSpan.FromSeconds(45);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MMOnsterpatchLauncher/0.19.11");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        internal async Task<string> PublishAsync(byte[] png, string caption, bool friendsOnly, ScreenshotPostDraft draft, Action<string> progress, CancellationToken cancellation)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            if (progress == null) progress = delegate { };
            if (!string.IsNullOrEmpty(draft.PostId)) return draft.PostId;
            if (draft.OutcomeUncertain) throw new InvalidOperationException("The previous post may have succeeded. Check your website feed before making another post.");
            if (png == null || png.Length == 0 || png.Length > MaximumImageBytes)
                throw new InvalidOperationException("The screenshot exceeds the website's 8 MB limit. Save it locally or capture at a lower resolution.");
            if ((caption ?? "").Length > 5000) throw new InvalidOperationException("Captions must be 5000 characters or fewer.");

            progress("Checking your website account...");
            await ValidateSessionAsync(cancellation).ConfigureAwait(false);

            if (string.IsNullOrEmpty(draft.MediaUrl))
            {
                progress("Uploading screenshot...");
                using (var request = CreateImageUpload(new Uri(origin, "api/community/media"), png))
                {
                    var uploaded = await SendJsonAsync(request, cancellation).ConfigureAwait(false);
                    string url = uploaded.MediaUrl ?? "";
                    Uri media;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out media) || media.Scheme != origin.Scheme || media.Authority != origin.Authority || !media.AbsolutePath.StartsWith("/community-media/", StringComparison.Ordinal))
                        throw new InvalidOperationException("The website returned an invalid image address.");
                    draft.MediaUrl = url;
                }
            }

            progress("Publishing your post...");
            using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "api/community/posts")))
            {
                request.Content = new StringContent(SerializePost(caption ?? "", friendsOnly ? "friends" : "global", draft.MediaUrl), Encoding.UTF8, "application/json");
                try
                {
                    var created = await SendJsonAsync(request, cancellation).ConfigureAwait(false);
                    draft.PostId = created.Post == null ? "" : created.Post.PostId ?? "";
                    if (string.IsNullOrEmpty(draft.PostId)) throw new InvalidOperationException("The website did not confirm a post ID.");
                    return draft.PostId;
                }
                catch (WebsiteRejectedException ex)
                {
                    // A server failure can happen after inserting the post (while constructing
                    // its response). Do not silently retry and create a duplicate.
                    if ((int)ex.Status >= 500) draft.OutcomeUncertain = true;
                    throw;
                }
                catch
                {
                    draft.OutcomeUncertain = true;
                    throw;
                }
            }
        }

        // One ownership chain: request -> multipart form -> image content.
        // Unity's Mono MultipartContent.Dispose is not safe to call twice.
        internal static HttpRequestMessage CreateImageUpload(Uri uri, byte[] png, MultipartFormDataContent form = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = form ?? new MultipartFormDataContent();
            try
            {
                var image = new ByteArrayContent(png);
                image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                ((MultipartFormDataContent)request.Content).Add(image, "file", "MMOnsterpatch-screenshot.png");
                return request;
            }
            catch { request.Dispose(); throw; }
        }

        internal async Task ValidateSessionAsync(CancellationToken cancellation)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "api/account/me")))
            {
                var accountResponse = await SendJsonAsync(request, cancellation).ConfigureAwait(false);
                if (accountResponse.Account == null || accountResponse.Account.AccountId != expectedAccountId)
                    throw new InvalidOperationException("The website session does not match your launcher account. Please sign in again.");
            }
        }

        private async Task<ApiResponse> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(false))
            {
                if (response == null || response.Content == null)
                    throw new InvalidOperationException("The website connection returned no response content.");
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string message;
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        message = "Your session expired or website access was denied. Sign in through the launcher again.";
                    else if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.MethodNotAllowed)
                        message = "The community posting API is not available on this account server.";
                    else if ((int)response.StatusCode == 429) message = "The website is busy with too many requests. Please wait before trying again.";
                    else if ((int)response.StatusCode == 413) message = "The website rejected the image size (maximum 8 MB).";
                    else message = "The website rejected the request (HTTP " + (int)response.StatusCode + ").";
                    throw new WebsiteRejectedException(response.StatusCode, message);
                }
                // Never display raw HTTP bodies: proxies can return HTML, and errors can
                // contain account details. Return only expected JSON data to the caller.
                ApiResponse parsed;
                try
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        parsed = new DataContractJsonSerializer(typeof(ApiResponse)).ReadObject(stream) as ApiResponse;
                }
                catch { throw new InvalidOperationException("The website returned an unreadable response."); }
                if (parsed == null || parsed.Success == false)
                    throw new InvalidOperationException("The website could not confirm this request.");
                return parsed;
            }
        }

        private static string SerializePost(string body, string visibility, string mediaUrl)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(PostRequest)).WriteObject(stream,
                    new PostRequest { Body = body, Visibility = visibility, MediaUrl = mediaUrl });
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        [DataContract]
        private sealed class ApiResponse
        {
            [DataMember(Name = "success")] public bool? Success;
            [DataMember(Name = "account")] public AccountResponse Account;
            [DataMember(Name = "post")] public PostResponse Post;
            [DataMember(Name = "mediaUrl")] public string MediaUrl;
        }
        [DataContract]
        private sealed class AccountResponse { [DataMember(Name = "accountId")] public string AccountId; }
        [DataContract]
        private sealed class PostResponse { [DataMember(Name = "postId")] public string PostId; }
        [DataContract]
        private sealed class PostRequest
        {
            [DataMember(Name = "body")] public string Body;
            [DataMember(Name = "visibility")] public string Visibility;
            [DataMember(Name = "mediaUrl")] public string MediaUrl;
        }

        public void Dispose() { http.Dispose(); }

        internal sealed class WebsiteRejectedException : Exception
        {
            internal readonly HttpStatusCode Status;
            internal WebsiteRejectedException(HttpStatusCode status, string message) : base(message) { Status = status; }
        }
    }
}
