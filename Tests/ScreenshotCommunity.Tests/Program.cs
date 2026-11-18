using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Goose.Monsterpatch.Screenshots;

class Handler : HttpMessageHandler
{
    internal string ExpectedToken = "la_test";
    internal readonly List<string> Paths = new List<string>();
    internal readonly List<string> Bodies = new List<string>();
    internal Func<int,HttpRequestMessage,HttpResponseMessage> Reply;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token)
    {
        if(req.Headers.Authorization.Scheme != "Bearer" || req.Headers.Authorization.Parameter != ExpectedToken) throw new Exception("Missing real-session credential.");
        if(req.Headers.UserAgent.ToString() != "MMOnsterpatchLauncher/0.19.11") throw new Exception("Missing API user agent.");
        Paths.Add(req.RequestUri.AbsolutePath);
        Bodies.Add(req.Content==null ? "" : await req.Content.ReadAsStringAsync());
        return Reply(Paths.Count,req);
    }
    internal static HttpResponseMessage Json(string body,int code=200) { return new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent(body)}; }
    internal static HttpResponseMessage Normal(int step,HttpRequestMessage req)
    {
        if(req.RequestUri.AbsolutePath=="/api/account/me")return Json("{\"online\":true,\"account\":{\"accountId\":\"acct_test\"},\"session\":{\"sessionToken\":\"ignored\"}}");
        if(req.RequestUri.AbsolutePath=="/api/community/media")return Json("{\"success\":true,\"mediaUrl\":\"https://example.test/community-media/shot.png\"}");
        return Json("{\"success\":true,\"post\":{\"postId\":\"post_test\"}}");
    }
}
class Program
{
    static int passed;
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static async Task ExpectFailure(Func<Task> action){try{await action();}catch{return;}throw new Exception("Expected failure.");}
    static async Task Publish(Handler h,ScreenshotPostDraft d, bool friends=true,byte[] png=null)
    {using(var c=new ScreenshotCommunityClient("https://example.test","la_test","acct_test",h))await c.PublishAsync(png ?? new byte[]{1,2,3},"caption \"quoted\"",friends,d,_=>{},CancellationToken.None);}
    static void Test(string name,Func<Task> test){test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS: "+name);}
    static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--live-account-check")
        {
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MMOnsterpatch","launcher_config.json");
            SavedSession saved;
            using(var stream=File.OpenRead(path)) saved=(SavedSession)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SavedSession)).ReadObject(stream);
            if(saved.AccountBaseUrl!="https://account.gamingwithgoose.com") throw new Exception("Unexpected account service.");
            using(var c=new ScreenshotCommunityClient(saved.AccountBaseUrl,saved.LauncherSessionToken,saved.AccountId))
                c.ValidateSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("PASS: updated posting client accepts the current saved token and the website confirms the matching account. No image or post was sent.");
            return;
        }
        Test("account verified before upload; multipart image and friends post use bearer session",async()=>{
            var h=new Handler{Reply=Handler.Normal};var d=new ScreenshotPostDraft();await Publish(h,d);
            Check(string.Join(",",h.Paths)=="/api/account/me,/api/community/media,/api/community/posts","Unexpected request sequence.");
            Check(h.Bodies[1].Contains("name=\"file\"") || h.Bodies[1].Contains("name=file"),"Missing multipart file field.");
            Check(h.Bodies[1].Contains("image/png"),"Missing PNG MIME.");
            Check(h.Bodies[2].Contains("\"visibility\":\"friends\"") && h.Bodies[2].Contains("mediaUrl"),"Wrong post fields.");
            Check(!h.Bodies[2].Contains("author") && d.PostId=="post_test","Wrong ownership or result.");
        });
        Test("everyone selection sends global visibility",async()=>{var h=new Handler{Reply=Handler.Normal};await Publish(h,new ScreenshotPostDraft(),false);Check(h.Bodies[2].Contains("\"visibility\":\"global\""),"Wrong audience.");});
        Test("expired session stops before image upload",async()=>{var h=new Handler{Reply=(n,r)=>Handler.Json("{}",403)};await ExpectFailure(()=>Publish(h,new ScreenshotPostDraft()));Check(h.Paths.Count==1,"Uploaded after rejected session.");});
        Test("account mismatch stops before image upload",async()=>{var h=new Handler{Reply=(n,r)=>Handler.Json("{\"account\":{\"accountId\":\"acct_other\"}}")} ;await ExpectFailure(()=>Publish(h,new ScreenshotPostDraft()));Check(h.Paths.Count==1,"Uploaded under wrong identity.");});
        Test("8 MB limit checked before network request",async()=>{var h=new Handler{Reply=Handler.Normal};await ExpectFailure(()=>Publish(h,new ScreenshotPostDraft(),true,new byte[8*1024*1024+1]));Check(h.Paths.Count==0,"Oversized image sent.");});
        Test("foreign media URL never posted",async()=>{var h=new Handler{Reply=(n,r)=>n==2?Handler.Json("{\"mediaUrl\":\"https://evil.test/community-media/x.png\"}"):Handler.Normal(n,r)};await ExpectFailure(()=>Publish(h,new ScreenshotPostDraft()));Check(h.Paths.Count==2,"Accepted foreign media.");});
        Test("ambiguous post timeout blocks automatic duplicate",async()=>{var h=new Handler{Reply=(n,r)=>n==3?throw new TaskCanceledException():Handler.Normal(n,r)};var d=new ScreenshotPostDraft();await ExpectFailure(()=>Publish(h,d));Check(d.OutcomeUncertain,"Lost ambiguous outcome.");var h2=new Handler{Reply=Handler.Normal};await ExpectFailure(()=>Publish(h2,d));Check(h2.Paths.Count==0,"Retried ambiguous post.");});
        Test("server failure after post request is treated as uncertain",async()=>{var h=new Handler{Reply=(n,r)=>n==3?Handler.Json("{}",500):Handler.Normal(n,r)};var d=new ScreenshotPostDraft();await ExpectFailure(()=>Publish(h,d));Check(d.OutcomeUncertain,"May duplicate server-side insert.");});
        Test("confirmed post cannot be submitted twice",async()=>{var h=new Handler{Reply=Handler.Normal};var d=new ScreenshotPostDraft{PostId="post_existing"};await Publish(h,d);Check(h.Paths.Count==0,"Duplicated confirmed post.");});
        Test("definite rejection can retry without uploading the image twice",async()=>{
            var h=new Handler{Reply=(n,r)=>n==3?Handler.Json("{}",400):Handler.Normal(n,r)};var d=new ScreenshotPostDraft();
            await ExpectFailure(()=>Publish(h,d));Check(!d.OutcomeUncertain && d.MediaUrl.Length>0,"Lost reusable upload.");
            var retry=new Handler{Reply=Handler.Normal};await Publish(retry,d);
            Check(retry.Paths.Count==2 && retry.Paths[1]=="/api/community/posts" && d.PostId=="post_test","Retry uploaded twice or failed.");
        });
        Test("HTML challenge response never causes an upload",async()=>{
            var h=new Handler{Reply=(n,r)=>Handler.Json("<html>challenge</html>")};
            await ExpectFailure(()=>Publish(h,new ScreenshotPostDraft()));Check(h.Paths.Count==1,"Uploaded after unreadable auth response.");
        });
        Test("current opaque website session tokens are accepted and validated by server",async()=>{
            string opaque="V2ViU2Vzc2lvblRva2VuX1dpdGhvdXRfQW55UHJlZml4";
            var h=new Handler{Reply=Handler.Normal,ExpectedToken=opaque};var d=new ScreenshotPostDraft();
            using(var c=new ScreenshotCommunityClient("https://example.test",opaque,"acct_test",h))
                await c.PublishAsync(new byte[]{1,2,3},"",true,d,_=>{},CancellationToken.None);
            Check(d.PostId=="post_test" && h.Paths[0]=="/api/account/me","Opaque session was not validated/published.");
        });
        Test("invalid opaque tokens still fail server validation before upload",async()=>{
            var h=new Handler{Reply=(n,r)=>Handler.Json("{}",401),ExpectedToken="opaque-invalid-session"};
            await ExpectFailure(async()=>{
                using(var c=new ScreenshotCommunityClient("https://example.test","opaque-invalid-session","acct_test",h))
                    await c.PublishAsync(new byte[]{1,2,3},"",true,new ScreenshotPostDraft(),_=>{},CancellationToken.None);
            });
            Check(h.Paths.Count==1,"Uploaded using invalid opaque token.");
        });
        Test("account ID cannot be used as a session credential",async()=>{await ExpectFailure(()=>{using(var c=new ScreenshotCommunityClient("https://example.test","acct_test","acct_test")){}return Task.CompletedTask;});});
        Test("plaintext endpoints are rejected",async()=>{await ExpectFailure(()=>{using(var c=new ScreenshotCommunityClient("http://example.test","la_test","acct_test")){}return Task.CompletedTask;});});
        Test("upload request disposes its multipart container exactly once",async()=>{
            var form=new StrictMultipart();
            using(var request=ScreenshotCommunityClient.CreateImageUpload(new Uri("https://example.test/api/community/media"),new byte[]{1,2,3},form))
            {
                Check(object.ReferenceEquals(request.Content,form),"Request does not own multipart content.");
                string body=await request.Content.ReadAsStringAsync();
                Check(body.Contains("MMOnsterpatch-screenshot.png") && body.Contains("image/png"),"Malformed upload.");
            }
            Check(form.DisposeCount==1,"Multipart content was not disposed exactly once.");
        });
        UnityTransportTests.RunAll();
        Console.WriteLine(passed+" community tests passed. All HTTP responses were simulated; nothing was uploaded or posted.");
    }
}

[System.Runtime.Serialization.DataContract]
class SavedSession
{
    [System.Runtime.Serialization.DataMember] public string AccountBaseUrl;
    [System.Runtime.Serialization.DataMember] public string AccountId;
    [System.Runtime.Serialization.DataMember] public string LauncherSessionToken;
}

class StrictMultipart : MultipartFormDataContent
{
    public int DisposeCount;
    protected override void Dispose(bool disposing)
    {
        if(disposing && ++DisposeCount!=1)throw new NullReferenceException("Simulated Unity Mono double-dispose failure.");
        base.Dispose(disposing);
    }
}
