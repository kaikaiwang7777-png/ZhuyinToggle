using System.ComponentModel;
using System.Diagnostics;

namespace ZhuyinToggle;

// 僅供明確選擇的實驗；不得替換為其他後端或推論實際 IME 輸出。
internal sealed class RegExeImeStore(DiagnosticLog? diagnostics) : IImeStore
{
    private readonly RegistryImeStore registry = new();
    public ImeSetting Read() => registry.Read();
    public void Write(ImeSetting setting)
    {
        if (setting == ImeSetting.Unknown) throw new InvalidOperationException("不可寫入未知狀態。");
        // 既有機碼、種類及資料須合法；不容許 reg add 替缺失設定建立值。
        _ = registry.Read();
        var executable = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string target = setting == ImeSetting.Simplified ? "0x00000001" : "0x00000000";
        foreach (string arg in new[] { "add", @"HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC", "/v", RegistryImeStore.Name, "/t", "REG_SZ", "/d", target, "/f" }) info.ArgumentList.Add(arg);
        var clock = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("無法啟動 reg.exe。");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            bool finished = process.WaitForExit(5000);
            if (!finished)
            {
                try { process.Kill(); process.WaitForExit(); } catch (InvalidOperationException) { }
                ImeSetting observed = registry.Read();
                diagnostics?.Write("reg_exe_timeout", new { executable, args = info.ArgumentList.ToArray(), elapsedMs = clock.ElapsedMilliseconds, observed = observed.ToString() });
                throw new InvalidOperationException("reg.exe 逾時，已重新讀取設定；不會自動重試，請檢查目前設定。");
            }
            Task.WaitAll(stdout, stderr);
            diagnostics?.Write("reg_exe_result", new { executable, args = info.ArgumentList.ToArray(), pid = process.Id, exitCode = process.ExitCode, stdout = stdout.Result, stderr = stderr.Result, elapsedMs = clock.ElapsedMilliseconds, createNoWindow = true });
            if (process.ExitCode != 0) throw new InvalidOperationException($"reg.exe 執行失敗（退出碼 {process.ExitCode}）。");
            var query = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "query", @"HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC", "/v", RegistryImeStore.Name }) query.ArgumentList.Add(arg);
            using var readback = Process.Start(query) ?? throw new InvalidOperationException("無法啟動 reg.exe 回讀。");
            Task<string> readOut = readback.StandardOutput.ReadToEndAsync();
            Task<string> readErr = readback.StandardError.ReadToEndAsync();
            if (!readback.WaitForExit(5000)) { try { readback.Kill(); } catch (InvalidOperationException) { } throw new InvalidOperationException("reg.exe 回讀逾時；請檢查目前設定，沒有自動重試。"); }
            Task.WaitAll(readOut, readErr);
            diagnostics?.Write("reg_exe_query", new { executable, args = query.ArgumentList.ToArray(), pid = readback.Id, exitCode = readback.ExitCode, stdout = readOut.Result, stderr = readErr.Result });
            if (readback.ExitCode != 0 || !System.Text.RegularExpressions.Regex.IsMatch(readOut.Result, @"REG_SZ\s+" + target + @"(?:\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidOperationException("reg.exe 子程序回讀與目標不一致；實際輸出未確認。");
        }
        catch (Win32Exception ex) { throw new InvalidOperationException("無法啟動或控制 reg.exe。", ex); }
    }
}
