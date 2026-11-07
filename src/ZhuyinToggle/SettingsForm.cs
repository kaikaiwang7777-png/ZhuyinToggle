namespace ZhuyinToggle;

internal sealed class SettingsForm : Form
{
    private readonly TrayContext context;
    private readonly Label current = new() { AutoSize = true };
    private readonly Label mode = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Label feedback = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly TextBox recorder = new() { ReadOnly = true, Width = 280, PlaceholderText = "按下新的快捷鍵" };
    private readonly CheckBox startup = new() { Text = "登入時自動啟動", AutoSize = true };
    private Hotkey? candidate;
    private bool updatingStartup;

    public SettingsForm(TrayContext context)
    {
        this.context = context;
        Text = "ZhuyinToggle 設定（開發版）";
        ClientSize = new Size(490, 310);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16), AutoScroll = true };
        Controls.Add(layout);
        layout.Controls.Add(mode);
        UpdateMode(context.ModeText, context.ModeError);
        layout.Controls.Add(new Label { Text = "僅修改設定；實際輸出可能未同步，可能需要正常重新取得文字輸入焦點。", AutoSize = true, MaximumSize = new Size(420, 0) });
        var toggle = new Button { Text = "切換繁簡設定", AutoSize = true };
        toggle.Click += (_, _) => context.ToggleMode();
        layout.Controls.Add(toggle);
        layout.Controls.Add(current);
        layout.Controls.Add(new Label { Text = "錄製快捷鍵（點選欄位後按下組合）", AutoSize = true });
        layout.Controls.Add(recorder);
        recorder.KeyDown += CaptureHotkey;
        recorder.KeyUp += (_, e) => { e.SuppressKeyPress = true; };
        var save = new Button { Text = "儲存快捷鍵", AutoSize = true };
        save.Click += (_, _) => {
            if (candidate is null) { feedback.Text = "請先錄製有效快捷鍵。"; return; }
            if (context.ChangeHotkey(candidate, out string error)) { feedback.Text = "快捷鍵已註冊並儲存。"; current.Text = "目前快捷鍵：" + context.CurrentHotkey; }
            else feedback.Text = error;
        };
        layout.Controls.Add(save); layout.Controls.Add(feedback); layout.Controls.Add(startup);
        try { startup.Checked = context.StartupEnabled(); } catch (Exception ex) { feedback.Text = "讀取啟動設定失敗：" + ex.Message; }
        startup.CheckedChanged += (_, _) => {
            if (updatingStartup) return;
            try { context.SetStartup(startup.Checked); }
            catch (Exception ex) {
                feedback.Text = "變更啟動設定失敗：" + ex.Message;
                updatingStartup = true;
                try { startup.Checked = context.StartupEnabled(); }
                catch { startup.Enabled = false; }
                finally { updatingStartup = false; }
            }
        };
        layout.Controls.Add(new Label { Text = "版本：0.1.0 開發版；尚未通過驗收", AutoSize = true });
        current.Text = "目前快捷鍵：" + context.CurrentHotkey;
    }
    internal void UpdateMode(string setting, string? error)
        => mode.Text = $"注音設定值：{setting}；實際輸出：未知" + (error is null ? "" : $"\n{error}");
    private void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        uint modifiers = (e.Alt ? 1u : 0) | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0);
        if ((Native.GetKeyState(0x5B) & 0x8000) != 0 || (Native.GetKeyState(0x5C) & 0x8000) != 0) modifiers |= 8;
        var proposed = new Hotkey(modifiers, e.KeyCode);
        if (proposed.IsValid) { candidate = proposed; recorder.Text = proposed.ToString(); feedback.Text = "尚未儲存；系統保留的組合可能無法錄製。"; }
        e.SuppressKeyPress = true; e.Handled = true;
    }
}
