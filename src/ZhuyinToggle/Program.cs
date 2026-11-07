using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace ZhuyinToggle;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ZhuyinToggle", out bool created);
        if (!created) { MessageBox.Show("ZhuyinToggle 已在執行。", "ZhuyinToggle"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
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
    private readonly HotkeyWindow window;
    private readonly NotifyIcon tray;
    private readonly Icon unknownIcon;
    private readonly ContextMenuStrip menu = new();
    private readonly System.Windows.Forms.Timer clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private readonly HotkeyCoordinator hotkeys;
    private SettingsForm? form;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhuyinToggle", "settings.json");
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public TrayContext()
    {
        window = new HotkeyWindow(() => MessageBox.Show("切換功能尚未驗證，暫不可用。", "ZhuyinToggle"));
        hotkeys = new HotkeyCoordinator(new WindowsHotkeyRegistration(window.Handle), SaveHotkey);
        unknownIcon = TrayIconFactory.Create(OutputState.Unknown, Math.Max(16, SystemInformation.SmallIconSize.Width));
        tray = new NotifyIcon { Icon = unknownIcon, Text = "ZhuyinToggle：輸出模式尚未確認", ContextMenuStrip = menu, Visible = true };
        menu.Items.Add("切換繁簡（尚未驗證）", null, (_, _) => Explain());
        menu.Items.Add("目前模式：無法確認").Enabled = false;
        menu.Items.Add("快捷鍵設定", null, (_, _) => ShowSettings());
        menu.Items.Add("開機自動啟動", null, (_, _) => ToggleStartup());
        menu.Items.Add("關於", null, (_, _) => MessageBox.Show("ZhuyinToggle 開發版\n注音輸出切換尚未驗證，不能作為正式版本使用。", "關於"));
        menu.Items.Add("結束程式", null, (_, _) => ExitThread());
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); Explain(); };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { clickTimer.Stop(); clickTimer.Start(); } };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { clickTimer.Stop(); ShowSettings(); } };
        Hotkey saved = Load();
        if (!hotkeys.Start(saved, out string error))
            MessageBox.Show("快捷鍵無法註冊，其他功能仍可使用。\n" + error, "ZhuyinToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void Explain() => MessageBox.Show("目前無法可靠確認或切換微軟注音的實際輸出模式。", "尚未驗證");
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
        hotkeys.Dispose();
        form?.Dispose(); clickTimer.Dispose(); tray.Visible = false; tray.Dispose(); unknownIcon.Dispose(); menu.Dispose(); window.Dispose();
        base.ExitThreadCore();
    }
    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Action pressed;
        public HotkeyWindow(Action pressed) { this.pressed = pressed; CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m) { if (m.Msg == WM_HOTKEY) pressed(); base.WndProc(ref m); }
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
}
