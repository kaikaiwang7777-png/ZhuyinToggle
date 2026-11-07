using System.Runtime.InteropServices;
using Microsoft.Win32;

// 只觀察本程序的測試輸入框。組字和候選字的 TSF 安全狀態尚無可靠判斷，
// 故嚴格失敗關閉：本版不寫登錄檔、不移動焦點，也不接觸其他程序。
internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new ProbeForm()); }
}

internal sealed class ProbeForm : Form
{
    private const int Hotkey = 1, WM_HOTKEY = 0x0312;
    private const string Path = @"SOFTWARE\Microsoft\IME\15.0\IMETC";
    private const string ValueName = "Enable Simplified Chinese Output";
    private readonly TextBox editor = new() { Multiline = true, Dock = DockStyle.Top, Height = 100, AccessibleName = "實驗輸入欄" };
    private readonly TextBox observations = new() { Multiline = true, Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "觀察結果" };
    private readonly Label notice = new() { Dock = DockStyle.Top, Height = 70, Text = "獨立觀察原型：僅在此視窗的輸入欄按 Win + Alt + C。\r\n無法可靠確認 TSF 組字／候選字是否結束，故禁止寫入和焦點重置。\r\n不監測其他程序的文字，不記錄輸入內容。" };
    private bool registered;
    public ProbeForm()
    {
        Text = "ZhuyinToggle 焦點重置安全性探針（只讀）"; Width = 780; Height = 480;
        Controls.Add(observations); Controls.Add(editor); Controls.Add(notice);
        Shown += (_, _) => { registered = RegisterHotKey(Handle, Hotkey, 0x0001 | 0x0008 | 0x4000, 0x43); Log(registered ? "快捷鍵已註冊。" : $"快捷鍵註冊失敗：{Marshal.GetLastWin32Error()}；不影響正式程式。"); };
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Hotkey) Observe();
        base.WndProc(ref m);
    }
    private void Observe()
    {
        var foreground = GetForegroundWindow();
        var ownFocus = GetFocus();
        uint tid = GetWindowThreadProcessId(foreground, out uint pid);
        var hkl = GetKeyboardLayout(tid);
        Log($"前景視窗屬於原型={foreground == Handle}；焦點為實驗輸入欄={ownFocus == editor.Handle}；前景執行緒={tid}；HKL=0x{hkl.ToInt64():X}（不證明 IME 身分）。");
        if (foreground != Handle || ownFocus != editor.Handle) { Log("中止：焦點不在此實驗視窗指定欄位；未更改任何設定或焦點。"); return; }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Path, false);
            if (key is null || !key.GetValueNames().Contains(ValueName) || key.GetValueKind(ValueName) != RegistryValueKind.String)
            { Log("中止：找不到 REG_SZ 設定；未更改。"); return; }
            var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (value is not ("0x00000000" or "0x00000001")) { Log("中止：設定格式不正確；未更改。"); return; }
            Log($"登錄檔目前 REG_SZ={value}；選取起點={editor.SelectionStart}，選取長度={editor.SelectionLength}，文字長度={editor.TextLength}。");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { Log($"中止：讀取設定失敗（{ex.GetType().Name}）；未更改。"); return; }
        Log("安全閘門：本原型無法證明新版微軟注音的 TSF 組字及候選字均已結束；不執行寫入或焦點移動。游標與文字未由本程式修改。");
    }
    private void Log(string text) => observations.AppendText($"{DateTimeOffset.Now:HH:mm:ss.fff} {text}\r\n");
    protected override void OnFormClosed(FormClosedEventArgs e) { if (registered) UnregisterHotKey(Handle, Hotkey); base.OnFormClosed(e); }
    [DllImport("user32.dll", SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
}

