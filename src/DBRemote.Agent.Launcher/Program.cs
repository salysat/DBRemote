using System.Net.Http.Json;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace DBRemote.BranchAgent;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BranchTray());
    }
}

sealed class AgentSettings
{
    public string Server { get; set; } = File.Exists(Path.Combine(AppContext.BaseDirectory,"server-url.txt")) ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"server-url.txt")).Trim() : "wss://remote.example.com";
    public string DeviceId { get; set; } = $"branch-{Environment.MachineName}".ToLowerInvariant().Replace(' ', '-');
    public string TokenProtected { get; set; } = "";
    public string ClaimProtected { get; set; } = "";
    public bool AutoStart { get; set; } = true;
    public bool AllowPreviews { get; set; } = false;
}

static class SettingsStore
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DBSolutions", "DBRemote");
    public static readonly string FileName = Path.Combine(Root, "agent.json");
    public static AgentSettings Read()
    {
        try { return File.Exists(FileName) ? JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(FileName)) ?? new() : new(); }
        catch { return new(); }
    }
    public static void Save(AgentSettings value)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(FileName, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }
    public static string Seal(string text) => Convert.ToBase64String(Dpapi.Protect(Encoding.UTF8.GetBytes(text)));
    public static string Unseal(string text) => Encoding.UTF8.GetString(Dpapi.Unprotect(Convert.FromBase64String(text)));
}

// DPAPI CurrentUser: protects the key at rest for the Windows account that enrolled this agent.
static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int cbData; public IntPtr pbData; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    public static byte[] Protect(byte[] value) => Run(value, true);
    public static byte[] Unprotect(byte[] value) => Run(value, false);
    private static byte[] Run(byte[] value, bool encrypt)
    {
        var pointer = Marshal.AllocHGlobal(value.Length);
        try
        {
            Marshal.Copy(value, 0, pointer, value.Length);
            var input = new Blob { cbData = value.Length, pbData = pointer };
            Blob output;
            var ok = encrypt ? CryptProtectData(ref input, "DB Remote Agent", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.cbData]; Marshal.Copy(output.pbData, result, 0, result.Length); return result; }
            finally { LocalFree(output.pbData); }
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}


record BranchOutgoingCommand(string Type, string Text);
record BridgeUiEvent(string Type, string Text, string? Extra = null);

sealed class BranchTray : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem status;
    Process? child;
    AgentSettings current;
    readonly string agentExe = Path.Combine(AppContext.BaseDirectory, "DBRemote.Agent.Windows.exe");
    readonly System.Windows.Forms.Timer healthTimer = new() { Interval = 4000 };
    bool exiting;
    bool checkingCredential;
    DateTimeOffset lastCredentialCheck=DateTimeOffset.MinValue;
    CancellationTokenSource? registrationCancel;

    public BranchTray()
    {
        current = SettingsStore.Read();
        status = new ToolStripMenuItem("غير متصل") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add("الإعدادات", null, (_, _) => Configure());
        menu.Items.Add("إعادة الاتصال", null, (_, _) => Restart());
        menu.Items.Add("إيقاف الاتصال", null, (_, _) => StopAgent());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("خروج", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "DB Remote - Branch Agent", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Configure();
        healthTimer.Tick += async (_, _) => {
            if (child != null && child.HasExited) { StopAgent(); status.Text = "انقطع الاتصال - اختر إعادة الاتصال"; }
            if (checkingCredential || string.IsNullOrEmpty(current.TokenProtected) || string.IsNullOrEmpty(current.ClaimProtected) || DateTimeOffset.UtcNow-lastCredentialCheck<TimeSpan.FromSeconds(20)) return;
            checkingCredential=true;lastCredentialCheck=DateTimeOffset.UtcNow;
            try { await CheckRevocation(); } finally { checkingCredential=false; }
        };
        healthTimer.Start();
        if(current.Server.Contains("example.com",StringComparison.OrdinalIgnoreCase)) Configure(); else Restart();
    }

    void Configure()
    {
        var form = new Form
        {
            Text = "مركز ربط جهاز الفرع | DB Remote", Width = 960, Height = 760,
            StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(244,248,252),
            ForeColor = Color.FromArgb(25,55,86), RightToLeft = RightToLeft.Yes,
            RightToLeftLayout = true, Font = new Font("Segoe UI", 10), MinimumSize = new Size(820,650)
        };

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3, BackColor = form.BackColor };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

        var head = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0,0,0,12), BorderStyle = BorderStyle.FixedSingle };
        var headGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White };
        headGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        headGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var logoCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 244, 251), Margin = new Padding(0,0,12,0), Padding = new Padding(8) };
        var logo = new Label { Text = "DB\nSOLUTIONS", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.FromArgb(26, 108, 176), TextAlign = ContentAlignment.MiddleCenter };
        logoCard.Controls.Add(logo);
        var titleWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.White };
        titleWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        titleWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        titleWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        titleWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Text = "DB REMOTE  |  مركز جهاز الفرع", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.FromArgb(18, 63, 110), TextAlign = ContentAlignment.MiddleLeft };
        var sub = new Label { Text = "مركز موحّد للمحادثة والملفات وإعدادات جهاز الفرع", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(96,118,142), TextAlign = ContentAlignment.MiddleLeft };
        var badges = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        Label Chip(string text, Color back, Color fore) => new Label { AutoSize = true, Text = "  " + text + "  ", Margin = new Padding(0,0,8,0), Padding = new Padding(8,4,8,4), BackColor = back, ForeColor = fore, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        badges.Controls.Add(Chip("ربط مباشر", Color.FromArgb(235,244,251), Color.FromArgb(18,63,110)));
        badges.Controls.Add(Chip("اعتماد آمن", Color.FromArgb(235,248,241), Color.FromArgb(21,122,89)));
        badges.Controls.Add(Chip("تشغيل تلقائي", Color.FromArgb(246,241,234), Color.FromArgb(156,110,38)));
        var sep = new Label { Text = "", Dock = DockStyle.Fill };
        titleWrap.Controls.Add(title, 0, 0);
        titleWrap.Controls.Add(sub, 0, 1);
        titleWrap.Controls.Add(badges, 0, 2);
        titleWrap.Controls.Add(sep, 0, 3);
        headGrid.Controls.Add(logoCard, 0, 0);
        headGrid.Controls.Add(titleWrap, 1, 0);
        head.Controls.Add(headGrid);

        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.Normal, RightToLeftLayout = true };
        var tabChat = new TabPage("المحادثة") { BackColor = form.BackColor };
        var tabFiles = new TabPage("الملفات") { BackColor = form.BackColor };
        var tabSettings = new TabPage("الإعدادات") { BackColor = form.BackColor };
        tabs.TabPages.AddRange(new[] { tabChat, tabFiles, tabSettings });

        // Chat tab
        var chatLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        chatLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chatLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var chatInfo = new Label { Text = "المحادثة مع الإدارة  •  إرسال واستقبال الرسائل أثناء الاتصال", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(87,108,130), TextAlign = ContentAlignment.MiddleRight };
        var chatBox = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = false, Font = new Font("Segoe UI", 10), BackColor = Color.White, ForeColor = Color.FromArgb(29,52,76), BorderStyle = BorderStyle.FixedSingle };
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sendBtn = new Button { Text = "إرسال", Dock = DockStyle.Fill, BackColor = Color.FromArgb(37,124,175), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10, FontStyle.Bold) }; sendBtn.FlatAppearance.BorderSize = 0;
        var chatInput = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "اكتب رسالة إلى الإدارة...", BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        composer.Controls.Add(sendBtn, 0, 0); composer.Controls.Add(chatInput, 1, 0);
        chatLayout.Controls.Add(chatInfo, 0, 0); chatLayout.Controls.Add(chatBox, 0, 1); chatLayout.Controls.Add(composer, 0, 2);
        tabChat.Controls.Add(chatLayout);

        // Files tab
        var filesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        filesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        filesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        filesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var filesInfo = new Label { Text = "الملفات المستلمة من الإدارة", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(87,108,130), TextAlign = ContentAlignment.MiddleRight };
        var filesList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, RightToLeftLayout = true };
        filesList.Columns.Add("الاسم", 340); filesList.Columns.Add("آخر تعديل", 180); filesList.Columns.Add("الحجم", 110);
        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var openFile = new Button { Text = "فتح الملف", Width = 110, BackColor = Color.FromArgb(37,124,175), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; openFile.FlatAppearance.BorderSize = 0;
        var openFolder = new Button { Text = "فتح المجلد", Width = 110, BackColor = Color.FromArgb(224,234,244), ForeColor = Color.FromArgb(25,55,86), FlatStyle = FlatStyle.Flat }; openFolder.FlatAppearance.BorderSize = 0;
        var refreshFiles = new Button { Text = "تحديث", Width = 90, BackColor = Color.FromArgb(224,234,244), ForeColor = Color.FromArgb(25,55,86), FlatStyle = FlatStyle.Flat }; refreshFiles.FlatAppearance.BorderSize = 0;
        fileButtons.Controls.Add(openFile); fileButtons.Controls.Add(openFolder); fileButtons.Controls.Add(refreshFiles);
        var filesHint = new Label { Text = @"ستجد الملفات في Downloads\DBRemote Incoming ويمكن فتحها من هنا مباشرة.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(87,108,130), TextAlign = ContentAlignment.MiddleRight };
        filesLayout.Controls.Add(filesInfo, 0, 0); filesLayout.Controls.Add(filesList, 0, 1); filesLayout.Controls.Add(fileButtons, 0, 2); filesLayout.Controls.Add(filesHint, 0, 3);
        tabFiles.Controls.Add(filesLayout);

        // Settings tab
        var settingsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 10, BackColor = form.BackColor };
        settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); settingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settingsPanel.Controls.Add(new Label { Text = "عنوان سيرفر الإدارة", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(87,108,130) }, 0, 0);
        var url = new TextBox { Dock = DockStyle.Fill, Text = current.Server, RightToLeft = RightToLeft.No, BackColor = Color.White, ForeColor = Color.FromArgb(25,55,86), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11) };
        settingsPanel.Controls.Add(url, 0, 1);
        settingsPanel.Controls.Add(new Label { Text = "معرّف الجهاز · يُنشأ تلقائيًا", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(87,108,130) }, 0, 2);
        var id = new TextBox { Dock = DockStyle.Fill, Text = current.DeviceId, RightToLeft = RightToLeft.No, BackColor = Color.White, ForeColor = Color.FromArgb(25,55,86), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11) };
        settingsPanel.Controls.Add(id, 0, 3);
        var autostart = new CheckBox { Text = "تشغيل Agent تلقائيًا بعد تسجيل الدخول إلى Windows", Checked = current.AutoStart, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(25,55,86), AutoSize = false };
        settingsPanel.Controls.Add(autostart, 0, 4);
        var previewConsent = new CheckBox { Text = "السماح بمعاينة مصغّرة دورية لشاشة هذا الجهاز في الإدارة", Checked = current.AllowPreviews, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(25,55,86), AutoSize = false };
        settingsPanel.Controls.Add(previewConsent, 0, 5);
        settingsPanel.Controls.Add(new Label { Text = "✓ الربط تلقائي: بمجرد تشغيل البرنامج يُرسل طلب الجهاز وينتظر موافقة الإدارة.\nللأجهزة الموجودة في فروع أخرى استخدم wss:// مع شهادة HTTPS موثوقة.\nيسمح بـ ws://localhost:5080 للاختبار على نفس الكمبيوتر فقط.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(22, 143, 108), TextAlign = ContentAlignment.TopRight }, 0, 6);
        tabSettings.Controls.Add(settingsPanel);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var statusLabel = new Label { Text = status.Text, Width = 300, Height = 40, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(25,55,86) };
        var save = new Button { Text = "حفظ الإعدادات والاتصال", Width = 220, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(22,104,170), ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) }; save.FlatAppearance.BorderSize = 0;
        var close = new Button { Text = "إغلاق", Width = 100, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(224, 234, 244), ForeColor = Color.FromArgb(25,55,86) }; close.FlatAppearance.BorderSize = 0;
        footer.Controls.Add(save); footer.Controls.Add(close); footer.Controls.Add(statusLabel);

        string eventsFile = Path.Combine(SettingsStore.Root, "events.log");
        string outboxDir = Path.Combine(SettingsStore.Root, "outbox");
        string incomingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DBRemote Incoming");
        Directory.CreateDirectory(outboxDir);

        void LoadFiles()
        {
            Directory.CreateDirectory(incomingDir);
            filesList.Items.Clear();
            foreach(var file in new DirectoryInfo(incomingDir).GetFiles().OrderByDescending(f=>f.LastWriteTime))
            {
                var item = new ListViewItem(file.Name);
                item.SubItems.Add(file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add((file.Length/1024d).ToString("0.0") + " KB");
                item.Tag = file.FullName;
                filesList.Items.Add(item);
            }
        }

        void LoadChat()
        {
            chatBox.Items.Clear();
            if(!File.Exists(eventsFile)) return;
            foreach(var line in File.ReadLines(eventsFile).TakeLast(200))
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<BridgeUiEvent>(line);
                    if(evt == null) continue;
                    if(evt.Type == "chat-in") chatBox.Items.Add("الإدارة: " + evt.Text);
                    else if(evt.Type == "chat-out") chatBox.Items.Add("أنت: " + evt.Text);
                    else if(evt.Type == "file-in") chatBox.Items.Add("ملف مستلم: " + System.IO.Path.GetFileName(evt.Text));
                }
                catch { }
            }
            if(chatBox.Items.Count>0) chatBox.TopIndex = chatBox.Items.Count-1;
        }

        void QueueOutgoingChat()
        {
            var text = chatInput.Text.Trim();
            if(string.IsNullOrWhiteSpace(text)) return;
            Directory.CreateDirectory(outboxDir);
            var path = Path.Combine(outboxDir, Guid.NewGuid().ToString("N") + "-chat.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new BranchOutgoingCommand("chat", text)), Encoding.UTF8);
            chatInput.Clear();
            LoadChat();
            statusLabel.Text = status.Text = "تم إرسال الرسالة";
        }

        sendBtn.Click += (_, _) => QueueOutgoingChat();
        chatInput.KeyDown += (_, e) => { if(e.KeyCode == Keys.Enter){ e.SuppressKeyPress = true; QueueOutgoingChat(); } };
        openFolder.Click += (_, _) => { Directory.CreateDirectory(incomingDir); Process.Start("explorer.exe", incomingDir); };
        refreshFiles.Click += (_, _) => LoadFiles();
        openFile.Click += (_, _) => { if(filesList.SelectedItems.Count == 0) return; var full = filesList.SelectedItems[0].Tag as string; if(full != null && File.Exists(full)) Process.Start(new ProcessStartInfo(full) { UseShellExecute = true }); };
        close.Click += (_, _) => form.Close();
        save.Click += (_, _) =>
        {
            var server = url.Text.Trim().TrimEnd('/');
            var dev = id.Text.Trim();
            var local = server is "ws://localhost:5080" or "ws://127.0.0.1:5080";
            if (!(Uri.TryCreate(server, UriKind.Absolute, out var uri) && (uri.Scheme == "wss" || local))) { MessageBox.Show("أدخل عنوان wss:// صالحاً، أو ws://localhost:5080 للتجربة المحلية فقط."); return; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(dev, "^[a-zA-Z0-9_-]{3,64}$")) { MessageBox.Show("معرّف الجهاز: أحرف إنجليزية أو أرقام وشرطة، بطول 3 إلى 64."); return; }
            try
            {
                var next = new AgentSettings { Server = server, DeviceId = dev, AutoStart = autostart.Checked, AllowPreviews = previewConsent.Checked, TokenProtected = dev == current.DeviceId && server == current.Server ? current.TokenProtected : "", ClaimProtected = dev == current.DeviceId && server == current.Server ? current.ClaimProtected : "" };
                SettingsStore.Save(next);
                SetAutostart(next.AutoStart);
                current = next;
                Restart();
                statusLabel.Text = status.Text = "تم حفظ الإعدادات وإعادة الاتصال";
                MessageBox.Show("تم حفظ الإعدادات وبدء الاتصال.");
            }
            catch (Exception ex) { MessageBox.Show("تعذر الحفظ: " + ex.Message); }
        };

        var refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        refreshTimer.Tick += (_, _) => { statusLabel.Text = status.Text; LoadChat(); LoadFiles(); };
        form.FormClosed += (_, _) => refreshTimer.Stop();
        refreshTimer.Start();
        LoadChat(); LoadFiles();

        main.Controls.Add(head, 0, 0); main.Controls.Add(tabs, 0, 1); main.Controls.Add(footer, 0, 2);
        form.Controls.Add(main);
        form.ShowDialog();
    }

    void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        const string name = "DBRemoteBranchAgent";
        if (enabled) key?.SetValue(name, "\"" + Environment.ProcessPath + "\""); else key?.DeleteValue(name, false);
    }

    async void Restart()
    {
        registrationCancel?.Cancel();
        registrationCancel?.Dispose();
        registrationCancel=new CancellationTokenSource();
        StopAgent();
        if(string.IsNullOrEmpty(current.TokenProtected))
        {
            await Enroll(registrationCancel.Token);
            if(string.IsNullOrEmpty(current.TokenProtected))return;
        }
        if (!File.Exists(agentExe)) { status.Text = "ملف Agent غير موجود"; MessageBox.Show("لم يتم العثور على DBRemote.Agent.Windows.exe بجوار البرنامج. شغّل publish-branch-agent.ps1 أولاً."); return; }
        try
        {
            var psi = new ProcessStartInfo(agentExe) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.Environment["DBREMOTE_SERVER"] = current.Server;
            psi.Environment["DBREMOTE_DEVICE_ID"] = current.DeviceId;
            psi.Environment["DBREMOTE_AGENT_TOKEN"] = SettingsStore.Unseal(current.TokenProtected);
            psi.Environment["DBREMOTE_ALLOW_PREVIEWS"] = current.AllowPreviews ? "1" : "0";
            child = new Process { StartInfo = psi, EnableRaisingEvents = true };
            child.OutputDataReceived += (_, e) => {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data.Contains("Connected. Waiting", StringComparison.OrdinalIgnoreCase)) status.Text = "متصل بالخادم - " + current.DeviceId;
                if (e.Data.StartsWith("CHAT|", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = e.Data.Split('|', 3);
                    var msg = parts.Length >= 3 ? parts[2] : "رسالة جديدة";
                    tray.BalloonTipTitle = "DB Remote Chat";
                    tray.BalloonTipText = msg.Length > 180 ? msg[..180] + "..." : msg;
                    tray.ShowBalloonTip(5000);
                    status.Text = "رسالة جديدة من لوحة الإدارة";
                }
            };
            child.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) status.Text = "خطأ في Agent - راجع الإعدادات"; };
            child.Start(); child.BeginOutputReadLine(); child.BeginErrorReadLine();
            status.Text = "جاري الاتصال - " + current.DeviceId;
        }
        catch (Exception ex) { status.Text = "فشل التشغيل"; MessageBox.Show(ex.Message); }
    }

    async Task CheckRevocation()
    {
        try
        {
            var settings=current;
            var claim=SettingsStore.Unseal(settings.ClaimProtected);
            var uri=new Uri(settings.Server.Replace("wss://","https://").Replace("ws://","http://").TrimEnd('/')+"/");
            using var http=new HttpClient { BaseAddress=uri,Timeout=TimeSpan.FromSeconds(7) };
            using var response=await http.PostAsJsonAsync("api/enroll/check",new { deviceId=settings.DeviceId, claim, name=Environment.MachineName });
            if(!response.IsSuccessStatusCode) return;
            using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(data.RootElement.GetProperty("status").GetString()!="revoked") return;
            if(current.DeviceId!=settings.DeviceId || current.Server!=settings.Server) return;
            StopAgent();
            current.TokenProtected="";
            SettingsStore.Save(current);
            status.Text="تم سحب الاعتماد؛ جاري طلب موافقة جديدة";
            Restart();
        }
        catch(Exception) { /* Network interruptions must not erase a valid credential. */ }
    }

    async Task Enroll(CancellationToken ct)
    {
        var settings=current;
        if(settings.Server.Contains("example.com",StringComparison.OrdinalIgnoreCase)) { status.Text="اضبط عنوان السيرفر أولاً";return; }
        status.Text="بانتظار موافقة الإدارة - "+settings.DeviceId;
        try
        {
            if(string.IsNullOrEmpty(settings.ClaimProtected))
            {
                settings.ClaimProtected=SettingsStore.Seal(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                SettingsStore.Save(settings);
            }
            var claim=SettingsStore.Unseal(settings.ClaimProtected);
            var uri=new Uri(settings.Server.Replace("wss://","https://").Replace("ws://","http://").TrimEnd('/')+"/");
            using var http=new HttpClient{BaseAddress=uri,Timeout=TimeSpan.FromSeconds(10)};
            var request=new {deviceId=settings.DeviceId,claim,name=Environment.MachineName};
            using(var response=await http.PostAsJsonAsync("api/enroll",request,ct))
            {
                if(!response.IsSuccessStatusCode){status.Text="التسجيل مرفوض: "+(int)response.StatusCode;return;}
            }
            while(!ct.IsCancellationRequested)
            {
                await Task.Delay(4000,ct);
                using var response=await http.PostAsJsonAsync("api/enroll/check",request,ct);
                if(!response.IsSuccessStatusCode) {status.Text="فشل متابعة التسجيل";return;}
                using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var state=data.RootElement.GetProperty("status").GetString();
                if(state=="approved")
                {
                    var token=data.RootElement.GetProperty("token").GetString();
                    if(string.IsNullOrWhiteSpace(token))return;
                    if(current.DeviceId!=settings.DeviceId || current.Server!=settings.Server)return;
                    current.TokenProtected=SettingsStore.Seal(token);
                    SettingsStore.Save(current);
                    status.Text="تم اعتماد الجهاز";
                    return;
                }
                if(state=="revoked") {status.Text="تم إلغاء تصريح الجهاز";return;}
            }
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) {status.Text="خطأ تسجيل: "+ex.Message;}
    }

    void StopAgent()
    {
        if (child is not null)
        {
            try { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(1500); } } catch { }
            child.Dispose(); child = null;
        }
        if (!exiting) status.Text = "متوقف";
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        registrationCancel?.Cancel();
        healthTimer.Stop(); healthTimer.Dispose();
        StopAgent(); tray.Visible = false; tray.Dispose();
        base.ExitThreadCore();
    }
}
