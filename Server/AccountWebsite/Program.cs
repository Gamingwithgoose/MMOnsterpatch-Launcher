using System.Linq;
using Microsoft.Extensions.FileProviders;
using MySqlConnector;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string serviceDir = AppContext.BaseDirectory;
string repackRoot = FindRepackRoot(serviceDir);
string configPath = Path.Combine(repackRoot, "Config", "website.ini");
string patchPayload = Path.Combine(repackRoot, "PatchPayload");
string websiteLogDir = Path.Combine(repackRoot, "Logs", "Website");
Directory.CreateDirectory(websiteLogDir);

var websiteConfig = WebsiteConfig.Load(configPath);
var accountStore = new AccountStore(repackRoot);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(websiteConfig.BindUrls);

// Permit only explicitly trusted browser frontends; native launcher requests
// do not need CORS. Keep authentication requirements unchanged.
var browserOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "https://gamingwithgoose.com",
    "https://www.gamingwithgoose.com"
};
foreach (string candidate in new[] { websiteConfig.PublicUrl, websiteConfig.PublicHttpsUrl, websiteConfig.LocalUrl }
    .Concat(websiteConfig.AllowedBrowserOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)))
{
    if (Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        browserOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
}
builder.Services.AddCors(options => options.AddPolicy("TrustedBrowserApi", policy =>
    policy.WithOrigins(browserOrigins.ToArray())
        .WithMethods("GET", "POST", "OPTIONS")
        .WithHeaders("Content-Type", "Authorization", "Accept")
        .AllowCredentials()));

var app = builder.Build();
app.UseRouting();
app.UseCors("TrustedBrowserApi");

if (Directory.Exists(patchPayload))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(patchPayload),
        RequestPath = "/patch"
    });
}

app.MapGet("/", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromCookie(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    string body = account is null ? """
<h1>MMOnsterpatch Account Website</h1>
<p>Register, log in, and later manage your online MMOnsterpatch characters from here.</p>
<p><a href='/register'>Register</a> · <a href='/login'>Log In</a> · <a href='/forgot-password'>Forgot Password</a> · <a href='/patch/manifest.json'>Patch Manifest</a> · <a href='/health'>Health</a></p>
""" : $"""
<h1>Welcome back, {Html(account.DisplayName)}</h1>
<p>You are logged in as <strong>{Html(account.Username)}</strong>.</p>
<p><a href='/account'>View Account / Characters</a> · <a href='/logout'>Log Out</a> · <a href='/patch/manifest.json'>Patch Manifest</a> · <a href='/health'>Health</a></p>
""";
    return Results.Content(Page("MMOnsterpatch Account", body), "text/html");
});

app.MapGet("/register", () => Results.Content(Page("Register", """
<h1>Create Account</h1>
<p>This creates a fresh MMOnsterpatch account. Steam verification/linking will be added next, but this account is already what the launcher uses for Online Play.</p>
<form method='post' action='/register'>
<label>Email</label><input name='email' type='email' placeholder='you@example.com' required />
<label>Username</label><input name='username' type='text' placeholder='Goose' required />
<label>Display Name</label><input name='displayName' type='text' placeholder='Goose' required />
<label>Password</label><input name='password' type='password' required />
<button type='submit'>Create Account</button>
</form>
<p><a href='/login'>Already have an account?</a></p>
"""), "text/html"));

app.MapPost("/register", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var req = new RegisterRequest(
        form["email"].ToString(),
        form["username"].ToString(),
        form["displayName"].ToString(),
        form["password"].ToString()
    );
    var result = accountStore.Register(req, GetClientIp(ctx));
    if (!result.Success || result.Account is null || result.Session is null)
        return Results.Content(Page("Register", $"<h1>Registration failed</h1><p>{Html(result.Message)}</p><p><a href='/register'>Try again</a></p>"), "text/html");
    accountStore.WriteSessionCookie(ctx, result.Session.SessionToken);
    return Results.Redirect("/account");
});

app.MapGet("/login", () => Results.Content(Page("Log In", """
<h1>Log In</h1>
<form method='post' action='/login'>
<label>Username or Email</label><input name='usernameOrEmail' type='text' required />
<label>Password</label><input name='password' type='password' required />
<button type='submit'>Log In</button>
</form>
<p><a href='/register'>Create an account</a> · <a href='/forgot-password'>Forgot Password</a></p>
"""), "text/html"));

app.MapPost("/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var result = accountStore.Login(new LoginRequest(form["usernameOrEmail"].ToString(), form["password"].ToString()), GetClientIp(ctx));
    if (!result.Success || result.Account is null || result.Session is null)
        return Results.Content(Page("Log In", $"<h1>Login failed</h1><p>{Html(result.Message)}</p><p><a href='/login'>Try again</a></p>"), "text/html");
    accountStore.WriteSessionCookie(ctx, result.Session.SessionToken);
    return Results.Redirect("/account");
});

app.MapGet("/logout", (HttpContext ctx) =>
{
    string? token = ctx.Request.Cookies[AccountStore.WebSessionCookieName];
    if (!string.IsNullOrWhiteSpace(token)) accountStore.RevokeSession(token);
    ctx.Response.Cookies.Delete(AccountStore.WebSessionCookieName);
    return Results.Redirect("/");
});

app.MapGet("/account", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromCookie(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (account is null)
        return Results.Redirect("/login");
    var chars = accountStore.GetCharactersForAccount(account).OrderBy(c => c.SlotIndex).ThenBy(c => c.DisplayName).ToList();
    var sb = new StringBuilder();
    sb.Append($"<h1>{Html(account.DisplayName)}'s Account</h1>");
    sb.Append($"<p><strong>Username:</strong> {Html(account.Username)}<br/><strong>Email:</strong> {Html(account.Email)}<br/><strong>Steam linked:</strong> {(account.SteamVerified ? "Yes" : "Not yet")}</p>");
    sb.Append("<h2>Characters</h2>");
    if (chars.Count == 0)
    {
        sb.Append("<p>No online characters yet. Create or load an online save slot from the launcher/game and it will appear here.</p>");
    }
    else
    {
        sb.Append("<div class='list'>");
        foreach (var c in chars)
        {
            sb.Append($"<a class='row' href='/character/{WebUtility.UrlEncode(c.CharacterId)}'><strong>{Html(c.PublicHandle)}</strong><span>Slot {c.SlotIndex} · Rank {Html(c.RankProfile?.Rank ?? "E")} · RP {c.RankProfile?.Rp ?? 0}</span></a>");
        }
        sb.Append("</div>");
    }
    sb.Append("<p><a href='/logout'>Log Out</a></p>");
    return Results.Content(Page("Account", sb.ToString()), "text/html");
});

app.MapGet("/launcher/profile", (HttpContext ctx, string? token) =>
{
    if (!string.IsNullOrWhiteSpace(token))
    {
        var session = accountStore.GetSession(token);
        if (session is not null)
        {
            accountStore.WriteSessionCookie(ctx, session.SessionToken);
            return Results.Redirect("/profile");
        }
    }
    return Results.Redirect("/login");
});

app.MapGet("/launcher/manage-account", (HttpContext ctx, string? token) =>
{
    if (!string.IsNullOrWhiteSpace(token))
    {
        var session = accountStore.GetSession(token);
        if (session is not null)
        {
            accountStore.WriteSessionCookie(ctx, session.SessionToken);
            return Results.Redirect("/manage-account");
        }
    }
    return Results.Redirect("/login");
});

app.MapGet("/profile", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromCookie(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (account is null)
        return Results.Redirect("/login");

    var chars = accountStore.GetCharactersForAccount(account).OrderBy(c => c.SlotIndex).ThenBy(c => c.DisplayName).ToList();
    string created = account.CreatedUtc == default ? "Unknown" : account.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    string body = $"""
<h1>Account Profile</h1>
<div class='profileHeader'>
  <div class='avatar'>{Html(Initials(account.DisplayName, account.Username))}</div>
  <div>
    <h2>{Html(account.DisplayName)}</h2>
    <p class='muted'>@{Html(account.Username)}</p>
  </div>
</div>
<div class='details'>
  <div><strong>Username</strong><span>{Html(account.Username)}</span></div>
  <div><strong>Display Name</strong><span>{Html(account.DisplayName)}</span></div>
  <div><strong>Email</strong><span>{Html(account.Email)}</span></div>
  <div><strong>Created</strong><span>{Html(created)}</span></div>
  <div><strong>Total Characters</strong><span>{chars.Count}</span></div>
</div>
<p><a class='buttonLink' href='/manage-account'>Manage Account</a> <a class='buttonLink secondary' href='/account'>View Characters</a></p>
""";
    return Results.Content(Page("Profile", body), "text/html");
});

app.MapGet("/manage-account", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromCookie(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (account is null)
        return Results.Redirect("/login");
    return Results.Content(Page("Manage Account", ManageAccountForm(account, "")), "text/html");
});

app.MapPost("/manage-account", async (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromCookie(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (account is null)
        return Results.Redirect("/login");

    var form = await ctx.Request.ReadFormAsync();
    var result = accountStore.UpdateAccountProfile(
        account.AccountId,
        form["username"].ToString(),
        form["displayName"].ToString(),
        form["currentPassword"].ToString(),
        form["newPassword"].ToString(),
        form["confirmPassword"].ToString());

    var refreshed = accountStore.GetAccount(account.AccountId) ?? account;
    string msg = result.Success
        ? "<p class='ok'>Saved account changes.</p>"
        : $"<p class='error'>{Html(result.Message)}</p>";
    return Results.Content(Page("Manage Account", ManageAccountForm(refreshed, msg)), "text/html");
});

app.MapGet("/character/{characterId}", (HttpContext ctx, string characterId) =>
{
    var session = accountStore.GetSessionFromCookie(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (account is null)
        return Results.Redirect("/login");
    var c = accountStore.GetCharactersForAccount(account).FirstOrDefault(x => x.CharacterId == characterId);
    if (c is null)
        return Results.Content(Page("Character", "<h1>Character not found</h1><p>That character is not linked to this account.</p><p><a href='/account'>Back to account</a></p>"), "text/html");
    var rp = c.RankProfile ?? new CharacterRankProfile();
    string body = $"""
<h1>{Html(c.PublicHandle)}</h1>
<p><strong>Display name:</strong> {Html(c.DisplayName)}<br/>
<strong>Slot:</strong> {c.SlotIndex}<br/>
<strong>Character ID:</strong> <code>{Html(c.CharacterId)}</code></p>
<h2>Rank Profile</h2>
<p><strong>Season:</strong> {Html(rp.SeasonName ?? "Season 0")}<br/>
<strong>Rank:</strong> {Html(rp.Rank ?? "E")}<br/>
<strong>RP:</strong> {rp.Rp}<br/>
<strong>Wins/Losses/Draws:</strong> {rp.Wins}/{rp.Losses}/{rp.Draws}<br/>
<strong>Highest:</strong> {Html(rp.HighestRank ?? "E")} ({rp.HighestRp} RP)</p>
<p><a href='/account'>Back to account</a></p>
""";
    return Results.Content(Page(c.PublicHandle, body), "text/html");
});

app.MapGet("/forgot-password", () => Results.Content(Page("Forgot Password", """
<h1>Forgot Password</h1>
<p>Password reset is still a placeholder. SMTP/token reset will come after the account/game-ticket flow is verified.</p>
<form>
<label>Email</label><input type='email' placeholder='you@example.com'/>
<button type='button'>Send Reset Placeholder</button>
</form>
"""), "text/html"));

app.MapGet("/manifest.json", () => Results.Redirect("/patch/manifest.json"));
app.MapGet("/favicon.ico", () => Results.NoContent());

app.MapPost("/api/account/register", async (HttpContext ctx) =>
{
    var req = await JsonSerializer.DeserializeAsync<RegisterRequest>(ctx.Request.Body, JsonOpts()) ?? new RegisterRequest("", "", "", "");
    var result = accountStore.Register(req, GetClientIp(ctx));
    return result.Success ? Results.Json(result.ToApi()) : Results.BadRequest(result.ToApi());
});

app.MapPost("/api/account/login", async (HttpContext ctx) =>
{
    var req = await JsonSerializer.DeserializeAsync<LoginRequest>(ctx.Request.Body, JsonOpts()) ?? new LoginRequest("", "");
    var result = accountStore.Login(req, GetClientIp(ctx));
    return result.Success ? Results.Json(result.ToApi()) : Results.BadRequest(result.ToApi());
});

app.MapGet("/api/account/me", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromRequest(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (session is null || account is null)
        return Results.Unauthorized();
    return Results.Json(new { online = true, account = account.ToPublic(), session = session.ToPublic() });
});

app.MapPost("/api/account/game-ticket", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromRequest(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    if (account is null || session is null) return Results.Unauthorized();
    // First pass: the current game client still consumes the AIO-compatible session token.
    // Later this becomes a one-use game ticket once the patcher protocol is fully updated.
    return Results.Json(new
    {
        success = true,
        message = "AIO-compatible game ticket issued.",
        account = account.ToPublic(),
        gameTicket = new
        {
            ticketToken = session.AioSessionToken,
            sessionToken = session.SessionToken,
            aioSessionToken = session.AioSessionToken,
            accountId = account.AccountId,
            pseudoSteamId = account.PseudoSteamId,
            displayName = account.DisplayName,
            expiresUtc = session.ExpiresUtc
        }
    });
});

app.MapGet("/api/account/characters", (HttpContext ctx) =>
{
    var session = accountStore.GetSessionFromRequest(ctx);
    var account = session is null ? null : accountStore.GetAccount(session.AccountId);
    return account is null ? Results.Unauthorized() : Results.Json(new { online = true, characters = accountStore.GetCharactersForAccount(account) });
});

app.MapGet("/api/launcher/status", () => Results.Json(ReadLauncherStatus(repackRoot, websiteConfig)));
app.MapGet("/api/server/online-users", () => Results.Json(ReadOnlineUsers(repackRoot)));

app.MapGet("/health", () => Results.Json(new
{
    online = true,
    service = "account-website",
    version = "0.15.0",
    accountAuth = "mariadb-official",
    bindUrls = websiteConfig.BindUrls,
    publicUrl = websiteConfig.PublicUrl,
    patchManifestUrl = websiteConfig.PatchManifestUrl,
    root = repackRoot,
    utc = DateTime.UtcNow
}));

Console.Title = "MMOnsterpatch Account Website";
Log("MMOnsterpatch Account Website v0.15.0 - MariaDBOfficial");
Log($"Root: {repackRoot}");
Log($"Config: {configPath}");
Log($"Listening on: {websiteConfig.BindUrls}");
Log($"Local test URL: {websiteConfig.LocalUrl}");
Log($"Public URL: {websiteConfig.PublicUrl}");
Log($"Patch manifest URL: {websiteConfig.PatchManifestUrl}");
Log("Cloudflare Tunnel route should be: account.gamingwithgoose.com -> http://127.0.0.1:8080");

app.Run();

static object ReadLauncherStatus(string repackRoot, WebsiteConfig websiteConfig)
{
    string path = Path.Combine(repackRoot, "OfficialServer", "data", "runtime_status.json");
    try
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double updatedUnix = root.TryGetProperty("updatedUnix", out var updatedProp) && updatedProp.TryGetDouble(out double u) ? u : 0;
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (updatedUnix > 0 && nowUnix - updatedUnix <= 15.0)
                return root.Clone();
        }
    }
    catch { }

    return new
    {
        online = false,
        serverName = "MMOnsterpatch UnOfficial Server",
        serverVersion = "unknown",
        managerBuild = "v0.12.8",
        authOnline = false,
        worldOnline = false,
        tradingPostOnline = false,
        socialOnline = false,
        playersOnline = 0,
        socialClients = 0,
        players = Array.Empty<object>(),
        accountPublicUrl = websiteConfig.PublicUrl,
        patchManifestUrl = websiteConfig.PatchManifestUrl,
        reason = File.Exists(path) ? "runtime_status_stale" : "runtime_status_missing",
        utc = DateTime.UtcNow
    };
}

static object ReadOnlineUsers(string repackRoot)
{
    string path = Path.Combine(repackRoot, "OfficialServer", "data", "runtime_status.json");
    try
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new
            {
                online = root.TryGetProperty("online", out var online) && online.GetBoolean(),
                playersOnline = root.TryGetProperty("playersOnline", out var count) ? count.GetInt32() : 0,
                players = root.TryGetProperty("players", out var players) ? players.Clone() : JsonDocument.Parse("[]").RootElement.Clone(),
                utc = DateTime.UtcNow
            };
        }
    }
    catch { }

    return new { online = false, playersOnline = 0, players = Array.Empty<object>(), utc = DateTime.UtcNow };
}

static string FindRepackRoot(string start)
{
    string dir = Path.GetFullPath(start);

    for (int i = 0; i < 8; i++)
    {
        if (Directory.Exists(Path.Combine(dir, "Config")) && Directory.Exists(Path.Combine(dir, "PatchPayload")))
            return dir;

        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }

    return Path.GetFullPath(Path.Combine(start, ".."));
}

static void Log(string message)
{
    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    Console.WriteLine(line);
    try
    {
        string root = FindRepackRoot(AppContext.BaseDirectory);
        string logDir = Path.Combine(root, "Logs", "Website");
        Directory.CreateDirectory(logDir);
        File.AppendAllText(Path.Combine(logDir, "website.log"), line + Environment.NewLine);
    }
    catch { }
}

static string Page(string title, string body)
{
    return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>{{WebUtility.HtmlEncode(title)}}</title>
<style>
body { background:#080B10; color:#ECECEC; font-family:Segoe UI,Arial,sans-serif; margin:40px; }
a { color:#DEC5FF; }
h1,h2 { color:#DEC5FF; }
.card { max-width:900px; background:#0A0F14; border:1px solid #35404C; padding:24px; border-radius:14px; box-shadow:0 0 32px rgba(90,33,151,.25); }
form { display:grid; gap:10px; max-width:520px; background:#0F151C; border:1px solid #35404C; padding:20px; border-radius:10px; }
input { background:#151B23; color:#ECECEC; border:1px solid #39434F; padding:10px; border-radius:6px; }
input[readonly] { color:#B8BCC3; background:#10151B; cursor:not-allowed; }
button,.buttonLink { background:#5A2197; color:#fff; border:1px solid #9A4DED; padding:10px 14px; border-radius:6px; font-weight:bold; text-decoration:none; display:inline-block; }
button:disabled { opacity:.45; cursor:not-allowed; }
.buttonLink.secondary { background:#171D25; color:#DEC5FF; border-color:#563489; }
code { color:#DEC5FF; }
.muted { color:#B8BCC3; }
.ok { color:#2DE342; font-weight:700; }
.error { color:#FFA6A6; font-weight:700; }
.profileHeader { display:flex; align-items:center; gap:18px; margin:10px 0 22px; }
.profileHeader h2 { margin:0; }
.avatar { width:78px; height:78px; border-radius:999px; display:flex; align-items:center; justify-content:center; background:#1A2028; border:1px solid #DEC5FF; color:#DEC5FF; font-size:28px; font-weight:800; box-shadow:0 0 18px rgba(154,77,237,.25); }
.details { display:grid; gap:10px; max-width:560px; margin:18px 0 24px; }
.details div { display:flex; justify-content:space-between; gap:24px; background:#10151B; border:1px solid #35404C; border-radius:10px; padding:13px 15px; }
.details span { color:#B8BCC3; }
.list { display:grid; gap:10px; margin:12px 0; }
.row { display:flex; justify-content:space-between; gap:20px; text-decoration:none; background:#10151B; border:1px solid #35404C; padding:14px; border-radius:10px; }
.row:hover { border-color:#9A4DED; box-shadow:0 0 14px rgba(154,77,237,.2); }
.row span { color:#B8BCC3; }
</style>
</head>
<body><div class="card">{{body}}</div></body>
</html>
""";
}

static string ManageAccountForm(AccountRecord account, string message)
{
    return $"""
<h1>Manage Account</h1>
{message}
<div class='profileHeader'>
  <div class='avatar'>{Html(Initials(account.DisplayName, account.Username))}</div>
  <div>
    <h2>{Html(account.DisplayName)}</h2>
    <p class='muted'>Profile picture <a href='#'>Edit</a> <span class='muted'>(upload coming later)</span></p>
  </div>
</div>
<form method='post' action='/manage-account'>
<label>Username</label><input name='username' type='text' value='{HtmlAttr(account.Username)}' readonly aria-readonly='true' title='Username cannot be changed after account creation.' />
<p class='muted'>Username is permanent after account creation.</p>
<label>Display Name</label><input name='displayName' type='text' value='{HtmlAttr(account.DisplayName)}' required />
<h2>Change Password</h2>
<label>Current Password</label><input name='currentPassword' type='password' autocomplete='current-password' />
<label>New Password</label><input name='newPassword' type='password' autocomplete='new-password' />
<label>Confirm New Password</label><input name='confirmPassword' type='password' autocomplete='new-password' />
<h2>MFA</h2>
<p class='muted'>MFA setup is reserved for the next security pass after the launcher/account flow is stable.</p>
<button type='button' disabled>Add MFA</button>
<button type='submit'>Save Changes</button>
</form>
<p><a href='/profile'>Back to Profile</a></p>
""";
}

static string Initials(string? displayName, string? username)
{
    string src = !string.IsNullOrWhiteSpace(displayName) ? displayName! : (username ?? "M");
    var parts = src.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length >= 2) return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
    return src.Length > 0 ? src[0].ToString().ToUpperInvariant() : "M";
}

static string HtmlAttr(string? value) => WebUtility.HtmlEncode(value ?? "");

static string Html(string? value) => WebUtility.HtmlEncode(value ?? "");

static string GetClientIp(HttpContext ctx)
{
    string? cf = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();
    string? xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(xff)) return xff.Split(',')[0].Trim();
    return ctx.Connection.RemoteIpAddress?.ToString() ?? "";
}

static JsonSerializerOptions JsonOpts() => new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

sealed class WebsiteConfig
{
    public string AllowedBrowserOrigins { get; set; } = "";
    public string BindUrls { get; set; } = "http://127.0.0.1:8080";
    public string PublicUrl { get; set; } = "https://account.gamingwithgoose.com";
    public string PublicHttpsUrl { get; set; } = "https://account.gamingwithgoose.com";
    public string PatchManifestUrl { get; set; } = "https://account.gamingwithgoose.com/patch/manifest.json";
    public string LocalUrl { get; set; } = "http://127.0.0.1:8080";

    public static WebsiteConfig Load(string path)
    {
        var cfg = new WebsiteConfig();
        if (!File.Exists(path)) return cfg;
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (value.Length == 0) continue;
            if (key.Equals("BindUrls", StringComparison.OrdinalIgnoreCase)) cfg.BindUrls = value;
            else if (key.Equals("BindUrl", StringComparison.OrdinalIgnoreCase)) cfg.BindUrls = value;
            else if (key.Equals("PublicUrl", StringComparison.OrdinalIgnoreCase)) cfg.PublicUrl = value.TrimEnd('/');
            else if (key.Equals("PublicHttpsUrl", StringComparison.OrdinalIgnoreCase)) cfg.PublicHttpsUrl = value.TrimEnd('/');
            else if (key.Equals("PatchManifestUrl", StringComparison.OrdinalIgnoreCase)) cfg.PatchManifestUrl = value;
            else if (key.Equals("LocalUrl", StringComparison.OrdinalIgnoreCase)) cfg.LocalUrl = value.TrimEnd('/');
            else if (key.Equals("AllowedBrowserOrigins", StringComparison.OrdinalIgnoreCase)) cfg.AllowedBrowserOrigins = value;
        }
        return cfg;
    }
}

sealed record RegisterRequest(string Email, string Username, string DisplayName, string Password);
sealed record LoginRequest(string UsernameOrEmail, string Password);

sealed class DatabaseConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3307;
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";
    public string AuthDatabase { get; set; } = "mmonsterpatch_auth";
    public string SocialDatabase { get; set; } = "mmonsterpatch_social";
    public string TradingPostDatabase { get; set; } = "mmonsterpatch_tradingpost";

    public string AuthConnectionString => $"Server={Host};Port={Port};User ID={User};Password={Password};Database={AuthDatabase};CharSet=utf8mb4;Allow User Variables=True;";
    public string SocialConnectionString => $"Server={Host};Port={Port};User ID={User};Password={Password};Database={SocialDatabase};CharSet=utf8mb4;Allow User Variables=True;";

    public static DatabaseConfig Load(string repackRoot)
    {
        var cfg = new DatabaseConfig();
        string path = Path.Combine(repackRoot, "Config", "database.ini");
        if (File.Exists(path))
        {
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) cfg.Host = value;
                else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int port)) cfg.Port = port;
                else if (key.Equals("User", StringComparison.OrdinalIgnoreCase)) cfg.User = value;
                else if (key.Equals("Password", StringComparison.OrdinalIgnoreCase)) cfg.Password = value;
                else if (key.Equals("AuthDatabase", StringComparison.OrdinalIgnoreCase)) cfg.AuthDatabase = value;
                else if (key.Equals("SocialDatabase", StringComparison.OrdinalIgnoreCase)) cfg.SocialDatabase = value;
                else if (key.Equals("TradingPostDatabase", StringComparison.OrdinalIgnoreCase)) cfg.TradingPostDatabase = value;
            }
        }
        cfg.Host = Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_HOST") ?? cfg.Host;
        if (int.TryParse(Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_PORT"), out int envPort)) cfg.Port = envPort;
        cfg.User = Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_USER") ?? cfg.User;
        cfg.Password = Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_PASSWORD") ?? cfg.Password;
        cfg.AuthDatabase = Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_AUTH") ?? cfg.AuthDatabase;
        cfg.SocialDatabase = Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_SOCIAL") ?? cfg.SocialDatabase;
        cfg.TradingPostDatabase = Environment.GetEnvironmentVariable("MMONSTERPATCH_DB_TRADINGPOST") ?? cfg.TradingPostDatabase;
        return cfg;
    }
}

sealed class AccountStore
{
    public const string WebSessionCookieName = "mmp_web_session";
    private readonly DatabaseConfig _db;
    private readonly object _lock = new();

    public AccountStore(string repackRoot)
    {
        _db = DatabaseConfig.Load(repackRoot);
        EnsureDb();
    }

    public AccountResult Register(RegisterRequest req, string sourceIp)
    {
        lock (_lock)
        {
            req = req with
            {
                Email = NormalizeEmail(req.Email),
                Username = NormalizeUsername(req.Username),
                DisplayName = SanitizeDisplayName(req.DisplayName),
                Password = req.Password ?? ""
            };
            if (!IsValidEmail(req.Email)) return AccountResult.Fail("Enter a valid email address.");
            if (req.Username.Length < 3) return AccountResult.Fail("Username must be at least 3 characters.");
            if (req.DisplayName.Length < 2) return AccountResult.Fail("Display name must be at least 2 characters.");
            if (req.Password.Length < 8) return AccountResult.Fail("Password must be at least 8 characters.");

            using var con = OpenAuth();
            if (ScalarLong(con, "SELECT COUNT(*) FROM accounts WHERE lower(email)=lower(@v)", ("@v", req.Email)) > 0) return AccountResult.Fail("That email is already registered.");
            if (ScalarLong(con, "SELECT COUNT(*) FROM accounts WHERE lower(username)=lower(@v)", ("@v", req.Username)) > 0) return AccountResult.Fail("That username is already registered.");

            string accountId = "acct_" + Guid.NewGuid().ToString("N");
            var hash = PasswordHasher.Hash(req.Password);
            var account = new AccountRecord
            {
                AccountId = accountId,
                Email = req.Email,
                Username = req.Username,
                DisplayName = req.DisplayName,
                PasswordSalt = hash.Salt,
                PasswordHash = hash.Hash,
                PseudoSteamId = "mmpacct_" + accountId[5..],
                SteamVerified = false,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                LastLoginUtc = DateTime.UtcNow
            };
            InsertOrReplaceAccount(con, account);
            var session = CreateSession(con, account, sourceIp);
            return AccountResult.Ok(account, session, "Account created.");
        }
    }

    public AccountResult Login(LoginRequest req, string sourceIp)
    {
        lock (_lock)
        {
            string key = (req.UsernameOrEmail ?? "").Trim();
            string pw = req.Password ?? "";
            using var con = OpenAuth();
            var account = GetAccountByUsernameOrEmail(con, key);
            if (account is null || !PasswordHasher.Verify(pw, account.PasswordSalt, account.PasswordHash))
                return AccountResult.Fail("Invalid username/email or password.");
            account.LastLoginUtc = DateTime.UtcNow;
            account.UpdatedUtc = DateTime.UtcNow;
            InsertOrReplaceAccount(con, account);
            var session = CreateSession(con, account, sourceIp);
            return AccountResult.Ok(account, session, "Logged in.");
        }
    }

    public void RevokeSession(string token)
    {
        lock (_lock)
        {
            using var con = OpenAuth();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE launcher_sessions SET revoked_utc=@r WHERE session_token=@t OR aio_session_token=@t";
            cmd.Parameters.AddWithValue("@r", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@t", token ?? "");
            cmd.ExecuteNonQuery();
        }
    }

    public AccountSession? GetSessionFromRequest(HttpContext ctx)
    {
        string? auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return GetSession(auth[7..].Trim());
        return GetSessionFromCookie(ctx);
    }

    public AccountSession? GetSessionFromCookie(HttpContext ctx)
    {
        string? token = ctx.Request.Cookies[WebSessionCookieName];
        return string.IsNullOrWhiteSpace(token) ? null : GetSession(token);
    }

    public AccountSession? GetSession(string token)
    {
        lock (_lock)
        {
            using var con = OpenAuth();
            var s = GetSessionInternal(con, token);
            if (s is null || s.RevokedUtc is not null || s.ExpiresUtc <= DateTime.UtcNow) return null;
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE launcher_sessions SET last_seen_utc=@n WHERE session_token=@t OR aio_session_token=@t";
            cmd.Parameters.AddWithValue("@n", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@t", token ?? "");
            cmd.ExecuteNonQuery();
            return s;
        }
    }

    public AccountRecord? GetAccount(string accountId)
    {
        lock (_lock)
        {
            using var con = OpenAuth();
            return GetAccountById(con, accountId);
        }
    }

    public void WriteSessionCookie(HttpContext ctx, string token)
    {
        ctx.Response.Cookies.Append(WebSessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(14)
        });
    }

    public List<CharacterSummary> GetCharactersForAccount(AccountRecord account)
    {
        var list = new List<CharacterSummary>();
        try
        {
            using var con = OpenSocial();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                SELECT u.account_uuid, COALESCE(a.steam_id64,'') AS steam_id64, u.slot_index, u.character_id, u.display_name,
                       u.public_handle, u.status,
                       COALESCE(r.season_id,'season0') AS season_id, COALESCE(r.rp,0) AS rp, COALESCE(r.`rank`,'E') AS rank_code,
                       COALESCE(r.wins,0) AS wins, COALESCE(r.losses,0) AS losses, COALESCE(r.draws,0) AS draws,
                       COALESCE(r.highest_rp,0) AS highest_rp, COALESCE(r.highest_rank,'E') AS highest_rank
                FROM users u
                LEFT JOIN accounts a ON a.account_uuid = u.account_uuid
                LEFT JOIN ranked_profiles r ON r.character_id = u.character_id AND r.season_id = 'season0'
                WHERE u.status='active'
                  AND (a.steam_id64=@pseudo OR u.account_uuid=@account OR u.account_uuid=@pseudo)
                ORDER BY u.slot_index, u.display_name
            """;
            cmd.Parameters.AddWithValue("@pseudo", account.PseudoSteamId ?? "");
            cmd.Parameters.AddWithValue("@account", account.AccountId ?? "");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string pseudoOrSteam = Str(r, "steam_id64");
                list.Add(new CharacterSummary
                {
                    AccountId = pseudoOrSteam.StartsWith("mmpacct_", StringComparison.OrdinalIgnoreCase) ? "acct_" + pseudoOrSteam[8..] : (account.AccountId ?? ""),
                    AccountUuid = Str(r, "account_uuid"),
                    SteamId64 = pseudoOrSteam,
                    SlotIndex = (int)Long(r, "slot_index"),
                    CharacterId = Str(r, "character_id"),
                    DisplayName = Str(r, "display_name"),
                    PublicHandle = Str(r, "public_handle"),
                    Status = Str(r, "status"),
                    RankProfile = new CharacterRankProfile
                    {
                        SeasonId = Str(r, "season_id"),
                        SeasonName = "Season 0",
                        Rp = (int)Long(r, "rp"),
                        Rank = Str(r, "rank_code"),
                        Wins = (int)Long(r, "wins"),
                        Losses = (int)Long(r, "losses"),
                        Draws = (int)Long(r, "draws"),
                        HighestRp = (int)Long(r, "highest_rp"),
                        HighestRank = Str(r, "highest_rank")
                    }
                });
            }
        }
        catch { }
        return list;
    }

    public AccountUpdateResult UpdateAccountProfile(string accountId, string username, string displayName, string currentPassword, string newPassword, string confirmPassword)
    {
        displayName = SanitizeDisplayName(displayName);
        if (displayName.Length < 2) return AccountUpdateResult.Fail("Display name must be at least 2 characters.");
        if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 8) return AccountUpdateResult.Fail("New password must be at least 8 characters.");
        if (!string.Equals(newPassword ?? "", confirmPassword ?? "", StringComparison.Ordinal)) return AccountUpdateResult.Fail("New password confirmation does not match.");

        lock (_lock)
        {
            using var con = OpenAuth();
            var account = GetAccountById(con, accountId);
            if (account is null) return AccountUpdateResult.Fail("Account not found.");

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                if (string.IsNullOrWhiteSpace(currentPassword)) return AccountUpdateResult.Fail("Enter your current password before changing password.");
                if (!PasswordHasher.Verify(currentPassword, account.PasswordSalt, account.PasswordHash)) return AccountUpdateResult.Fail("Current password is incorrect.");
                var ph = PasswordHasher.Hash(newPassword);
                account.PasswordSalt = ph.Salt;
                account.PasswordHash = ph.Hash;
            }

            account.DisplayName = displayName;
            account.UpdatedUtc = DateTime.UtcNow;
            InsertOrReplaceAccount(con, account);

            using var sessions = con.CreateCommand();
            sessions.CommandText = "UPDATE launcher_sessions SET username=@username, display_name=@display WHERE account_id=@id";
            sessions.Parameters.AddWithValue("@username", account.Username);
            sessions.Parameters.AddWithValue("@display", account.DisplayName);
            sessions.Parameters.AddWithValue("@id", account.AccountId);
            sessions.ExecuteNonQuery();
            return AccountUpdateResult.Ok("Saved account changes.");
        }
    }

    private AccountSession CreateSession(MySqlConnection con, AccountRecord account, string sourceIp)
    {
        using (var cleanup = con.CreateCommand())
        {
            cleanup.CommandText = "DELETE FROM launcher_sessions WHERE expires_utc <= @now OR revoked_utc IS NOT NULL";
            cleanup.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            cleanup.ExecuteNonQuery();
        }

        var session = new AccountSession
        {
            SessionToken = "la_" + Token(),
            AioSessionToken = "aio_" + Token(),
            AccountId = account.AccountId,
            Username = account.Username,
            Email = account.Email,
            DisplayName = account.DisplayName,
            PseudoSteamId = account.PseudoSteamId,
            SourceIp = sourceIp ?? "",
            CreatedUtc = DateTime.UtcNow,
            LastSeenUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddHours(12)
        };
        InsertSession(con, session);
        return session;
    }

    private void EnsureDb()
    {
        using var con = OpenAuth();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
CREATE TABLE IF NOT EXISTS accounts (
  account_id VARCHAR(255) NOT NULL,
  email VARCHAR(255) NOT NULL,
  username VARCHAR(255) NOT NULL,
  display_name VARCHAR(255) NOT NULL,
  password_salt LONGTEXT NOT NULL,
  password_hash LONGTEXT NOT NULL,
  pseudo_steam_id VARCHAR(255) NOT NULL,
  steam_verified BIGINT NOT NULL DEFAULT 0,
  linked_steam_id64 LONGTEXT NULL,
  created_utc VARCHAR(64) NOT NULL,
  updated_utc VARCHAR(64) NOT NULL,
  last_login_utc VARCHAR(64) NULL,
  PRIMARY KEY (account_id),
  UNIQUE KEY uq_accounts_email (email),
  UNIQUE KEY uq_accounts_username (username),
  UNIQUE KEY uq_accounts_pseudo (pseudo_steam_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS launcher_sessions (
  session_token VARCHAR(255) NOT NULL,
  aio_session_token VARCHAR(255) NOT NULL,
  account_id VARCHAR(255) NOT NULL,
  username VARCHAR(255) NOT NULL,
  email VARCHAR(255) NOT NULL,
  display_name VARCHAR(255) NOT NULL,
  pseudo_steam_id LONGTEXT NOT NULL,
  source_ip LONGTEXT NOT NULL,
  created_utc VARCHAR(64) NOT NULL,
  last_seen_utc VARCHAR(64) NOT NULL,
  expires_utc VARCHAR(255) NOT NULL,
  revoked_utc VARCHAR(255) NULL,
  PRIMARY KEY (session_token),
  UNIQUE KEY uq_launcher_sessions_aio (aio_session_token),
  KEY idx_launcher_sessions_account (account_id, revoked_utc, expires_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS game_tickets (
  ticket_token VARCHAR(255) NOT NULL,
  account_id VARCHAR(255) NOT NULL,
  launcher_session_token LONGTEXT NOT NULL,
  created_utc VARCHAR(64) NOT NULL,
  expires_utc VARCHAR(64) NOT NULL,
  consumed_utc VARCHAR(64) NULL,
  PRIMARY KEY (ticket_token)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
""";
        cmd.ExecuteNonQuery();
    }

    private MySqlConnection OpenAuth()
    {
        var con = new MySqlConnection(_db.AuthConnectionString);
        con.Open();
        return con;
    }

    private MySqlConnection OpenSocial()
    {
        var con = new MySqlConnection(_db.SocialConnectionString);
        con.Open();
        return con;
    }

    private void InsertOrReplaceAccount(MySqlConnection con, AccountRecord a)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
INSERT INTO accounts(account_id,email,username,display_name,password_salt,password_hash,pseudo_steam_id,steam_verified,linked_steam_id64,created_utc,updated_utc,last_login_utc)
VALUES(@id,@email,@username,@display,@salt,@hash,@pseudo,@verified,@linked,@created,@updated,@last)
ON DUPLICATE KEY UPDATE
 email=VALUES(email), username=VALUES(username), display_name=VALUES(display_name), password_salt=VALUES(password_salt),
 password_hash=VALUES(password_hash), pseudo_steam_id=VALUES(pseudo_steam_id), steam_verified=VALUES(steam_verified),
 linked_steam_id64=VALUES(linked_steam_id64), updated_utc=VALUES(updated_utc), last_login_utc=VALUES(last_login_utc)
""";
        cmd.Parameters.AddWithValue("@id", a.AccountId);
        cmd.Parameters.AddWithValue("@email", a.Email);
        cmd.Parameters.AddWithValue("@username", a.Username);
        cmd.Parameters.AddWithValue("@display", a.DisplayName);
        cmd.Parameters.AddWithValue("@salt", a.PasswordSalt);
        cmd.Parameters.AddWithValue("@hash", a.PasswordHash);
        cmd.Parameters.AddWithValue("@pseudo", a.PseudoSteamId);
        cmd.Parameters.AddWithValue("@verified", a.SteamVerified ? 1 : 0);
        cmd.Parameters.AddWithValue("@linked", (object?)a.LinkedSteamId64 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", (a.CreatedUtc == default ? DateTime.UtcNow : a.CreatedUtc).ToString("o"));
        cmd.Parameters.AddWithValue("@updated", (a.UpdatedUtc == default ? DateTime.UtcNow : a.UpdatedUtc).ToString("o"));
        cmd.Parameters.AddWithValue("@last", a.LastLoginUtc?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void InsertSession(MySqlConnection con, AccountSession s)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
REPLACE INTO launcher_sessions(session_token,aio_session_token,account_id,username,email,display_name,pseudo_steam_id,source_ip,created_utc,last_seen_utc,expires_utc,revoked_utc)
VALUES(@session,@aio,@account,@username,@email,@display,@pseudo,@ip,@created,@seen,@expires,@revoked)
""";
        cmd.Parameters.AddWithValue("@session", s.SessionToken);
        cmd.Parameters.AddWithValue("@aio", s.AioSessionToken);
        cmd.Parameters.AddWithValue("@account", s.AccountId);
        cmd.Parameters.AddWithValue("@username", s.Username);
        cmd.Parameters.AddWithValue("@email", s.Email);
        cmd.Parameters.AddWithValue("@display", s.DisplayName);
        cmd.Parameters.AddWithValue("@pseudo", s.PseudoSteamId);
        cmd.Parameters.AddWithValue("@ip", s.SourceIp ?? "");
        cmd.Parameters.AddWithValue("@created", (s.CreatedUtc == default ? DateTime.UtcNow : s.CreatedUtc).ToString("o"));
        cmd.Parameters.AddWithValue("@seen", (s.LastSeenUtc == default ? DateTime.UtcNow : s.LastSeenUtc).ToString("o"));
        cmd.Parameters.AddWithValue("@expires", (s.ExpiresUtc == default ? DateTime.UtcNow.AddHours(12) : s.ExpiresUtc).ToString("o"));
        cmd.Parameters.AddWithValue("@revoked", s.RevokedUtc?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private AccountRecord? GetAccountByUsernameOrEmail(MySqlConnection con, string key)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM accounts WHERE lower(email)=lower(@k) OR lower(username)=lower(@k) LIMIT 1";
        cmd.Parameters.AddWithValue("@k", key);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadAccount(r) : null;
    }

    private AccountRecord? GetAccountById(MySqlConnection con, string id)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM accounts WHERE account_id=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id ?? "");
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadAccount(r) : null;
    }

    private AccountSession? GetSessionInternal(MySqlConnection con, string token)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT * FROM launcher_sessions WHERE session_token=@t OR aio_session_token=@t LIMIT 1";
        cmd.Parameters.AddWithValue("@t", token ?? "");
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadSession(r) : null;
    }

    private static long ScalarLong(MySqlConnection con, string sql, params (string, object)[] ps)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
    }

    private static AccountRecord ReadAccount(MySqlDataReader r) => new()
    {
        AccountId = Str(r, "account_id"),
        Email = Str(r, "email"),
        Username = Str(r, "username"),
        DisplayName = Str(r, "display_name"),
        PasswordSalt = Str(r, "password_salt"),
        PasswordHash = Str(r, "password_hash"),
        PseudoSteamId = Str(r, "pseudo_steam_id"),
        SteamVerified = Long(r, "steam_verified") != 0,
        LinkedSteamId64 = NullStr(r, "linked_steam_id64"),
        CreatedUtc = Date(Str(r, "created_utc")),
        UpdatedUtc = Date(Str(r, "updated_utc")),
        LastLoginUtc = DateNullable(NullStr(r, "last_login_utc"))
    };

    private static AccountSession ReadSession(MySqlDataReader r) => new()
    {
        SessionToken = Str(r, "session_token"),
        AioSessionToken = Str(r, "aio_session_token"),
        AccountId = Str(r, "account_id"),
        Username = Str(r, "username"),
        Email = Str(r, "email"),
        DisplayName = Str(r, "display_name"),
        PseudoSteamId = Str(r, "pseudo_steam_id"),
        SourceIp = Str(r, "source_ip"),
        CreatedUtc = Date(Str(r, "created_utc")),
        LastSeenUtc = Date(Str(r, "last_seen_utc")),
        ExpiresUtc = Date(Str(r, "expires_utc")),
        RevokedUtc = DateNullable(NullStr(r, "revoked_utc"))
    };

    private static string Str(MySqlDataReader r, string name) => r[name] == DBNull.Value ? "" : Convert.ToString(r[name]) ?? "";
    private static string? NullStr(MySqlDataReader r, string name) => r[name] == DBNull.Value ? null : Convert.ToString(r[name]);
    private static long Long(MySqlDataReader r, string name) => r[name] == DBNull.Value ? 0 : Convert.ToInt64(r[name]);
    private static DateTime Date(string value) => DateNullable(value) ?? DateTime.UtcNow;
    private static DateTime? DateNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value.Replace("Z", "+00:00"), out var dt)) return dt.ToUniversalTime();
        return null;
    }

    private static string Token()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string NormalizeEmail(string? s) => (s ?? "").Trim().ToLowerInvariant();
    private static string NormalizeUsername(string? s) => Regex.Replace((s ?? "").Trim(), "[^A-Za-z0-9_.-]", "");
    private static string SanitizeDisplayName(string? s)
    {
        string v = Regex.Replace((s ?? "").Trim(), "[^A-Za-z0-9 _.-]", "");
        return string.IsNullOrWhiteSpace(v) ? "Player" : v.Length > 32 ? v[..32] : v;
    }
    private static bool IsValidEmail(string s) => s.Contains('@') && s.Contains('.') && s.Length <= 255;
}

sealed class AccountRecord
{
    public string AccountId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PseudoSteamId { get; set; } = "";
    public bool SteamVerified { get; set; }
    public string? LinkedSteamId64 { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public object ToPublic() => new { accountId = AccountId, email = Email, username = Username, displayName = DisplayName, pseudoSteamId = PseudoSteamId, steamVerified = SteamVerified, linkedSteamId64 = LinkedSteamId64 };
}

sealed class AccountSession
{
    public string SessionToken { get; set; } = "";
    public string AioSessionToken { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PseudoSteamId { get; set; } = "";
    public string SourceIp { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public object ToPublic() => new { sessionToken = SessionToken, aioSessionToken = AioSessionToken, accountId = AccountId, username = Username, displayName = DisplayName, pseudoSteamId = PseudoSteamId, expiresUtc = ExpiresUtc };
}

sealed class AccountUpdateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public static AccountUpdateResult Fail(string msg) => new() { Success = false, Message = msg };
    public static AccountUpdateResult Ok(string msg) => new() { Success = true, Message = msg };
}

sealed class AccountResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public AccountRecord? Account { get; set; }
    public AccountSession? Session { get; set; }
    public static AccountResult Fail(string msg) => new() { Success = false, Message = msg };
    public static AccountResult Ok(AccountRecord account, AccountSession session, string msg) => new() { Success = true, Message = msg, Account = account, Session = session };
    public object ToApi() => new
    {
        success = Success,
        message = Message,
        account = Account?.ToPublic(),
        session = Session?.ToPublic()
    };
}

sealed class CharacterSummary
{
    public string AccountId { get; set; } = "";
    public string AccountUuid { get; set; } = "";
    public string SteamId64 { get; set; } = "";
    public int SlotIndex { get; set; }
    public string CharacterId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PublicHandle { get; set; } = "";
    public string Status { get; set; } = "";
    public CharacterRankProfile? RankProfile { get; set; }
}

sealed class CharacterRankProfile
{
    public string SeasonId { get; set; } = "season0";
    public string SeasonName { get; set; } = "Season 0";
    public string Rank { get; set; } = "E";
    public int Rp { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int HighestRp { get; set; }
    public string HighestRank { get; set; } = "E";
}

static class PasswordHasher
{
    public static (string Salt, string Hash) Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string saltB64, string hashB64)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(saltB64);
            byte[] expected = Convert.FromBase64String(hashB64);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}
