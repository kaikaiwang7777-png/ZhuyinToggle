using Microsoft.Win32;

// 單獨的實驗原型，不作為正式切換器；只修改已存在且格式符合的值。
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || (args[0] != "traditional" && args[0] != "simplified")) return 2;
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
            key.SetValue(name, after, RegistryValueKind.String);
            if (!string.Equals(key.GetValue(name) as string, after, StringComparison.Ordinal) || key.GetValueKind(name) != RegistryValueKind.String)
                throw new InvalidOperationException("設定回讀不一致，請手動檢查。");
            MessageBox.Show($"REG_SZ：{before} → {after}\n只確認寫入，請實際驗證注音輸出。", "登錄原型");
            return 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "登錄原型失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
