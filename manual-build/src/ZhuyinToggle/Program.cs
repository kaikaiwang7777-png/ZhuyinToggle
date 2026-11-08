using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZhuyinToggleManual;

static class Program
{
    [STAThread] static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ZhuyinToggle-Manual", out bool first);
        if (!first) { MessageBox.Show("人工驗收版已經在執行。", "注音切換"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication());
    }
}
record Settings(uint Modifiers = 9, Keys Key = Keys.C, bool Reactivate = true, int DelayMs = 50);
record CommandResult(string Script, string Arguments, string Stdout, string Stderr, int? ProcessExit, long Milliseconds, int? WriteExit, int? QueryExit, int? Mode, string Error);

sealed class PowerShellCommands
{
    public static readonly string PathName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    const string KeyPath = @"HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC";
    const string ValueName = "Enable Simplified Chinese Output";
    public static async Task<CommandResult> Run(int? target)
    {
        // 一次 PowerShell 先檢查原值，再依明確方向寫入，並且分開記錄兩次查詢與寫入退出碼。
        string query = $"reg query \"{KeyPath}\" /v \"{ValueName}\"";
        string write = target is null ? "" : $"reg add \"{KeyPath}\" /v \"{ValueName}\" /t REG_SZ /d \"0x{target.Value:00000000}\" /f";
        string script = $"$ErrorActionPreference = 'Continue'\n$before = & {query} 2>&1\n$beforeExit = $LASTEXITCODE\n$before | ForEach-Object {{ Write-Output $_ }}\nWrite-Output ('ZT_BEFORE_EXIT=' + $beforeExit)\n" +
            "$entry = @($before | Where-Object { $_ -match '^\\s*Enable Simplified Chinese Output\\s+REG_SZ\\s+0x0000000[01]\\s*$' })\n" +
            "if ($beforeExit -ne 0 -or $entry.Count -ne 1) { Write-Output 'ZT_INVALID_BEFORE'; exit 23 }\n" +
            (target is null ? "" : $"& {write}\n$writeExit = $LASTEXITCODE\nWrite-Output ('ZT_WRITE_EXIT=' + $writeExit)\nif ($writeExit -ne 0) {{ exit 24 }}\n") +
            $"$after = & {query} 2>&1\n$queryExit = $LASTEXITCODE\n$after | ForEach-Object {{ Write-Output $_ }}\nWrite-Output ('ZT_QUERY_EXIT=' + $queryExit)\nif ($queryExit -ne 0) {{ exit 25 }}\n" +
            "$result = @($after | Where-Object { $_ -match '^\\s*Enable Simplified Chinese Output\\s+REG_SZ\\s+0x0000000[01]\\s*$' })\n" +
            "if ($result.Count -ne 1) { exit 26 }\nif ($result[0] -match '0x00000001\\s*$') { Write-Output 'ZT_MODE=1' } else { Write-Output 'ZT_MODE=0' }\n";
        var psi = new ProcessStartInfo(PathName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(arg);
        string args = "-NoProfile -NonInteractive -EncodedCommand <已編碼的下方完整腳本>";
        var sw = Stopwatch.StartNew(); string stdout = "", stderr = "", error = ""; int? exit = null;
        try {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("無法啟動 PowerShell。");
            Task<string> o = p.StandardOutput.ReadToEndAsync(), e = p.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } await p.WaitForExitAsync(); error = "命令逾時；不會自動重試，設定可能已改變，請自行重新查詢。"; }
            stdout = await o; stderr = await e; exit = p.ExitCode;
        } catch (Exception ex) { error = "執行失敗：" + ex.Message; }
        sw.Stop();
        int? Field(string name) { var m = Regex.Match(stdout, @"(?m)^" + name + @"=(-?\d+)\s*$"); return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : null; }
        int? mode = Field("ZT_MODE"), we = Field("ZT_WRITE_EXIT"), qe = Field("ZT_QUERY_EXIT");
        if (error.Length == 0 && (exit != 0 || qe != 0 || mode is null || (target is not null && (we != 0 || mode != target)))) error = "命令失敗、資料不合法或讀回與目標不一致。";
        return new(script, args, stdout, stderr, exit, sw.ElapsedMilliseconds, we, qe, mode, error);
    }
}

sealed class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    public event Action? Pressed;
    public HotkeyWindow() { CreateHandle(new CreateParams()); }
    public bool Register(int id, Settings s) => RegisterHotKey(Handle, id, s.Modifiers | 0x4000, (uint)s.Key);
    public void Unregister(int id) => UnregisterHotKey(Handle, id);
    protected override void WndProc(ref Message m) { if (m.Msg == 0x0312) Pressed?.Invoke(); base.WndProc(ref m); }
    public void Dispose() { Unregister(1); Unregister(2); DestroyHandle(); }
}

sealed class TrayApplication : ApplicationContext
{
    const string RunName = "ZhuyinToggle-Manual";
    readonly string settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhuyinToggle-Manual", "settings.json");
    readonly NotifyIcon tray = new(); readonly HotkeyWindow hotkey = new(); readonly System.Windows.Forms.Timer clickTimer = new();
    Settings settings = new(); SettingsForm? form; bool busy; bool hotkeyRegistered; int currentId = 1; int? mode; CommandResult? last; Icon? currentIcon; string windowLog = "尚未執行視窗往返。";
    public TrayApplication()
    {
        try { if (File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new(); } catch { MessageBox.Show("無法讀取獨立設定，使用預設快捷鍵。", "注音切換"); }
        hotkey.Pressed += async () => { var original = WindowRoundTrip.Capture(); await Change(null, original); };
        hotkeyRegistered = hotkey.Register(currentId, settings);
        var menu = new ContextMenuStrip();
        menu.Items.Add("切換", null, async (_, _) => await Change(null));
        menu.Items.Add("設為繁體", null, async (_, _) => await Change(0));
        menu.Items.Add("設為簡體", null, async (_, _) => await Change(1));
        menu.Items.Add("設定", null, (_, _) => ShowSettings());
        menu.Items.Add("結束程式", null, (_, _) => ExitThread());
        tray.ContextMenuStrip = menu; tray.Visible = true;
        clickTimer.Interval = SystemInformation.DoubleClickTime; clickTimer.Tick += async (_, _) => { clickTimer.Stop(); await Change(null); };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && e.Clicks == 1) { clickTimer.Stop(); clickTimer.Start(); } };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { clickTimer.Stop(); ShowSettings(); } };
        UpdateDisplay(); if (!hotkeyRegistered) tray.ShowBalloonTip(4000, "快捷鍵註冊失敗", "快捷鍵可能已被占用；仍可使用系統匣操作。", ToolTipIcon.Warning);
        _ = RefreshMode();
    }
    async Task RefreshMode() { if (busy) return; busy = true; try { last = await PowerShellCommands.Run(null); mode = last.Error.Length == 0 ? last.Mode : null; UpdateDisplay(); } finally { busy = false; } }
    async Task Change(int? requested, WindowRoundTrip.Original? original = null)
    {
        if (busy) { tray.ShowBalloonTip(2000, "處理中", "上一次操作尚未完成。", ToolTipIcon.Info); return; }
        busy = true;
        windowLog = original is null ? "非熱鍵操作；未執行視窗往返。" : $"切換前原視窗：HWND 0x{original.Value.Handle.ToInt64():X}，PID {original.Value.ProcessId}；尚未執行。";
        try {
            var before = await PowerShellCommands.Run(null); last = before;
            if (before.Error.Length != 0 || before.Mode is null) { mode = null; throw new InvalidOperationException("無法確認目前設定；未寫入。" + before.Error); }
            int target = requested ?? (1 - before.Mode.Value);
            last = await PowerShellCommands.Run(target);
            if (last.Error.Length != 0) { mode = null; throw new InvalidOperationException(last.Error); }
            mode = last.Mode; UpdateDisplay();
            if (original is not null && settings.Reactivate)
            {
                string result = await WindowRoundTrip.Run(original.Value, settings);
                windowLog = result;
                if (!result.StartsWith("完成", StringComparison.Ordinal))
                    tray.ShowBalloonTip(5000, "設定已更新，視窗重新啟用未完成。", result, ToolTipIcon.Warning);
            }
            else if (original is not null) windowLog += " 自動重新啟用已關閉。";
        } catch (Exception e) { UpdateDisplay(); tray.ShowBalloonTip(5000, "切換未確認", e.Message, ToolTipIcon.Error); if (form is { IsDisposed: false }) MessageBox.Show(form, e.Message, "切換未確認"); }
        finally { busy = false; }
    }
    static Icon MakeIcon(int? value)
    {
        using Bitmap bmp = new(32, 32); using Graphics g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(value == 0 ? Color.RoyalBlue : value == 1 ? Color.SeaGreen : Color.DimGray);
        using var font = new Font("Microsoft JhengHei", value is null ? 20 : 18, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(value == 0 ? "繁" : value == 1 ? "简" : "?", font, Brushes.White, new RectangleF(0, 0, 32, 32), fmt);
        IntPtr h = bmp.GetHicon(); try { using Icon temp = Icon.FromHandle(h); return (Icon)temp.Clone(); } finally { DestroyIcon(h); }
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    void UpdateDisplay() { Icon next = MakeIcon(mode); tray.Icon = next; currentIcon?.Dispose(); currentIcon = next; string m = mode == 0 ? "繁體" : mode == 1 ? "簡體" : "未知"; tray.Text = $"目前設定：{m}；實際輸出待人工驗收。"; if (form is { IsDisposed: false }) form.SetStatus(m); }
    void ShowSettings() { if (form is null || form.IsDisposed) form = new SettingsForm(this); form.Show(); form.Activate(); }
    public Settings Current => settings;
    public bool Autostart { get { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return k?.GetValue(RunName) is string; } }
    public void SetAutostart(bool on) { using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\""); else k.DeleteValue(RunName, false); }
    public bool ChangeHotkey(Settings s, out string error)
    {
        error = ""; if ((s.Modifiers & 15) == 0 || s.Key is Keys.None or Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) { error = "請選擇至少一個修飾鍵及一般按鍵。"; return false; }
        if (s.Modifiers == settings.Modifiers && s.Key == settings.Key && hotkeyRegistered)
        {
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, JsonSerializer.Serialize(s)); settings = s; return true; }
            catch (Exception ex) { error = "儲存失敗：" + ex.Message; return false; }
        }
        int newId = currentId == 1 ? 2 : 1;
        if (!hotkey.Register(newId, s)) { error = "快捷鍵註冊失敗，可能已被占用；原快捷鍵維持不變。"; return false; }
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath, JsonSerializer.Serialize(s)); }
        catch (Exception ex) { hotkey.Unregister(newId); error = "儲存失敗，原快捷鍵維持不變：" + ex.Message; return false; }
        if (hotkeyRegistered) hotkey.Unregister(currentId); currentId = newId; settings = s; hotkeyRegistered = true; return true;
    }
    public string LastCommand => last is null ? "尚無命令。\r\n" + windowLog : $"PowerShell：{PowerShellCommands.PathName}\r\n參數：{last.Arguments}\r\n\r\n完整腳本：\r\n{last.Script}\r\n寫入退出碼：{last.WriteExit?.ToString() ?? "未執行"}\r\n查詢退出碼：{last.QueryExit?.ToString() ?? "未取得"}\r\n程序退出碼：{last.ProcessExit}\r\n耗時：{last.Milliseconds} ms\r\n讀回設定：{(last.Mode == 0 ? "繁體" : last.Mode == 1 ? "簡體" : "未知")}\r\n錯誤：{last.Error}\r\n\r\nstdout：\r\n{last.Stdout}\r\nstderr：\r\n{last.Stderr}\r\n\r\n視窗往返：{windowLog}";
    public Task Toggle() => Change(null);
    protected override void ExitThreadCore() { clickTimer.Stop(); clickTimer.Dispose(); form?.Dispose(); hotkey.Dispose(); tray.Visible = false; tray.Dispose(); currentIcon?.Dispose(); base.ExitThreadCore(); }
}

sealed class SettingsForm : Form
{
    readonly TrayApplication app; readonly Label status = new() { AutoSize = true }; readonly TextBox capture = new() { ReadOnly = true, Width = 240 }; readonly CheckBox startup = new() { Text = "登入時自動啟動（預設關閉）", AutoSize = true }; Settings pending;
    public SettingsForm(TrayApplication app)
    {
        this.app = app; pending = app.Current; Text = "注音切換－人工驗收開發版"; Width = 540; Height = 410; StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(14), WrapContents = false, AutoScroll = true }; Controls.Add(p);
        p.Controls.Add(status); p.Controls.Add(new Label { Text = "點選下方欄位，再按新的快捷鍵：", AutoSize = true }); p.Controls.Add(capture);
        capture.KeyDown += (_, e) => { e.SuppressKeyPress = true; uint m = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0) | ((GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) ? 8u : 0); if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return; pending = pending with { Modifiers = m, Key = e.KeyCode }; ShowHotkey(); };
        ShowHotkey(); var save = new Button { Text = "儲存快捷鍵", AutoSize = true }; save.Click += (_, _) => { if (!app.ChangeHotkey(pending, out string error)) MessageBox.Show(this, error, "快捷鍵"); else MessageBox.Show(this, "快捷鍵已儲存。", "快捷鍵"); }; p.Controls.Add(save);
        try { startup.Checked = app.Autostart; } catch { startup.Enabled = false; }
        startup.CheckedChanged += (_, _) => { try { app.SetAutostart(startup.Checked); } catch (Exception ex) { MessageBox.Show(this, "無法修改自動啟動：" + ex.Message); startup.Enabled = false; } }; p.Controls.Add(startup);
        var reactivate = new CheckBox { Text = "熱鍵切換後，自動重新啟用原視窗（待驗證）", AutoSize = true, Checked = pending.Reactivate }; p.Controls.Add(reactivate);
        var delayRow = new FlowLayoutPanel { AutoSize = true }; p.Controls.Add(delayRow);
        delayRow.Controls.Add(new Label { Text = "往返間隔：", AutoSize = true });
        var delay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 }; delay.Items.AddRange(new object[] { 50, 100, 150 }); delay.SelectedItem = new[] { 50, 100, 150 }.Contains(pending.DelayMs) ? pending.DelayMs : 50; delayRow.Controls.Add(delay);
        var saveWindow = new Button { Text = "儲存視窗設定", AutoSize = true }; saveWindow.Click += (_, _) => { var proposed = app.Current with { Reactivate = reactivate.Checked, DelayMs = (int)delay.SelectedItem! }; if (!app.ChangeHotkey(proposed, out string error)) MessageBox.Show(this, error, "視窗設定"); else { pending = pending with { Reactivate = proposed.Reactivate, DelayMs = proposed.DelayMs }; MessageBox.Show(this, "視窗設定已儲存。", "視窗設定"); } }; p.Controls.Add(saveWindow);
        var row = new FlowLayoutPanel { AutoSize = true }; p.Controls.Add(row);
        var toggle = new Button { Text = "切換繁簡設定", AutoSize = true }; toggle.Click += async (_, _) => await app.Toggle(); row.Controls.Add(toggle);
        var details = new Button { Text = "查看最近一次命令", AutoSize = true }; details.Click += (_, _) => { var f = new Form { Text = "最近一次命令及結果", Width = 850, Height = 600, StartPosition = FormStartPosition.CenterParent }; var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = app.LastCommand }; f.Controls.Add(box); f.ShowDialog(this); f.Dispose(); }; row.Controls.Add(details);
        p.Controls.Add(new Label { Text = "人工驗收開發版；實際輸出是否更新尚未驗證。", AutoSize = true }); SetStatus("讀取中");
    }
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    void ShowHotkey() { capture.Text = ((pending.Modifiers & 8) != 0 ? "Win + " : "") + ((pending.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((pending.Modifiers & 1) != 0 ? "Alt + " : "") + ((pending.Modifiers & 4) != 0 ? "Shift + " : "") + pending.Key; }
    public void SetStatus(string text) => status.Text = $"目前設定：{text}；實際輸出待人工驗收。";
}

// 實驗性頂層視窗往返；不修改第三方文字控制項，也不注入鍵盤。
static class WindowRoundTrip
{
    public readonly record struct Original(IntPtr Handle, uint ProcessId);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size; public uint Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
    static uint? LastInputTime() { var x = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref x) ? x.Time : null; }
    public static Original Capture() { IntPtr h = GetForegroundWindow(); GetWindowThreadProcessId(h, out uint pid); return new(h, pid); }
    static bool Valid(Original o) { if (o.Handle == IntPtr.Zero || !IsWindow(o.Handle)) return false; GetWindowThreadProcessId(o.Handle, out uint p); return p == o.ProcessId && p != 0; }
    static bool KeysDown(Settings s) => (GetAsyncKeyState((int)s.Key) & 0x8000) != 0 || ((s.Modifiers & 1) != 0 && (GetAsyncKeyState(0x12) & 0x8000) != 0) || ((s.Modifiers & 2) != 0 && (GetAsyncKeyState(0x11) & 0x8000) != 0) || ((s.Modifiers & 4) != 0 && (GetAsyncKeyState(0x10) & 0x8000) != 0) || ((s.Modifiers & 8) != 0 && ((GetAsyncKeyState(0x5B) | GetAsyncKeyState(0x5C)) & 0x8000) != 0);
    public static async Task<string> Run(Original o, Settings settings)
    {
        var clock = Stopwatch.StartNew(); bool taskbarOk = false, restored = false;
        string Result(string reason) => $"{reason}；原視窗 HWND 0x{o.Handle.ToInt64():X}，PID {o.ProcessId}；設定已寫入並回讀；工作列啟用：{(taskbarOk ? "成功" : "未確認")}；原視窗恢復：{(restored ? "成功" : "未確認")}；耗時 {clock.ElapsedMilliseconds}ms。";
        if (!Valid(o)) return Result("取消：原視窗不存在或程序身分已變更");
        if (GetForegroundWindow() != o.Handle) return Result("取消：使用者已離開原視窗");
        while (KeysDown(settings) && clock.ElapsedMilliseconds < 1000) { await Task.Delay(10); if (!Valid(o) || GetForegroundWindow() != o.Handle) return Result("取消：等待按鍵釋放時前景改變"); }
        if (KeysDown(settings)) return Result("取消：快捷鍵未於限時內釋放");
        var lastInput = LastInputTime(); if (lastInput is null) return Result("取消：無法確認輸入狀態");
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null); if (taskbar == IntPtr.Zero || !IsWindow(taskbar)) return Result("取消：找不到工作列");
        if (!Valid(o) || GetForegroundWindow() != o.Handle || LastInputTime() != lastInput) return Result("取消：操作前輸入或前景已改變");
        if (!SetForegroundWindow(taskbar)) return Result("失敗：系統拒絕啟用工作列");
        var start = clock.ElapsedMilliseconds;
        while (GetForegroundWindow() != taskbar && clock.ElapsedMilliseconds - start < 250)
        {
            var fg = GetForegroundWindow(); if (fg != o.Handle && fg != taskbar) return Result("取消：前景變為其他視窗");
            if (!Valid(o) || LastInputTime() != lastInput) return Result("取消：原視窗或輸入狀態變更");
            await Task.Delay(10);
        }
        if (GetForegroundWindow() != taskbar) return Result("失敗：未確認工作列成為前景");
        taskbarOk = true; start = clock.ElapsedMilliseconds;
        int delay = settings.DelayMs is 50 or 100 or 150 ? settings.DelayMs : 50;
        while (clock.ElapsedMilliseconds - start < delay)
        {
            if (GetForegroundWindow() != taskbar || !Valid(o) || LastInputTime() != lastInput) return Result("取消：等待期間前景、原視窗或輸入狀態變更");
            await Task.Delay(10);
        }
        if (GetForegroundWindow() != taskbar || !Valid(o) || LastInputTime() != lastInput) return Result("取消：恢復前條件已變更");
        if (!SetForegroundWindow(o.Handle)) return Result("失敗：系統拒絕恢復原視窗");
        start = clock.ElapsedMilliseconds;
        while (clock.ElapsedMilliseconds - start < 250)
        {
            if (!Valid(o) || LastInputTime() != lastInput) return Result("取消：恢復期間原視窗或輸入狀態變更");
            var fg = GetForegroundWindow(); if (fg == o.Handle) { restored = true; return Result("完成：已確認前景返回原視窗"); }
            if (fg != taskbar) return Result("取消：恢復期間前景變為其他視窗");
            await Task.Delay(10);
        }
        return Result("失敗：未在限時內確認原視窗恢復");
    }
}
