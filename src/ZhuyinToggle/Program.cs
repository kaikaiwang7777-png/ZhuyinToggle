using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ZhuyinToggle;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\ZhuyinToggle", out bool created);
        if (!created) { MessageBox.Show("ZhuyinToggle 已在執行。", "ZhuyinToggle"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(DiagnosticLog.Create(args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))));
    }
}

internal sealed record Hotkey(uint Modifiers, Keys Key)
{
    public static Hotkey Default => new(0x0009, Keys.C); // Alt | Win
    public override string ToString() => string.Join(" + ", new[] {
        (Modifiers & 8) != 0 ? "Win" : null, (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 1) != 0 ? "Alt" : null, (Modifiers & 4) != 0 ? "Shift" : null,
        Key.ToString() }.Where(s => s is not null));
    public bool IsValid => (Modifiers & 0x0f) != 0 && (Modifiers & ~0x0fu) == 0 &&
        ((Key >= Keys.A && Key <= Keys.Z) || (Key >= Keys.D0 && Key <= Keys.D9) || (Key >= Keys.F1 && Key <= Keys.F12));
}

internal sealed class TrayContext : ApplicationContext
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_IME_SETTING_CHANGED = 0x8001;
    private readonly HotkeyWindow window;
    private readonly NotifyIcon tray;
    private readonly Icon unknownIcon;
    private readonly Icon traditionalIcon;
    private readonly Icon simplifiedIcon;
    private readonly ImeModeService ime;
    private readonly DiagnosticLog? diagnostics;
    private readonly RegistryChangeWatcher watcher;
    private readonly ToolStripItem modeItem;
    private long lastToggle;
    private readonly ContextMenuStrip menu = new();
    private readonly System.Windows.Forms.Timer clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private readonly HotkeyCoordinator hotkeys;
    private SettingsForm? form;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhuyinToggle", "settings.json");
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public TrayContext(DiagnosticLog? diagnostics = null)
    {
        this.diagnostics = diagnostics;
        ime = new ImeModeService(new RegistryImeStore(), diagnostics);
        window = new HotkeyWindow(id => { diagnostics?.Write("wm_hotkey", new { id }); ToggleMode("hotkey", id); }, () => ime.Refresh());
        hotkeys = new HotkeyCoordinator(new WindowsHotkeyRegistration(window.Handle), SaveHotkey);
        int iconSize = Math.Clamp(SystemInformation.SmallIconSize.Width, 16, 256);
        unknownIcon = TrayIconFactory.Create(OutputState.Unknown, iconSize);
        traditionalIcon = TrayIconFactory.Create(OutputState.Traditional, iconSize);
        simplifiedIcon = TrayIconFactory.Create(OutputState.Simplified, iconSize);
        tray = new NotifyIcon { Icon = unknownIcon, Text = "ZhuyinToggle：輸出模式尚未確認", ContextMenuStrip = menu, Visible = true };
        menu.Items.Add("切換繁簡設定", null, (_, _) => ToggleMode("menu"));
        modeItem = menu.Items.Add("設定值：未知；實際輸出：未知"); modeItem.Enabled = false;
        var limitation = menu.Items.Add("輸出可能未同步；可能需正常重新取得輸入焦點"); limitation.Enabled = false;
        menu.Items.Add("快捷鍵設定", null, (_, _) => ShowSettings());
        menu.Items.Add("開機自動啟動", null, (_, _) => ToggleStartup());
        menu.Items.Add("關於", null, (_, _) => MessageBox.Show("ZhuyinToggle 受限開發版\n僅修改設定值，灰色圖示表示實際輸出未知。輸出可能未同步，可能需要正常重新取得文字輸入焦點；請自行確認輸出。", "關於"));
        menu.Items.Add("結束程式", null, (_, _) => ExitThread());
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); ToggleMode("tray"); };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { clickTimer.Stop(); clickTimer.Start(); } };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { clickTimer.Stop(); ShowSettings(); } };
        ime.Changed += UpdateMode;
        ime.Refresh();
        watcher = new RegistryChangeWatcher(window.NotifySettingChanged, diagnostics);
        Hotkey saved = Load();
        if (!hotkeys.Start(saved, out string error))
            MessageBox.Show("快捷鍵無法註冊，其他功能仍可使用。\n" + error, "ZhuyinToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    internal void ToggleMode(string source = "settings", int? hotkeyId = null)
    {
        long now = Environment.TickCount64;
        long elapsed = now - lastToggle;
        if (elapsed < 250) { diagnostics?.Write("toggle_ignored", new { source, hotkeyId, elapsedMs = elapsed, thresholdMs = 250 }); return; }
        diagnostics?.Write("toggle_execute", new { source, hotkeyId, elapsedMs = elapsed });
        lastToggle = now;
        if (!ime.Toggle(out string? error)) MessageBox.Show("無法切換設定：" + error, "ZhuyinToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    private void UpdateMode()
    {
        string setting = ime.Current switch { ImeSetting.Traditional => "繁體", ImeSetting.Simplified => "簡體", _ => "未知" };
        // 登錄值可能與目前 IME 的實際輸出不一致；未有可靠的輸出模式查詢前不顯示繁／简。
        tray.Icon = unknownIcon;
        tray.Text = $"ZhuyinToggle：設定值 {setting}；實際輸出未知";
        modeItem.Text = $"設定值：{setting}；實際輸出：未知";
        form?.UpdateMode(setting, ime.Error);
    }
    internal string ModeText => ime.Current switch { ImeSetting.Traditional => "繁體", ImeSetting.Simplified => "簡體", _ => "未知" };
    internal string? ModeError => ime.Error;
    private static Hotkey Load()
    {
        try { var key = JsonSerializer.Deserialize<Hotkey>(File.ReadAllText(SettingsPath)); return key is { IsValid: true } ? key : Hotkey.Default; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return Hotkey.Default; }
    }
    private static void SaveHotkey(Hotkey next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(next));
        File.Move(temp, SettingsPath, true);
    }
    private sealed class WindowsHotkeyRegistration(IntPtr handle) : IHotkeyRegistration
    {
        public bool Register(int id, Hotkey key, out string error)
        {
            if (Native.RegisterHotKey(handle, id, key.Modifiers | 0x4000, (uint)key.Key)) { error = ""; return true; }
            error = $"快捷鍵可能已被使用（Windows 錯誤碼 {Marshal.GetLastWin32Error()}）。";
            return false;
        }
        public void Unregister(int id) => Native.UnregisterHotKey(handle, id);
    }
    internal bool ChangeHotkey(Hotkey next, out string error) => hotkeys.Change(next, out error);
    internal string CurrentHotkey => hotkeys.Active?.ToString() ?? "未註冊";
    internal bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue("ZhuyinToggle") as string, StartupCommand(), StringComparison.OrdinalIgnoreCase);
    }
    private static string StartupCommand() => $"\"{Environment.ProcessPath}\"";
    internal void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey) ?? throw new IOException("無法開啟啟動設定。");
        if (enable) key.SetValue("ZhuyinToggle", StartupCommand(), RegistryValueKind.String);
        else key.DeleteValue("ZhuyinToggle", false);
    }
    private void ToggleStartup() { try { SetStartup(!StartupEnabled()); } catch (Exception ex) { MessageBox.Show(ex.Message, "啟動設定失敗"); } }
    private void ShowSettings() { if (form is null || form.IsDisposed) form = new SettingsForm(this); form.Show(); form.Activate(); }
    protected override void ExitThreadCore()
    {
        diagnostics?.Write("shutdown", new { note = "若 3 秒取樣尚未完成，紀錄可能不完整" });
        watcher.Dispose(); hotkeys.Dispose();
        form?.Dispose(); clickTimer.Dispose(); tray.Visible = false; tray.Dispose(); unknownIcon.Dispose(); traditionalIcon.Dispose(); simplifiedIcon.Dispose(); menu.Dispose(); window.Dispose();
        base.ExitThreadCore();
    }
    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Action<int> pressed;
        private readonly Action settingChanged;
        public HotkeyWindow(Action<int> pressed, Action settingChanged) { this.pressed = pressed; this.settingChanged = settingChanged; CreateHandle(new CreateParams()); }
        public void NotifySettingChanged() => Native.PostMessage(Handle, WM_IME_SETTING_CHANGED, IntPtr.Zero, IntPtr.Zero);
        protected override void WndProc(ref Message m) { if (m.Msg == WM_HOTKEY) pressed(m.WParam.ToInt32()); else if (m.Msg == WM_IME_SETTING_CHANGED) settingChanged(); base.WndProc(ref m); }
        public void Dispose() => DestroyHandle();
    }
}

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
