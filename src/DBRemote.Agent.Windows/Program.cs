using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

var server=Environment.GetEnvironmentVariable("DBREMOTE_SERVER")??"ws://localhost:5080";
var device=Environment.GetEnvironmentVariable("DBREMOTE_DEVICE_ID")??Environment.MachineName.ToLowerInvariant().Replace(" ","-");
var token=Environment.GetEnvironmentVariable("DBREMOTE_AGENT_TOKEN");
if(string.IsNullOrWhiteSpace(token)||token.Length<24)throw new Exception("DBREMOTE_AGENT_TOKEN must have at least 24 characters");
if(!System.Text.RegularExpressions.Regex.IsMatch(device,"^[a-zA-Z0-9_-]{3,64}$"))throw new Exception("Invalid DBREMOTE_DEVICE_ID");
if(!server.StartsWith("wss://",StringComparison.OrdinalIgnoreCase)&&!server.StartsWith("ws://localhost",StringComparison.OrdinalIgnoreCase)&&!server.StartsWith("ws://127.0.0.1",StringComparison.OrdinalIgnoreCase))throw new Exception("Remote connections require wss://");
var agent=new RemoteAgent(server,device,token);
Console.WriteLine($"DB Remote Agent: {device} => {server}");
Console.WriteLine("Remote control enabled on this device. CTRL+C to stop.");
using var stop=new CancellationTokenSource();
Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;stop.Cancel();};
var run=agent.Run(stop.Token);
var commands=Task.Run(async ()=>
{
    while(!stop.IsCancellationRequested)
    {
        var line=Console.ReadLine();
        if(line==null)break;
        if(line.StartsWith("send ",StringComparison.OrdinalIgnoreCase))
        {
            try{await agent.SendFile(line[5..].Trim().Trim('"'),stop.Token);}catch(Exception ex){Console.WriteLine("Send error: "+ex.Message);}
        }
    }
});
Console.WriteLine("Type: send C:\\path\\file.txt to send a file to connected viewer.");
await run;

sealed class RemoteAgent(string server,string device,string token)
{
    readonly ConcurrentDictionary<string,IncomingFile> files=new();
    readonly ConcurrentDictionary<string,byte> viewers=new();
    readonly SemaphoreSlim sendLock=new(1,1);
    ClientWebSocket? socket;
    int fps=5;
    readonly bool previewsEnabled=Environment.GetEnvironmentVariable("DBREMOTE_ALLOW_PREVIEWS")=="1";
    DateTimeOffset lastPreview=DateTimeOffset.MinValue;
    string? activeViewer;
    const int MaxFileBytes=1024*1024*1024; // 1 GiB per transfer in this reference implementation
    public async Task Run(CancellationToken stop)
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var ws=new ClientWebSocket();socket=ws;
                await ws.ConnectAsync(new Uri(server.TrimEnd('/')+"/ws/agent/"+device),stop);
                await Send(new {token});
                Console.WriteLine("Connected. Waiting for controller.");
                using var local=CancellationTokenSource.CreateLinkedTokenSource(stop);
                var capture=CaptureLoop(local.Token);
                var bridge=BridgeLoop(local.Token);
                try
                {
                    while(ws.State==WebSocketState.Open && !stop.IsCancellationRequested)
                    {
                        string? msg=await Receive(ws,stop);if(msg==null)break;
                        using var doc=JsonDocument.Parse(msg);var m=doc.RootElement;
                        var kind=String(m,"type");
                        if(kind=="viewer-joined") { var v=String(m,"viewer");if(v!=null){viewers.TryAdd(v,0);activeViewer ??= v;Console.WriteLine("Viewer connected");} }
                        else if(kind=="viewer-left") {var v=String(m,"viewer");if(v!=null){viewers.TryRemove(v,out _);if(activeViewer==v)activeViewer=viewers.Keys.FirstOrDefault();} }
                        else if(kind=="forward")
                        {
                            var v=String(m,"viewer");if(v==null||!viewers.ContainsKey(v))continue;
                            // One controller at a time; newer viewer becomes active only when previous leaves.
                            if(activeViewer!=v)continue;
                            await Handle(m.GetProperty("payload"),v);
                        }
                    }
                }
                finally {local.Cancel();try{await Task.WhenAll(capture, bridge);}catch(OperationCanceledException){} }
            }
            catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
            catch(Exception e){Console.WriteLine("Disconnected: "+e.Message);}
            finally {socket=null;activeViewer=null;viewers.Clear();foreach(var f in files.Values)f.Dispose();files.Clear();}
            try{await Task.Delay(3000,stop);}catch(OperationCanceledException){break;}
        }
    }
    public async Task SendFile(string path,CancellationToken ct)
    {
        if(!File.Exists(path))throw new FileNotFoundException("Not found",path);
        var fileInfo=new FileInfo(path);
        if(fileInfo.Length>MaxFileBytes)throw new Exception("File exceeds 1 GiB demo limit");
        if(viewers.Count==0)throw new Exception("No connected viewer");
        var id=Guid.NewGuid().ToString("N");
        await Send(new {type="file-start",id,name=fileInfo.Name,size=fileInfo.Length});
        await using var fs=File.OpenRead(path);var buffer=new byte[24*1024];int read;
        while((read=await fs.ReadAsync(buffer,ct))>0)
        {
            await Send(new {type="file-chunk",id,data=Convert.ToBase64String(buffer,0,read)});
            await Task.Delay(4,ct);
        }
        await Send(new {type="file-end",id});
        Console.WriteLine("Sent file: "+fileInfo.Name);
    }
    async Task Handle(JsonElement m,string v)
    {
        switch(String(m,"type"))
        {
            case "screen-config": fps=Math.Clamp(Int(m,"fps",5),1,10);break;
            case "mouse": Input.Mouse(m);break;
            case "key": Input.Key(m);break;
            case "text": Input.Text(String(m,"text")??"");break;
            case "chat":
                {
                    var txt=String(m,"text")??"";
                    Console.WriteLine("CHAT|" + v + "|" + txt.Replace('\r', ' ').Replace('\n', ' '));
                    await Send(new {type="chat",viewer=v,from="agent",text="تم استلام رسالتك: "+txt});
                    break;
                }
            case "clipboard-get": await Send(new {type="clipboard",viewer=v,text=ClipboardWorker.Get()});break;
            case "clipboard-set": ClipboardWorker.Set(String(m,"text")??"");break;
            case "file-start":
                {
                    var id=String(m,"id");var name=String(m,"name");var size=Long(m,"size");
                    if(id==null||name==null||size<0||size>MaxFileBytes||files.ContainsKey(id))break;
                    var safe=Path.GetFileName(name);if(string.IsNullOrWhiteSpace(safe)||safe.IndexOfAny(Path.GetInvalidFileNameChars())>=0)break;
                    var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","DBRemote Incoming");Directory.CreateDirectory(dir);
                    // Avoid overwriting an existing file and avoid partial files mistaken as complete.
                    var path=Path.Combine(dir,Guid.NewGuid().ToString("N")+"-"+safe+".partial");
                    files.TryAdd(id,new IncomingFile(path,size,v));break;
                }
            case "file-chunk":
                {
                    var id=String(m,"id");if(id==null||!files.TryGetValue(id,out var f)||f.Viewer!=v)break;
                    try {var bytes=Convert.FromBase64String(String(m,"data")??"");if(bytes.Length>32*1024||f.Received+bytes.Length>f.Expected){if(files.TryRemove(id,out var bad))bad.Dispose();break;}await f.Write(bytes);}
                    catch(FormatException){if(files.TryRemove(id,out var bad))bad.Dispose();}break;
                }
            case "file-end":
                {
                    var id=String(m,"id");if(id==null||!files.TryRemove(id,out var f))break;
                    if(f.Viewer==v&&f.Received==f.Expected){var done=f.Path[..^".partial".Length];f.Dispose();File.Move(f.Path,done);AgentBridge.AppendEvent(new BridgeEvent("file-in", done)); await Send(new {type="status",viewer=v,message="File saved: "+Path.GetFileName(done)});}else f.Dispose();break;
                }
        }
    }
    async Task CaptureLoop(CancellationToken stop)
    {
        while(!stop.IsCancellationRequested)
        {
            var doPreview=previewsEnabled && DateTimeOffset.UtcNow-lastPreview>=TimeSpan.FromSeconds(15);
            if((viewers.Count>0 || doPreview)&&socket?.State==WebSocketState.Open)
            {
                try
                {
                    var bounds=SystemInformation.VirtualScreen;
                    using var bmp=new Bitmap(bounds.Width,bounds.Height);
                    using(var graphics=Graphics.FromImage(bmp))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bounds.Size);
                    using var ms=new MemoryStream();
                    var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");
                    using var pars=new EncoderParameters(1);pars.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,55L);
                    bmp.Save(ms,codec,pars);
                    // Preview is privacy opt-in; coarse thumbnail at most once / 15 seconds.
                    if(doPreview)
                    {
                        lastPreview=DateTimeOffset.UtcNow;
                        using var thumb=new Bitmap(480,Math.Max(1,(int)(480.0*bounds.Height/bounds.Width)));
                        using(var g=Graphics.FromImage(thumb))g.DrawImage(bmp,0,0,thumb.Width,thumb.Height);
                        using var thumbBytes=new MemoryStream();
                        using var thumbQuality=new EncoderParameters(1);
                        thumbQuality.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,35L);
                        thumb.Save(thumbBytes,codec,thumbQuality);
                        if(thumbBytes.Length<180_000)await Send(new {type="preview",data=Convert.ToBase64String(thumbBytes.ToArray())});
                    }
                    if(viewers.Count>0 && ms.Length < 2_500_000) await Send(new {type="frame",data=Convert.ToBase64String(ms.ToArray())});
                }
                catch(Exception e){Console.WriteLine("Capture: "+e.Message);}
            }
            await Task.Delay(1000/Math.Max(1,fps),stop);
        }
    }
    async Task BridgeLoop(CancellationToken stop)
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                foreach(var cmd in AgentBridge.DequeueOutgoing())
                {
                    if(activeViewer is null) continue;
                    if(cmd.Type=="chat" && !string.IsNullOrWhiteSpace(cmd.Text))
                    {
                        await Send(new {type="chat",viewer=activeViewer,from="branch",text=cmd.Text});
                        AgentBridge.AppendEvent(new BridgeEvent("chat-out", cmd.Text));
                    }
                }
            }
            catch(Exception ex){ Console.WriteLine("Bridge: "+ex.Message); }
            await Task.Delay(900, stop);
        }
    }
    async Task Send(object obj)
    {
        var ws=socket;if(ws?.State!=WebSocketState.Open)return;
        var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));await sendLock.WaitAsync();
        try{if(ws.State==WebSocketState.Open)await ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,CancellationToken.None);}
        catch(WebSocketException){}finally{sendLock.Release();}
    }
    static async Task<string?> Receive(ClientWebSocket ws,CancellationToken ct)
    {
        using var ms=new MemoryStream();var b=new byte[65536];WebSocketReceiveResult r;
        do{r=await ws.ReceiveAsync(new ArraySegment<byte>(b),ct);if(r.MessageType==WebSocketMessageType.Close)return null;ms.Write(b,0,r.Count);if(ms.Length>4*1024*1024)throw new Exception("Oversized message");}while(!r.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
    static string? String(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    static int Int(JsonElement e,string key,int fallback=0)=>e.TryGetProperty(key,out var v)&&v.TryGetInt32(out var n)?n:fallback;
    static long Long(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.TryGetInt64(out var n)?n:-1;
}
record BridgeEvent(string Type, string Text, string? Extra=null);
record OutgoingCommand(string Type, string Text);
static class AgentBridge
{
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DBSolutions", "DBRemote");
    static readonly string EventsFile = Path.Combine(Root, "events.log");
    static readonly string OutboxDir = Path.Combine(Root, "outbox");
    public static void AppendEvent(BridgeEvent evt)
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(EventsFile, JsonSerializer.Serialize(evt) + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }
    public static IEnumerable<OutgoingCommand> DequeueOutgoing()
    {
        Directory.CreateDirectory(OutboxDir);
        foreach (var file in Directory.GetFiles(OutboxDir, "*.json").OrderBy(x=>x))
        {
            OutgoingCommand? cmd = null;
            try
            {
                cmd = JsonSerializer.Deserialize<OutgoingCommand>(File.ReadAllText(file));
            }
            catch { }
            try { File.Delete(file); } catch { }
            if (cmd is not null) yield return cmd;
        }
    }
}
sealed class IncomingFile:IDisposable
{
    readonly FileStream file;
    public string Path {get;}
    public long Expected {get;}
    public long Received {get;private set;}
    public string Viewer {get;}
    public IncomingFile(string path,long expected,string viewer){Path=path;Expected=expected;Viewer=viewer;file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);}
    public async Task Write(byte[] bytes){await file.WriteAsync(bytes);Received+=bytes.Length;}
    public void Dispose()=>file.Dispose();
}
static class ClipboardWorker
{
    static T Invoke<T>(Func<T> func)
    {
        T result=default!;Exception? error=null;
        var thread=new Thread(()=>{try{result=func();}catch(Exception e){error=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join(TimeSpan.FromSeconds(5));
        if(error!=null)throw error;return result;
    }
    public static string Get()=>Invoke(()=>Clipboard.ContainsText()?Clipboard.GetText():"");
    public static void Set(string text)=>Invoke(()=>{if(text.Length>0)Clipboard.SetText(text);else Clipboard.Clear();return true;});
}
static class Input
{
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")]static extern uint SendInput(uint nInputs,[In] INPUT[] inputs,int cbSize);
    [StructLayout(LayoutKind.Sequential)]struct INPUT{public uint type;public INPUTUNION u;}
    [StructLayout(LayoutKind.Explicit)]struct INPUTUNION{[FieldOffset(0)]public KEYBDINPUT ki;[FieldOffset(0)]public MOUSEINPUT mi;}
    [StructLayout(LayoutKind.Sequential)]struct KEYBDINPUT{public ushort wVk,wScan;public uint dwFlags,time;public UIntPtr dwExtraInfo;}
    [StructLayout(LayoutKind.Sequential)]struct MOUSEINPUT{public int dx,dy;public uint mouseData,dwFlags,time;public UIntPtr dwExtraInfo;}
    static string? Str(JsonElement e,string name)=>e.TryGetProperty(name,out var v)?v.GetString():null;
    static double Num(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.TryGetDouble(out var n)?n:0;
    public static void Mouse(JsonElement m)
    {
        var r=SystemInformation.VirtualScreen;var x=r.Left+(int)(Math.Clamp(Num(m,"x"),0,1)*(r.Width-1));var y=r.Top+(int)(Math.Clamp(Num(m,"y"),0,1)*(r.Height-1));SetCursorPos(x,y);
        var action=Str(m,"action");int b=(int)Num(m,"button");uint flag=0;
        if(action=="down")flag=b==2?0x0008u:b==1?0x0020u:0x0002u;
        if(action=="up")flag=b==2?0x0010u:b==1?0x0040u:0x0004u;
        if(action=="wheel") {mouse_event(0x0800,0,0,unchecked((uint)(int)Num(m,"delta")),UIntPtr.Zero);return;}
        if(flag!=0)mouse_event(flag,0,0,0,UIntPtr.Zero);
    }
    public static void Key(JsonElement m)
    {
        int code=(int)Num(m,"code");if(code<8||code>255)return;
        keybd_event((byte)code,0,Str(m,"action")=="up"?2u:0u,UIntPtr.Zero);
    }
    public static void Text(string value)
    {
        if(value.Length>8192)return;
        foreach(var c in value)
        {
            var inputs=new[]{new INPUT{type=1,u=new INPUTUNION{ki=new KEYBDINPUT{wScan=c,dwFlags=0x0004}}},new INPUT{type=1,u=new INPUTUNION{ki=new KEYBDINPUT{wScan=c,dwFlags=0x0004|0x0002}}}};
            SendInput(2,inputs,Marshal.SizeOf<INPUT>());
        }
    }
}
