using Microsoft.Win32;
using ZhuyinToggle;
using System.Diagnostics;

// 單獨的實驗原型，不作為正式切換器；只修改已存在且格式符合的值。
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1 || (args[0] != "traditional" && args[0] != "simplified") ||
            args.Skip(1).Any(x => x is not ("--diagnostics" or "--no-dialog")) ||
            (args.Contains("--no-dialog") && !args.Contains("--diagnostics"))) return 2;
        var diagnostics = DiagnosticLog.Create(args.Contains("--diagnostics"), "RegistryToggleProbe");
        if (args.Contains("--diagnostics") && diagnostics is null) return 1;
        int id = diagnostics?.NextSequence() ?? 0;
        const string path = @"SOFTWARE\Microsoft\IME\15.0\IMETC";
        const string name = "Enable Simplified Chinese Output";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path, true) ?? throw new InvalidOperationException("找不到設定機碼。");
            if (!key.GetValueNames().Contains(name) || key.GetValueKind(name) != RegistryValueKind.String)
                throw new InvalidOperationException("設定不存在或類型不是 REG_SZ；未寫入。");
            string? before = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (before is not ("0x00000000" or "0x00000001")) throw new InvalidOperationException("設定值格式不符；未寫入。");
            string after = args[0] == "simplified" ? "0x00000001" : "0x00000000";
            diagnostics?.Snapshot(id, "before");
            diagnostics?.Write("write_attempt", new { id, target = args[0], dialog = !args.Contains("--no-dialog") });
            key.SetValue(name, after, RegistryValueKind.String);
            long writtenAt = Stopwatch.GetTimestamp();
            diagnostics?.Write("write_returned", new { id, success = true });
            ImeSetting expected = args[0] == "simplified" ? ImeSetting.Simplified : ImeSetting.Traditional;
            diagnostics?.Snapshot(id, "immediate", expected);
            diagnostics?.ScheduleSnapshots(id, writtenAt, expected);
            if (!string.Equals(key.GetValue(name) as string, after, StringComparison.Ordinal) || key.GetValueKind(name) != RegistryValueKind.String)
                throw new InvalidOperationException("設定回讀不一致，請手動檢查。");
            if (args.Contains("--no-dialog")) Thread.Sleep(3200); // 僅保留診斷取樣程序生命週期，不是修復。
            else MessageBox.Show($"REG_SZ：{before} → {after}\n只確認寫入，請實際驗證注音輸出。", "登錄原型");
            return 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            diagnostics?.Write("toggle_error", new { id, type = ex.GetType().Name });
            MessageBox.Show(ex.Message, "登錄原型失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
