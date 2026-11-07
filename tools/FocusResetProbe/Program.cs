using System.Runtime.InteropServices;
using Microsoft.Win32;
using FocusResetProbe;

internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new ProbeForm()); }
}
internal sealed class ImeInput : TextBox
{
    public bool Composing { get; private set; }
    public bool Candidate { get; private set; }
    public int Starts { get; private set; }
    public int Ends { get; private set; }
    public int Results { get; private set; }
    public Action? Changed;
    public void ResetEvidence() { Starts = Ends = Results = 0; }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0302) return; // 拒絕貼上
        if (m.Msg == 0x010D) { Composing = true; Starts++; }
        if (m.Msg == 0x010F && (m.LParam.ToInt64() & 0x800) != 0) Results++;
        if (m.Msg == 0x010E) { Composing = false; Ends++; }
        if (m.Msg == 0x0282 && m.WParam.ToInt64() == 5) Candidate = true;
        if (m.Msg == 0x0282 && m.WParam.ToInt64() == 4) Candidate = false;
        base.WndProc(ref m);
        if (m.Msg is 0x010D or 0x010E or 0x010F or 0x0282) Changed?.Invoke();
    }
    public string ImmDiagnostic = "尚未查詢";
    public bool ImmSettled()
    {
        var h = ImmGetContext(Handle); if (h == IntPtr.Zero) { ImmDiagnostic = "ImmGetContext=空"; return false; }
        try { uint count; int comp = ImmGetCompositionStringW(h, 0x0008, IntPtr.Zero, 0); uint bytes = ImmGetCandidateListCountW(h, out count); ImmDiagnostic = $"composition={comp}, candidateLists={count}, candidateBytes={bytes}"; return comp == 0 && count == 0 && (bytes > 0 || (Starts > 0 && Ends >= Starts && Results > 0 && !Candidate)); }
        finally { ImmReleaseContext(Handle, h); }
    }
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hwnd);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr imc);
    [DllImport("imm32.dll", CharSet=CharSet.Unicode)] static extern int ImmGetCompositionStringW(IntPtr imc, int index, IntPtr buffer, int size);
    [DllImport("imm32.dll")] static extern uint ImmGetCandidateListCountW(IntPtr imc, out uint size);
}
internal sealed class ProbeForm : Form
{
    const string RegPath = @"SOFTWARE\Microsoft\IME\15.0\IMETC", NameOfValue = "Enable Simplified Chinese Output";
    const string T = "0x00000000", S = "0x00000001";
    readonly Button start = new() { Text = "開始測試", Width = 130, Height = 40 };
    readonly Button end = new() { Text = "結束並還原", Width = 130, Height = 40, Enabled = false };
    readonly ImeInput a = new() { Width = 670, Height = 35, Font = new Font("Microsoft JhengHei UI", 16), MaxLength = 12, AccessibleName = "測試文字框" };
    readonly TextBox b = new() { Width = 2, Height = 2, TabStop = false, AccessibleName = "內部焦點目標B" };
    readonly Button target = new() { Width = 2, Height = 2, TabStop = false, AccessibleName = "內部按鈕焦點目標" };
    readonly Label status = new() { AutoSize = false, Width = 740, Height = 70, Text = "請先按「開始測試」。只使用專用視窗內的可丟棄內容。" };
    readonly TextBox progress = new() { Width = 740, Height = 230, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, TabStop = false, AccessibleName = "測試進度及結果" };
    System.Windows.Forms.Timer? settle, timeout;
    readonly SampleGate gate = new();
    readonly int[] groupOrder = [1, 2, 0];
    int orderIndex;
    readonly List<string> focusEvents = [];
    bool active, blocked, inputExpected, driving, internalClear;
    string? original, expected, lastObserved;
    int group, phase, round; bool forwardPassed; readonly int[] completedRounds = new int[3], passedRounds = new int[3]; // baseline, simplified, traditional
    int[] pass = new int[3], fail = new int[3], unknown = new int[3];
    string[] names = ["A 不改焦點", "B 經按鈕", "C 經文字框"];
    public ProbeForm()
    {
        Text = "注音輸出對照實驗（獨立測試）"; Width = 820; Height = 510; StartPosition = FormStartPosition.CenterScreen;
        var row = new FlowLayoutPanel { AutoSize = true, Width = 740 }; row.Controls.AddRange([start, end]);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        layout.Controls.Add(row); layout.Controls.Add(status); layout.Controls.Add(a); layout.Controls.Add(progress);
        Controls.Add(layout); Controls.Add(b); Controls.Add(target); b.Location = new Point(1, 1); target.Location = new Point(4, 1); b.BringToFront(); target.BringToFront();
        a.Enter += (_,_) => FocusEvent("A Enter"); a.Leave += (_,_) => FocusEvent("A Leave");
        b.Enter += (_,_) => FocusEvent("B Enter"); b.Leave += (_,_) => FocusEvent("B Leave"); target.Enter += (_,_) => FocusEvent("Button Enter"); target.Leave += (_,_) => FocusEvent("Button Leave");
        a.Changed += OnInputActivity; a.TextChanged += (_,_) => OnInputActivity();
        start.Click += (_,_) => StartTest(); end.Click += (_,_) => Finish();
        FormClosing += (_, _) => { if (active) { Finish(); if (active) MessageBox.Show("未能安全還原，未覆寫設定。請依本機備份與 Windows 原生設定人工核對。", "需要人工檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
    }
    void Info(string message) => progress.AppendText($"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
    void FocusEvent(string value) { if (driving) { focusEvents.Add(value); Info("焦點：" + value); } }
    static RegistryKey Open(bool write) => Registry.CurrentUser.OpenSubKey(RegPath, write) ?? throw new InvalidOperationException("找不到設定機碼");
    static string Read(RegistryKey k) { if (!k.GetValueNames().Contains(NameOfValue) || k.GetValueKind(NameOfValue) != RegistryValueKind.String) throw new InvalidOperationException("不是 REG_SZ"); return k.GetValue(NameOfValue) as string is T or S ? (string)k.GetValue(NameOfValue)! : throw new InvalidOperationException("設定格式無效"); }
    bool OwnA() => GetForegroundWindow() == Handle && GetFocus() == a.Handle && a.Focused;
    bool Safe() { bool own=OwnA(), imm=a.ImmSettled(), profile=Profile.IsBopomofo(out _); bool ok=own && !a.Composing && !a.Candidate && imm && profile; if(!ok) Info($"安全觀察：自身前景焦點={own}，組字={a.Composing}，候選={a.Candidate}，IME start/end/result={a.Starts}/{a.Ends}/{a.Results}，{a.ImmDiagnostic}，微軟注音={profile}"); return ok; }
    void StartTest()
    {
        if (active) return;
        try
        {
            using var k = Open(false); original = expected = Read(k);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhuyinToggle", "FocusProbeBackups"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.txt"), $"原始 REG_SZ={original}\r\n");
            active = true; blocked = false; orderIndex = 0; group = groupOrder[orderIndex]; phase = round = 0; start.Enabled = false; end.Enabled = true; a.Focus();
            if (!OwnA() || !Profile.IsBopomofo(out string detail)) { Info("NOT TESTED：無法確認測試視窗使用微軟注音。請停止，未啟動切換。"); status.Text = "無法確認微軟注音，請按「結束並還原」。"; blocked = true; return; }
            Info("作用中設定檔：" + detail); Info("備份完成；先 B、C 各五趟，再 A 一趟。文字未寫入檔案。");
            if (!Safe()) { Stop("無法確認目前組字已結束"); return; }
            SetSetting(T); if (!blocked) Prompt();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or COMException) { Stop("啟動失敗：" + ex.Message); }
    }
    void SetSetting(string value)
    {
        if (blocked || !Safe()) { Stop("焦點、組字或輸入法狀態不安全"); return; }
        using var k = Open(true); string now = Read(k);
        if (now != expected) { Stop("設定遭外部變更，不覆寫"); return; }
        if (now != value) { k.SetValue(NameOfValue, value, RegistryValueKind.String); expected = value; }
        if (Read(k) != value) { Stop("回讀不符，停止"); return; }
        Info($"設定 {now} → {value}，回讀相符；輸出須以注音組字驗證。");
    }
    void Prompt()
    {
        if (!Safe()) { Stop("無法確認輸入欄位與組字狀態"); return; }
        CancelTimers(); internalClear = true; a.Clear(); internalClear = false; a.ResetEvidence(); inputExpected = true;
        int id = gate.Begin();
        status.Text = $"樣本 #{id} 已就緒：請用微軟注音輸入「電腦中國」，完成候選字後稍候。";
        Info($"樣本 #{id} 已就緒；{names[group]}：{(phase == 0 ? "繁體基準" : phase == 1 ? "目標簡體" : "目標繁體")}，等待實際注音輸入。");
        timeout = new System.Windows.Forms.Timer { Interval = 45000 };
        timeout.Tick += (_, _) => { if (!gate.Waiting || !gate.IsCurrent(id)) return; CancelTimers(); unknown[group]++; Stop($"樣本 #{id} 逾時：NOT TESTED"); };
        timeout.Start();
    }
    void OnInputActivity()
    {
        if (!active || blocked || !inputExpected || internalClear || !gate.Waiting) return;
        int id = gate.Id, revision = gate.Changed();
        settle?.Stop(); settle?.Dispose();
        var timer = new System.Windows.Forms.Timer { Interval = 700 }; settle = timer;
        timer.Tick += (_, _) => { timer.Stop(); timer.Dispose(); if (ReferenceEquals(settle, timer)) settle = null; Judge(id, revision); };
        timer.Start();
    }
    void CancelTimers() { settle?.Stop(); settle?.Dispose(); settle = null; timeout?.Stop(); timeout?.Dispose(); timeout = null; }
    void Judge(int id, int revision)
    {
        if (!active || blocked || !inputExpected || !gate.CanJudge(id, revision)) return;
        if (!OwnA()) { Stop("焦點已離開測試欄，不搶回"); return; }
        if (a.Composing || a.Candidate) return;
        if (a.TextLength < 4) return;
        if (!Safe() || a.Starts == 0 || a.Ends == 0 || a.Results == 0)
        { unknown[group]++; Stop("NOT TESTED：無法確認微軟注音組字及候選字提交"); return; }
        string output = a.Text;
        bool allowed = output is "電腦中國" or "电脑中国" or "電腦中国" or "电脑中國";
        if (!allowed) { unknown[group]++; Stop("NOT TESTED：輸入不符合測試詞，不記錄其他文字"); return; }
        if (!gate.TryClaim(id, revision)) return;
        CancelTimers(); inputExpected = false;
        bool success = output == (phase == 1 ? "电脑中国" : "電腦中國");
        bool validSource = phase == 0 || lastObserved == (phase == 1 ? "電腦中國" : "电脑中国");
        string verdict = !validSource ? "NOT TESTED" : success ? "PASS" : "FAIL";
        Info($"樣本 #{id} {names[group]} {(phase == 0 ? "基準" : phase == 1 ? "繁→簡" : "簡→繁")}：{output}；{verdict}；IME start/end/result={a.Starts}/{a.Ends}/{a.Results}。");
        if (phase == 0 && !success) { unknown[group]++; Stop("繁體基準未成立，不能比較"); return; }
        if (phase != 0)
        {
            if (phase == 1) forwardPassed = validSource && success;
            if (!validSource) { unknown[group]++; Info("NOT TESTED：前一次實際輸出不是切換前所需模式，不能將本次結果當作有效轉換。"); }
            else if (success) pass[group]++; else fail[group]++;
        }
        lastObserved = output;
        if (phase == 2)
        {
            completedRounds[group]++;
            if (forwardPassed && lastObserved == "電腦中國" && success) passedRounds[group]++;
            round++;
            Info($"{names[group]}：第 {round}/{(group == 0 ? 1 : 5)} 趟，成功 {passedRounds[group]}；單向 PASS={pass[group]} FAIL={fail[group]} NOT TESTED={unknown[group]}。");
            int targetRounds = group == 0 ? 1 : 5;
            if (round < targetRounds) { phase = 1; BeginToggle(); return; }
            orderIndex++;
            if (orderIndex == groupOrder.Length) { Finish(); return; }
            group = groupOrder[orderIndex]; phase = round = 0;
            SetSetting(T); if (!blocked) Prompt(); return;
        }
        phase++;
        BeginToggle();
    }
    void BeginToggle()
    {
        if (!Safe()) { Stop("組字結束狀態不明，停止切換"); return; }
        int sampleId = gate.Id;
        string text = a.Text; int pos = a.SelectionStart, selection = a.SelectionLength; var fg = GetForegroundWindow(); uint tick = LastInput();
        try
        {
            SetSetting(phase == 1 ? S : T); if (blocked) return;
            if (!Safe() || LastInput() != tick) { Stop("輸入或焦點改變，停止焦點操作"); return; }
            Control? other = group == 1 ? target : group == 2 ? b : null;
            if (other is null) { CheckAndPrompt(text, pos, selection, fg); return; }
            focusEvents.Clear(); driving = true;
            if (!other.Focus() || GetForegroundWindow() != fg || GetFocus() != other.Handle) { driving = false; Stop("無法移至自己的目標控制項"); return; }
            // 回到 UI 訊息循環，之後驗證原焦點與使用者輸入沒有變動，再恢復 A。
            BeginInvoke(() =>
            {
                if (!active || blocked || !gate.IsCurrent(sampleId) || gate.Waiting) { driving = false; return; }
                if (GetForegroundWindow() != fg || GetFocus() != other.Handle || LastInput() != tick || a.Composing || a.Candidate || !a.ImmSettled())
                { driving = false; Stop("使用者輸入或焦點已改變，不奪回"); return; }
                if (!a.Focus() || !OwnA()) { driving = false; Stop("無法恢復 A，不再嘗試"); return; }
                bool selectionChanged = a.SelectionStart != pos || a.SelectionLength != selection;
                if (selectionChanged)
                {
                    Info($"焦點返回時選取已變：原 {pos},{selection}，現 {a.SelectionStart},{a.SelectionLength}；嘗試在自有測試欄還原。");
                    if (!OwnA() || LastInput() != tick || a.Composing || a.Candidate || a.Text != text) { driving = false; Stop("狀態改變，不修正選取"); return; }
                    a.Select(pos, selection);
                }
                driving = false; Info("焦點事件順序：" + string.Join(" → ", focusEvents));
                string[] wanted = group == 1 ? ["A Leave", "Button Enter", "Button Leave", "A Enter"] : ["A Leave", "B Enter", "B Leave", "A Enter"];
                if (!focusEvents.SequenceEqual(wanted)) { Stop("焦點事件不完整；NOT TESTED"); return; }
                CheckAndPrompt(text, pos, selection, fg);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or COMException) { Stop("操作失敗：" + ex.Message); }
    }
    void CheckAndPrompt(string text, int pos, int selection, IntPtr fg)
    {
        try
        {
            using var k = Open(false);
            if (Read(k) != expected) { Stop("焦點操作後設定與預期不同，不覆寫"); return; }
            bool intact = a.Text == text && a.SelectionStart == pos && a.SelectionLength == selection;
            bool same = fg == GetForegroundWindow() && OwnA();
            Info($"焦點還原={same}；既有文字、游標與選取不變={intact}；閃爍與組字中斷未自動驗證。");
            if (!same || !intact) { Stop("焦點或文字狀態異常"); return; }
            Prompt();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { Stop(ex.Message); }
    }
    void Stop(string reason) { blocked = true; inputExpected = false; CancelTimers(); gate.Invalidate(); Info("停止：" + reason); status.Text = "測試已停止，正在嘗試還原。"; if (active && !a.Composing && !a.Candidate) Finish(); }
    void Finish()
    {
        CancelTimers(); gate.Invalidate(); inputExpected = false; blocked = true;
        if (!active) { Close(); return; }
        try
        {
            using var k = Open(true); var now = Read(k);
            if (now != expected) { Info("未還原：設定可能被外部更改，沒有覆寫。請人工確認原值 " + original); status.Text = "設定已變動，未覆寫；請人工確認。"; return; }
            if (now != original) k.SetValue(NameOfValue, original!, RegistryValueKind.String);
            if (Read(k) != original) throw new InvalidOperationException("還原後回讀不符");
            Info("原始登錄設定已回讀確認；實際輸出狀態仍須另行確認。");
            for (int i=0;i<3;i++) Info($"{names[i]}：有效單向 PASS {pass[i]}、FAIL {fail[i]}、NOT TESTED {unknown[i]}；往返 {passedRounds[i]}/{completedRounds[i]}（未完成不計成功）。");
            status.Text = "測試結束，設定已還原。可以關閉視窗。"; active = false; end.Enabled = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { Info("未能確認還原：" + ex.Message); status.Text = "未能安全還原，請人工檢查。"; }
    }
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUT { public uint size, tick; }
    static uint LastInput() { var x = new LASTINPUT { size = (uint)Marshal.SizeOf<LASTINPUT>() }; return GetLastInputInfo(ref x) ? x.tick : throw new InvalidOperationException("無法檢查輸入活動"); }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUT x);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
}
internal static class Profile
{
    [StructLayout(LayoutKind.Sequential)] struct InputProfile { public uint type; public ushort language; public Guid clsid, profile, category; public IntPtr substitute; public uint caps; public IntPtr hkl; public uint flags; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetActive(IntPtr self, ref Guid category, out InputProfile profile);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr value);
    public static bool IsBopomofo(out string detail)
    {
        detail = "未知"; IntPtr ptr = IntPtr.Zero;
        try
        {
            Guid cls = new("33C53A50-F456-4884-B049-85FD643ECFED"), iid = new("71C6E74C-0F28-11D8-A82A-00065B84435C"), cat = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
            if (CoCreateInstance(ref cls, IntPtr.Zero, 1, ref iid, out ptr) != 0 || ptr == IntPtr.Zero) return false;
            var vtable = Marshal.ReadIntPtr(ptr); var f = Marshal.GetDelegateForFunctionPointer<GetActive>(Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size));
            int hr = f(ptr, ref cat, out var p); detail = $"HRESULT=0x{hr:X8}, lang=0x{p.language:X4}, clsid={p.clsid}, profile={p.profile}";
            return hr == 0 && p.language == 0x0404 && p.clsid == new Guid("B115690A-EA02-48D5-A231-E3578D2FDF80") && p.profile == new Guid("B2F9C502-1742-11D4-9790-0080C882687E");
        }
        catch (Exception ex) when (ex is COMException or ArgumentException) { detail = ex.Message; return false; }
        finally { if (ptr != IntPtr.Zero) Marshal.Release(ptr); }
    }
}
