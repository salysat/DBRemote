using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name="DBRemote.Session";
    options.Cookie.HttpOnly=true;
    options.Cookie.SameSite=SameSiteMode.Strict;
    options.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan=TimeSpan.FromHours(8);
    options.SlidingExpiration=true;
    options.Events.OnRedirectToLogin=ctx=> {ctx.Response.StatusCode=401;return Task.CompletedTask;};
    options.Events.OnRedirectToAccessDenied=ctx=> {ctx.Response.StatusCode=403;return Task.CompletedTask;};
});
var keyStore=Path.Combine(builder.Environment.ContentRootPath,"data","dataprotection");
Directory.CreateDirectory(keyStore);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyStore)).SetApplicationName("DBRemote");
var app = builder.Build();
var adminToken = Environment.GetEnvironmentVariable("DBREMOTE_ADMIN_TOKEN");
var agentToken = Environment.GetEnvironmentVariable("DBREMOTE_AGENT_TOKEN");
var adminPassword = Environment.GetEnvironmentVariable("DBREMOTE_ADMIN_PASSWORD");
if (string.IsNullOrWhiteSpace(adminToken) || string.IsNullOrWhiteSpace(agentToken) || adminToken.Length < 24 || agentToken.Length < 24 || adminToken == agentToken)
    throw new InvalidOperationException("Configure DIFFERENT DBREMOTE_ADMIN_TOKEN and DBREMOTE_AGENT_TOKEN (minimum 24 characters each).");
if(string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 16)
    throw new InvalidOperationException("Set DBREMOTE_ADMIN_PASSWORD (16+ characters) using setup-local.ps1.");
app.UseAuthentication();
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
var agents = new ConcurrentDictionary<string, Peer>();
var viewers = new ConcurrentDictionary<string, ConcurrentDictionary<string, Peer>>();
var previews = new ConcurrentDictionary<string,(byte[] Data,DateTimeOffset At)>();
// Keep the previous data location for upgrade compatibility. The folder and registry
// file are created on first startup, without touching existing device approvals.
var registryPath = Environment.GetEnvironmentVariable("DBREMOTE_DEVICES_PATH");
if (string.IsNullOrWhiteSpace(registryPath))
    registryPath = Path.Combine(builder.Environment.ContentRootPath, "data", "devices.json");
var registry = new DeviceRegistry(registryPath, agentToken!);
// Independent metadata storage. Existing device registrations remain untouched.
var studio = new StudioStore(Path.Combine(builder.Environment.ContentRootPath,"data","studio.json"));

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
static bool SecureEquals(string a,string b) => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(a)),System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(b)));
bool IsAdmin(HttpContext ctx) => ctx.User.Identity?.IsAuthenticated==true;
// Browser-origin and custom-header requirement protects state-changing cookie-authenticated endpoints.
bool IsSafeWrite(HttpContext ctx) {
    if(ctx.Request.Headers["X-DBRemote-Request"]!="1") return false;
    if(!ctx.Request.Headers.TryGetValue("Origin",out var origin))return false;
    if(!Uri.TryCreate(origin.ToString(),UriKind.Absolute,out var o))return false;
    return string.Equals(o.Host,ctx.Request.Host.Host,StringComparison.OrdinalIgnoreCase) && o.Port==ctx.Request.Host.Port.GetValueOrDefault(ctx.Request.IsHttps?443:80) && o.Scheme==(ctx.Request.IsHttps?"https":"http");
}
var failures=new ConcurrentDictionary<string,(int Count,DateTimeOffset Until)>();
var tickets=new ConcurrentDictionary<string,(string Device,DateTimeOffset Expires)>();
app.MapGet("/api/auth/me",(HttpContext ctx)=>IsAdmin(ctx)?Results.Ok(new {authenticated=true,user="admin"}):Results.Unauthorized());
app.MapPost("/api/auth/login",async (HttpContext ctx,LoginRequest req)=>{
    if(!IsLocalOrTls(ctx)||!IsSafeWrite(ctx))return Results.StatusCode(403);
    string ip=ctx.Connection.RemoteIpAddress?.ToString()??"unknown";
    if(failures.TryGetValue(ip,out var f) && f.Count>=5 && f.Until>DateTimeOffset.UtcNow)return Results.StatusCode(429);
    if(!SecureEquals(req.Password??"",adminPassword!)) {
        failures.AddOrUpdate(ip,_=>(1,DateTimeOffset.UtcNow.AddMinutes(10)),(_,old)=>(old.Until<DateTimeOffset.UtcNow?1:old.Count+1,DateTimeOffset.UtcNow.AddMinutes(10)));
        return Results.Unauthorized();
    }
    failures.TryRemove(ip,out _);
    var identity=new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"admin")},CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(identity),new AuthenticationProperties{IsPersistent=false});
    return Results.Ok(new {authenticated=true});
});
app.MapPost("/api/auth/logout",async (HttpContext ctx)=>{
    if(!IsSafeWrite(ctx))return Results.StatusCode(403);
    await ctx.SignOutAsync();return Results.Ok(new {ok=true});
});
app.MapPost("/api/viewer-ticket/{device}",(HttpContext ctx,string device)=>{
    if(!IsAdmin(ctx)||!IsSafeWrite(ctx))return Results.Unauthorized();
    if(!System.Text.RegularExpressions.Regex.IsMatch(device,"^[a-zA-Z0-9_-]{3,64}$"))return Results.BadRequest();
    var t=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    tickets[t]=(device,DateTimeOffset.UtcNow.AddSeconds(30));
    return Results.Ok(new {ticket=t});
});
bool IsLocalOrTls(HttpContext ctx) => ctx.Request.IsHttps || System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? System.Net.IPAddress.None);
app.MapPost("/api/enroll", (HttpContext ctx, EnrollmentRequest req) => {
    if(!IsLocalOrTls(ctx))return Results.StatusCode(403);
    if(!System.Text.RegularExpressions.Regex.IsMatch(req.DeviceId??"", "^[a-zA-Z0-9_-]{3,64}$"))return Results.BadRequest();
    if(string.IsNullOrWhiteSpace(req.Claim) || req.Claim.Length<40 || req.Claim.Length>160)return Results.BadRequest();
    return registry.Request(req.DeviceId!, req.Claim!, req.Name??req.DeviceId!);
});
app.MapPost("/api/enroll/check", (HttpContext ctx, EnrollmentRequest req) => {
    if(!IsLocalOrTls(ctx))return Results.StatusCode(403);
    return registry.Check(req.DeviceId??"",req.Claim??"");
});
app.MapGet("/api/devices",(HttpContext ctx) => IsAdmin(ctx) ? Results.Ok(registry.List(agents.Keys)) : Results.Unauthorized());
app.MapGet("/api/devices/{id}/preview",(HttpContext ctx,string id) => {
    if(!IsAdmin(ctx))return Results.Unauthorized();
    if(!agents.ContainsKey(id)||!previews.TryGetValue(id,out var preview)||DateTimeOffset.UtcNow-preview.At>TimeSpan.FromSeconds(45))return Results.NotFound();
    ctx.Response.Headers.CacheControl="no-store";
    return Results.File(preview.Data,"image/jpeg");
});
app.MapPost("/api/devices/{id}/approve",(HttpContext ctx,string id) => IsAdmin(ctx)&&IsSafeWrite(ctx)?registry.Approve(id):Results.Unauthorized());
app.MapPost("/api/devices/{id}/revoke",async (HttpContext ctx,string id) => {
    if(!IsAdmin(ctx)||!IsSafeWrite(ctx))return Results.Unauthorized();
    var result=registry.Revoke(id);
    if(agents.TryRemove(id,out var peer)) await peer.Disconnect();
    return result;
});

app.MapGet("/api/studio",(HttpContext ctx) => IsAdmin(ctx) ? Results.Ok(studio.Snapshot()) : Results.Unauthorized());
app.MapPut("/api/studio/groups",(HttpContext ctx, GroupUpdate req) => {
    if (!IsAdmin(ctx)||!IsSafeWrite(ctx)) return Results.Unauthorized();
    if (req.Groups is null || req.Groups.Length>40 || req.Groups.Any(g=>string.IsNullOrWhiteSpace(g)||g.Length>48))return Results.BadRequest();
    return Results.Ok(studio.SetGroups(req.Groups));
});
app.MapPut("/api/studio/device/{id}/group",(HttpContext ctx,string id,DeviceGroupUpdate req) => {
    if(!IsAdmin(ctx)||!IsSafeWrite(ctx))return Results.Unauthorized();
    if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-zA-Z0-9_-]{3,64}$")||req.Group is null||req.Group.Length>48)return Results.BadRequest();
    return Results.Ok(studio.Assign(id,req.Group));
});
app.MapPut("/api/studio/branding",(HttpContext ctx,BrandingUpdate req) => {
    if(!IsAdmin(ctx)||!IsSafeWrite(ctx))return Results.Unauthorized();
    if(string.IsNullOrWhiteSpace(req.Title)||req.Title.Length>60||!System.Text.RegularExpressions.Regex.IsMatch(req.Accent??"","^#[0-9A-Fa-f]{6}$"))return Results.BadRequest();
    return Results.Ok(studio.SetBranding(req.Title,req.Accent!));
});
app.MapGet("/api/studio/history",(HttpContext ctx) => IsAdmin(ctx)?Results.Ok(studio.History()):Results.Unauthorized());
app.MapGet("/health", () => Results.Ok(new { status="ok", agents=agents.Count }));
app.Map("/ws/{role}/{device}", async (HttpContext context, string role, string device) =>
{
    if (!context.WebSockets.IsWebSocketRequest || (role != "agent" && role != "viewer") || !System.Text.RegularExpressions.Regex.IsMatch(device, "^[a-zA-Z0-9_-]{3,64}$")) { context.Response.StatusCode=400; return; }
    // Only accept TLS except on loopback. Configure HTTPS reverse proxy and ForwardedHeaders with trusted proxies in production.
    if (!context.Request.IsHttps && !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? System.Net.IPAddress.None)) { context.Response.StatusCode=403; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var hello = await Receive(socket, context.RequestAborted);
    string? provided = null;
    try { using var doc=JsonDocument.Parse(hello ?? "{}"); provided=doc.RootElement.GetProperty("token").GetString(); } catch { }
    var valid = provided is not null && (role == "agent" ? registry.Authenticate(device,provided) : tickets.TryRemove(provided,out var ticket) && ticket.Device==device && ticket.Expires>DateTimeOffset.UtcNow);
    if (!valid)
    {
        // Use hash comparison with constant-size values for token validation.
        try { await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication failed", CancellationToken.None); } catch (WebSocketException) { } return;
    }
    var peer=new Peer(socket);
    if (role=="agent")
    {
        if (!agents.TryAdd(device,peer)) { await peer.Send("{\"type\":\"error\",\"message\":\"Device already connected\"}"); return; }
        await peer.Send("{\"type\":\"registered\"}");
        studio.AddEvent("agent-connected",device);
    }
    else
    {
        if (!agents.ContainsKey(device)) { await peer.Send("{\"type\":\"error\",\"message\":\"Device offline\"}"); return; }
        viewers.GetOrAdd(device,_=>new()).TryAdd(peer.Id,peer);
        await peer.Send("{\"type\":\"connected\"}");
        studio.AddEvent("viewer-connected",device);
        if (agents.TryGetValue(device,out var a)) await a.Send(JsonSerializer.Serialize(new { type="viewer-joined", viewer=peer.Id }));
    }
    try
    {
        while (socket.State==WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            var msg = await Receive(socket,context.RequestAborted);
            if (msg is null) break;
            using var doc = JsonDocument.Parse(msg);
            var root=doc.RootElement;
            if (!root.TryGetProperty("type",out var typeProp)) continue;
            var type=typeProp.GetString();
            if (type=="hello" || type=="registered") continue;
            if (role=="viewer")
            {
                if (type is not ("mouse" or "key" or "text" or "chat" or "clipboard-set" or "clipboard-get" or "file-start" or "file-chunk" or "file-end" or "screen-config")) continue;
                if (agents.TryGetValue(device,out var a))
                {
                    // Only the newest active viewer may control; isolate messages per device.
                    var envelope=JsonSerializer.Serialize(new { type="forward", viewer=peer.Id, payload=root.Clone() });
                    await a.Send(envelope);
                }
            }
            else
            {
                if(type=="preview")
                {
                    if(root.TryGetProperty("data",out var imageData) && imageData.ValueKind==JsonValueKind.String)
                    {
                        var b64=imageData.GetString();
                        if(b64 is not null && b64.Length<=240_000)
                        {
                            try { var bytes=Convert.FromBase64String(b64);if(bytes.Length>4 && bytes.Length<180_000 && bytes[0]==0xFF && bytes[1]==0xD8) previews[device]=(bytes,DateTimeOffset.UtcNow); } catch(FormatException) { }
                        }
                    }
                    continue;
                }
                if (type is not ("frame" or "clipboard" or "chat" or "file-start" or "file-chunk" or "file-end" or "status" or "error")) continue;
                if (root.TryGetProperty("viewer",out var v) && v.ValueKind==JsonValueKind.String)
                {
                    var target=v.GetString();
                    if (target is not null && viewers.TryGetValue(device,out var map) && map.TryGetValue(target,out var dest)) await dest.Send(msg);
                }
                else if (viewers.TryGetValue(device,out var map)) foreach(var target in map.Values) await target.Send(msg);
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException) { }
    catch (JsonException) { }
    finally
    {
        if (role=="agent") { previews.TryRemove(device,out _);studio.AddEvent("agent-disconnected",device); if(agents.TryGetValue(device,out var current) && ReferenceEquals(current,peer))agents.TryRemove(device,out _); if(viewers.TryGetValue(device,out var map)) foreach(var v in map.Values) await v.Send("{\"type\":\"error\",\"message\":\"Device disconnected\"}"); }
        else if(viewers.TryGetValue(device,out var map)) { studio.AddEvent("viewer-disconnected",device); map.TryRemove(peer.Id,out _); if(agents.TryGetValue(device,out var a)) await a.Send(JsonSerializer.Serialize(new {type="viewer-left",viewer=peer.Id})); }
    }
});
app.Run();

static async Task<string?> Receive(WebSocket ws,CancellationToken ct)
{
    using var buffer = new MemoryStream();
    var chunk=new byte[65536];
    WebSocketReceiveResult result;
    do { result=await ws.ReceiveAsync(new ArraySegment<byte>(chunk),ct); if(result.MessageType==WebSocketMessageType.Close)return null; buffer.Write(chunk,0,result.Count); if(buffer.Length > 4*1024*1024)throw new WebSocketException("Message too large"); } while(!result.EndOfMessage);
    return Encoding.UTF8.GetString(buffer.ToArray());
}
sealed class Peer(WebSocket socket)
{
    readonly SemaphoreSlim sendLock=new(1,1);
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public async Task Disconnect()
    {
        try { if(socket.State==WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation,"Device revoked",CancellationToken.None); }
        catch(WebSocketException) { }
    }
    public async Task Send(string message)
    {
        if(socket.State!=WebSocketState.Open)return;
        await sendLock.WaitAsync();
        try { if(socket.State==WebSocketState.Open)await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),WebSocketMessageType.Text,true,CancellationToken.None); }
        catch(WebSocketException){} finally {sendLock.Release();}
    }
}

record LoginRequest(string? Password);
record EnrollmentRequest(string? DeviceId,string? Claim,string? Name);
sealed class DeviceRecord
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Status {get;set;}="pending";
    public string ClaimHash {get;set;}="";
    public string TokenHash {get;set;}="";
    public DateTimeOffset Created {get;set;}=DateTimeOffset.UtcNow;
}
sealed class DeviceRegistry
{
    readonly object gate=new();
    readonly string file;
    readonly byte[] signingKey;
    readonly Dictionary<string,DeviceRecord> devices;
    public DeviceRegistry(string path,string secret)
    {
        file=Path.GetFullPath(path);
        signingKey=Encoding.UTF8.GetBytes(secret);
        // The previous version tried to read data/devices.json before its parent existed.
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (!File.Exists(file))
        {
            // Create a valid EMPTY DICTIONARY, not [] (the registry uses device ID keys).
            // CreateNew avoids overwriting an existing registry if startup races.
            try
            {
                using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write("{}");
            }
            catch (IOException) when (File.Exists(file)) { }
        }
        try
        {
            devices=JsonSerializer.Deserialize<Dictionary<string,DeviceRecord>>(File.ReadAllText(file))
                ?? throw new InvalidDataException("Device registry must be a JSON object, not null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid device registry at '{file}'. Back up and repair it; existing registrations were NOT erased.", ex);
        }
    }
    string DeviceToken(string id)=>Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(signingKey,Encoding.UTF8.GetBytes("DBRemote-device-v1:"+id)));
    static string Hash(string value)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp=file+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(devices),Encoding.UTF8);
        File.Move(temp,file,true);
    }
    public IResult Request(string id,string claim,string name)
    {
        lock(gate)
        {
            if(devices.TryGetValue(id,out var current))
                {
                    // Re-enrollment is permitted ONLY for the same device holding its original claim.
                    if (current.ClaimHash!=Hash(claim)) return Results.Conflict(new {error="Device ID belongs to another installation"});
                    if (current.Status=="revoked")
                    {
                        current.Status="pending";
                        current.TokenHash="";
                        current.Created=DateTimeOffset.UtcNow;
                        current.Name=name[..Math.Min(name.Length,100)];
                        Save();
                    }
                    return Results.Ok(new {status=current.Status});
                }
            if(devices.Values.Count(x=>x.Status=="pending")>=100)return Results.StatusCode(429);
            devices[id]=new DeviceRecord{Id=id,Name=name[..Math.Min(name.Length,100)],ClaimHash=Hash(claim)};
            Save();return Results.Ok(new {status="pending"});
        }
    }
    public IResult Check(string id,string claim)
    {
        lock(gate)
        {
            if(!devices.TryGetValue(id,out var d) || d.ClaimHash!=Hash(claim))return Results.NotFound();
            if(d.Status=="revoked")return Results.Ok(new {status="revoked"});
            if(d.Status=="pending")return Results.Ok(new {status="pending"});
            // Credential is delivered only to client holding its original random claim.
            return Results.Ok(new {status="approved",token=DeviceToken(id)});
        }
    }
    public object List(IEnumerable<string> online)
    {
        lock(gate) {var active=online.ToHashSet();return devices.Values.Select(d=>new {id=d.Id,name=d.Name,status=d.Status,online=active.Contains(d.Id),created=d.Created}).OrderByDescending(d=>d.created).ToArray();}
    }
    public IResult Approve(string id)
    {
        lock(gate)
        {
            if(!devices.TryGetValue(id,out var d) || d.Status!="pending")return Results.NotFound();
            var token=DeviceToken(id);
            d.TokenHash=Hash(token);d.Status="approved";Save();
            return Results.Ok(new {status="approved"});
        }
    }
    public IResult Revoke(string id)
    {
        lock(gate)
        {
            if(!devices.TryGetValue(id,out var d))return Results.NotFound();
            d.Status="revoked";d.TokenHash="";Save();return Results.Ok(new {status="revoked"});
        }
    }
    public bool Authenticate(string id,string token)
    {
        lock(gate) return devices.TryGetValue(id,out var d) && d.Status=="approved" && d.TokenHash==Hash(token);
    }
}

record GroupUpdate(string[]? Groups);
record DeviceGroupUpdate(string? Group);
record BrandingUpdate(string? Title,string? Accent);
sealed class StudioState
{
    public List<string> Groups {get;set;} = new(){"الافتراضي"};
    public Dictionary<string,string> DeviceGroups {get;set;} = new();
    public string Title {get;set;}="DB REMOTE";
    public string Accent {get;set;}="#1768B0";
    public List<StudioEvent> Events {get;set;} = new();
}
record StudioEvent(DateTimeOffset At,string Type,string DeviceId);
sealed class StudioStore
{
    readonly object gate=new(); readonly string path; StudioState state;
    public StudioStore(string file)
    {
        path=file;Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Never overwrite corrupted configuration silently.
        state=File.Exists(path) ? JsonSerializer.Deserialize<StudioState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid studio state") : new StudioState();
    }
    void Save(){var tmp=path+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(state));File.Move(tmp,path,true);}
    public object Snapshot(){lock(gate)return new { groups=state.Groups.ToArray(),deviceGroups=new Dictionary<string,string>(state.DeviceGroups),title=state.Title,accent=state.Accent};}
    public object SetGroups(string[] groups){lock(gate){state.Groups=groups.Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(!state.Groups.Contains("الافتراضي"))state.Groups.Insert(0,"الافتراضي");Save();return Snapshot();}}
    public object Assign(string id,string group){lock(gate){if(!state.Groups.Contains(group))return new {success=false};state.DeviceGroups[id]=group;Save();return new {success=true};}}
    public object SetBranding(string title,string accent){lock(gate){state.Title=title;state.Accent=accent;Save();return Snapshot();}}
    public void AddEvent(string type,string device){lock(gate){state.Events.Add(new StudioEvent(DateTimeOffset.UtcNow,type,device));if(state.Events.Count>3000)state.Events.RemoveRange(0,state.Events.Count-3000);Save();}}
    public StudioEvent[] History(){lock(gate)return state.Events.TakeLast(300).Reverse().ToArray();}
}
