using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new ProbeForm()); }
}

internal sealed class ImeTextBox : TextBox
{
    public bool Composing { get; private set; }
    public bool CandidateOpen { get; private set; }
    public event Action<string>? ImeEvent;
    public event Action? CompositionStarted;
    protected override void WndProc(ref Message m)
    {
        const int Start = 0x010D, End = 0x010E, Notify = 0x0282, OpenCandidate = 5, CloseCandidate = 4;
        if (m.Msg == Start) { Composing = true; CompositionStarted?.Invoke(); ImeEvent?.Invoke("WM_IME_STARTCOMPOSITION"); }
        if (m.Msg == End) { Composing = false; ImeEvent?.Invoke("WM_IME_ENDCOMPOSITION"); }
        if (m.Msg == Notify && m.WParam.ToInt64() == OpenCandidate) { CandidateOpen = true; ImeEvent?.Invoke("IMN_OPENCANDIDATE"); }
        if (m.Msg == Notify && m.WParam.ToInt64() == CloseCandidate) { CandidateOpen = false; ImeEvent?.Invoke("IMN_CLOSECANDIDATE"); }
        base.WndProc(ref m);
    }
}

internal sealed class ProbeForm : Form
{
    const int HotkeyId = 1, WM_HOTKEY = 0x0312;
    const string RegPath = @"SOFTWARE\Microsoft\IME\15.0\IMETC", ValueName = "Enable Simplified Chinese Output";
    const string Traditional = "0x00000000", Simplified = "0x00000001";
    readonly ImeTextBox a = new() { Multiline = true, Width = 650, Height = 65, AccessibleName = "文字方塊 A" };
    readonly ImeTextBox b = new() { Multiline = true, Width = 650, Height = 45, AccessibleName = "文字方塊 B" };
    readonly Button focusTarget = new() { Text = "焦點目標（不執行動作）", AutoSize = true, AccessibleName = "非文字焦點目標" };
    readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly CheckBox consent = new() { AutoSize = true, Text = "已結束組字及候選字，且內容可丟棄（每次重勾）" };
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "實驗紀錄" };
    readonly Label state = new() { AutoSize = true, Text = "尚未開始；此視窗的文字請勿放私人資料。" };
    readonly Button start = new() { Text = "開始一組實驗／備份原值", AutoSize = true };
    readonly Button restore = new() { Text = "結束並安全還原原值", AutoSize = true, Enabled = false };
    readonly Button abandon = new() { Text = "不覆寫並退出（原值需人工檢查）", AutoSize = true, Visible = false };
    readonly Button observedTraditional = new() { Text = "實際：電腦", AutoSize = true, Enabled = false };
    readonly Button observedSimplified = new() { Text = "實際：电脑", AutoSize = true, Enabled = false };
    readonly Button observedOther = new() { Text = "其他／失敗", AutoSize = true, Enabled = false };
    bool registered, active, blocked, performing, pending;
    string? original, expected, pendingMode, pendingTarget;
    int transitions, successes, failures;

    public ProbeForm()
    {
        Text = "焦點往返實驗（獨立測試宿主，不適用正式產品）"; Width = 1040; Height = 760; StartPosition = FormStartPosition.CenterScreen;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 385, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        mode.Items.AddRange(["A：不改變焦點", "B：A → 按鈕 → A", "C：A → B → A"]); mode.SelectedIndex = 0;
        var commands = new FlowLayoutPanel { Width = 900, AutoSize = true }; commands.Controls.AddRange([mode, start, restore, abandon]);
        var results = new FlowLayoutPanel { Width = 900, AutoSize = true }; results.Controls.AddRange([observedTraditional, observedSimplified, observedOther]);
        top.Controls.Add(new Label { AutoSize = true, Text = "僅限自行建立的可丟棄測試內容。先選模式、開始、確認組字結束，再回 A 按 Win+Alt+C。" });
        top.Controls.Add(commands); top.Controls.Add(consent); top.Controls.Add(new Label { AutoSize = true, Text = "A：主要輸入欄（每次切換後手動注音輸入「電腦／电脑」）" }); top.Controls.Add(a);
        top.Controls.Add(new Label { AutoSize = true, Text = "B：第二個焦點目標（C 組期間不可在此組字）" }); top.Controls.Add(b); top.Controls.Add(focusTarget); top.Controls.Add(results); top.Controls.Add(state);
        Controls.Add(log); Controls.Add(top);
        start.Click += (_, _) => StartSession(); restore.Click += (_, _) => EndSession();
        abandon.Click += (_, _) => { active = false; Close(); };
        observedTraditional.Click += (_, _) => RecordResult(Traditional); observedSimplified.Click += (_, _) => RecordResult(Simplified); observedOther.Click += (_, _) => RecordResult("其他／未完成");
        mode.SelectedIndexChanged += (_, _) => { if (active) { blocked = true; WriteLog("實驗期間模式改變；中止，不再寫入。請安全還原。"); } };
        a.Enter += (_, _) => FocusEvent("A Enter"); a.Leave += (_, _) => FocusEvent("A Leave"); b.Enter += (_, _) => FocusEvent("B Enter"); b.Leave += (_, _) => FocusEvent("B Leave");
        focusTarget.Enter += (_, _) => FocusEvent("Button Enter"); focusTarget.Leave += (_, _) => FocusEvent("Button Leave");
        a.ImeEvent += e => WriteLog("A " + e); b.ImeEvent += e => WriteLog("B " + e);
        a.CompositionStarted += RevokeConsent; b.CompositionStarted += RevokeConsent;
        a.KeyPress += (_, _) => RevokeConsent(); b.KeyPress += (_, _) => RevokeConsent();
        Shown += (_, _) => { registered = RegisterHotKey(Handle, HotkeyId, 0x0001 | 0x0008 | 0x4000, 0x43); WriteLog(registered ? "Win+Alt+C 已註冊。" : $"快捷鍵註冊失敗：{Marshal.GetLastWin32Error()}；停止測試。"); };
        FormClosing += OnClosing;
    }
    void RevokeConsent() { if (consent.Checked && !performing) { consent.Checked = false; WriteLog("輸入或組字開始；確認已撤銷，下一輪須重新勾選。"); } }
    void FocusEvent(string e) { WriteLog("焦點事件：" + e); }
    void WriteLog(string message) { log.AppendText($"{DateTimeOffset.Now:HH:mm:ss.fff} {message}\r\n"); }
    protected override void WndProc(ref Message m) { if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) Toggle(); base.WndProc(ref m); }
    static string Read(RegistryKey key)
    {
        if (!key.GetValueNames().Contains(ValueName) || key.GetValueKind(ValueName) != RegistryValueKind.String) throw new InvalidOperationException("設定不存在或不是 REG_SZ。");
        var result = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (result is not (Traditional or Simplified)) throw new InvalidOperationException("設定格式不符。");
        return result;
    }
    static RegistryKey Open(bool writable) => Registry.CurrentUser.OpenSubKey(RegPath, writable) ?? throw new InvalidOperationException("找不到指定登錄機碼。");
    void StartSession()
    {
        if (active || !registered) { WriteLog("無法開始：已有實驗或快捷鍵未註冊。"); return; }
        try
        {
            using var key = Open(false); original = expected = Read(key);
            string backupDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhuyinToggle", "FocusProbeBackups");
            Directory.CreateDirectory(backupDir);
            string backup = System.IO.Path.Combine(backupDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(backup, $"原始 REG_SZ={original}\r\n測試目標：HKCU IMETC Enable Simplified Chinese Output\r\n");
            WriteLog($"已保存本機備份（不含輸入文字）：{backup}"); active = true; blocked = false; pending = false; transitions = successes = failures = 0;
            mode.Enabled = false; start.Enabled = false; restore.Enabled = true; consent.Checked = false;
            state.Text = "實驗進行中：人工確認完成組字後，回到 A 觸發快捷鍵。";
            WriteLog($"開始 {mode.Text}；記憶體備份原始 REG_SZ={original}。沒有儲存文字內容。必須人工確認目前實際輸出基準。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException) { WriteLog("無法開始：" + ex.Message); }
    }
    bool OwnA() => GetForegroundWindow() == Handle && GetFocus() == a.Handle && a.Focused;
    bool OnTarget(Control c) => GetForegroundWindow() == Handle && GetFocus() == c.Handle && c.Focused;
    void Toggle()
    {
        if (!active || blocked || pending || !consent.Checked || !OwnA()) { WriteLog("中止：未開始／已鎖定／尚未記錄輸出／未確認／焦點不在本視窗 A；未寫入。"); return; }
        consent.Checked = false;
        if (a.Composing || a.CandidateOpen || b.Composing || b.CandidateOpen) { WriteLog("中止：宿主觀察到未結束的組字或候選字；未寫入。"); return; }
        var beforeText = a.Text; int beforeStart = a.SelectionStart, beforeLength = a.SelectionLength; var foreground = GetForegroundWindow();
        try
        {
            uint tickBefore = LastInputTick();
            using var key = Open(true);
            var before = Read(key);
            if (before != expected) { blocked = true; WriteLog("中止：設定與上次已知值不同，可能被外部修改；不覆蓋。"); return; }
            if (!OwnA() || LastInputTick() != tickBefore) { WriteLog("中止：使用者輸入或焦點已變，未寫入。"); return; }
            var target = before == Traditional ? Simplified : Traditional;
            WriteLog($"{mode.Text}；寫入前={before}；目標={target}；選取={beforeStart},{beforeLength}；前景為本視窗={foreground == Handle}。");
            key.SetValue(ValueName, target, RegistryValueKind.String);
            expected = target;
            if (Read(key) != target) { blocked = true; WriteLog("寫入後回讀不符；停止且不覆蓋外部值。"); return; }
            WriteLog("寫入後 REG_SZ 回讀相符；不代表實際輸出已更新。");
            if (!OwnA() || LastInputTick() != tickBefore || a.Composing || a.CandidateOpen) { blocked = true; WriteLog("寫入後焦點或輸入狀態變動；停止，不奪回焦點。請安全還原。"); return; }
            performing = true;
            try
            {
                Control? other = mode.SelectedIndex switch { 1 => focusTarget, 2 => b, _ => null };
                if (other is not null)
                {
                    if (!OwnA() || LastInputTick() != tickBefore || !other.Focus() || !OnTarget(other)) { blocked = true; WriteLog("未成功移至預定目標；停止，不嘗試奪回焦點。"); return; }
                    if (!OnTarget(other) || LastInputTick() != tickBefore || a.Composing || a.CandidateOpen || b.Composing || b.CandidateOpen) { blocked = true; WriteLog("焦點或輸入狀態意外改變；停止，不嘗試奪回焦點。"); return; }
                    if (!a.Focus() || !OwnA()) { blocked = true; WriteLog("無法恢復 A 焦點；停止，不再嘗試。"); return; }
                }
            }
            finally { performing = false; }
            if (Read(key) != expected) { blocked = true; WriteLog("焦點操作後偵測到外部設定變更；停止，不覆寫。"); return; }
            bool intact = a.Text == beforeText && a.SelectionStart == beforeStart && a.SelectionLength == beforeLength;
            bool sameForeground = GetForegroundWindow() == foreground && OwnA();
            WriteLog($"焦點動作結束：原前景與 A={sameForeground}；文字、游標及選取相同={intact}。不代表畫面無閃爍或輸出已驗證。");
            if (!sameForeground || !intact) { blocked = true; WriteLog("狀態異常；停止後續切換。"); return; }
            transitions++; pending = true; pendingMode = mode.Text; pendingTarget = target; SetResultButtons(true);
            state.Text = "請在 A 完成注音組字，再點選實際輸出；本程式不自動判讀。";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or Win32Exception)
        { blocked = true; WriteLog("操作失敗：" + ex.Message + "；停止並檢查原值。"); }
    }
    void RecordResult(string observed)
    {
        if (!pending) return;
        bool success = observed == pendingTarget;
        if (success) successes++; else failures++;
        WriteLog($"第 {transitions} 次 {pendingMode}；目標={pendingTarget}；人工觀察={observed}；{(success ? "符合" : "不符合")}。累計符合 {successes}／已記錄 {successes + failures}；需人工另記閃爍／輸入中斷。");
        pending = false; SetResultButtons(false); state.Text = "如需下一次，確認已結束組字、重勾選後回到 A。";
    }
    void SetResultButtons(bool enabled) { observedTraditional.Enabled = enabled; observedSimplified.Enabled = enabled; observedOther.Enabled = enabled; }
    bool TryRestore()
    {
        if (!active || original is null || expected is null) return true;
        try
        {
            using var key = Open(true); string current = Read(key);
            if (current != expected) { WriteLog($"未還原：當前值 {current} 不等於本程式上次觀察值；可能被外部更動。原值 {original} 請人工核對。"); return false; }
            if (current != original) { key.SetValue(ValueName, original, RegistryValueKind.String); if (Read(key) != original) { WriteLog("還原後回讀不符；請人工檢查。"); return false; } }
            WriteLog($"原始 REG_SZ={original} 已回讀確認；未驗證實際輸出模式。"); return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException) { WriteLog("無法安全還原：" + ex.Message); return false; }
    }
    void EndSession()
    {
        if (!TryRestore()) { blocked = true; abandon.Visible = true; MessageBox.Show("未能確認安全還原；請檢查日誌與 Windows 設定，程式不會覆蓋未知變更。", "需要人工檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        WriteLog($"本組摘要：完成焦點操作 {transitions} 次；人工判讀 {successes + failures} 次，符合 {successes}、不符合 {failures}；未判讀 {transitions - successes - failures} 次。未判讀不得計為成功。");
        active = false; blocked = false; pending = false; SetResultButtons(false); mode.Enabled = true; start.Enabled = true; restore.Enabled = false; consent.Checked = false; state.Text = "實驗已結束；登錄值已核對。";
    }
    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (active && !TryRestore()) { e.Cancel = true; blocked = true; abandon.Visible = true; MessageBox.Show("偵測到可能的外部變更或還原失敗，未覆蓋。請確認紀錄；需要離開時先手動確認設定。", "未能安全還原", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (registered) { UnregisterHotKey(Handle, HotkeyId); registered = false; }
    }
    static uint LastInputTick() { var x = new LastInput { cbSize = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref x) ? x.tick : throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint cbSize, tick; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetLastInputInfo(ref LastInput info);
}
