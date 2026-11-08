using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuyinToggle;

// 僅限明確啟用的實驗：PowerShell 執行原始 reg 命令，不切換其他後端。
internal sealed class PowerShellImeStore(DiagnosticLog? diagnostics) : IImeStore
{
    private readonly RegistryImeStore registry = new();
    public ImeSetting Read() => registry.Read();
    public void Write(ImeSetting setting)
    {
        if (setting == ImeSetting.Unknown) throw new InvalidOperationException("不可寫入未知狀態。");
        _ = registry.Read();
        string target = setting == ImeSetting.Simplified ? "0x00000001" : "0x00000000";
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            Write-Output ('ZT_PS_VERSION=' + $PSVersionTable.PSVersion.ToString())
            Write-Output ('ZT_PS_PATH=' + (Get-Process -Id $PID).Path)
            Write-Output ('ZT_REG_PATH=' + (Get-Command reg.exe -CommandType Application).Source)
            reg add "HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC" /v "Enable Simplified Chinese Output" /t REG_SZ /d "{{target}}" /f
            $writeExit = $LASTEXITCODE
            Write-Output ('ZT_WRITE_EXIT=' + $writeExit)
            if ($writeExit -ne 0) { exit $writeExit }
            reg query "HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC" /v "Enable Simplified Chinese Output"
            $queryExit = $LASTEXITCODE
            Write-Output ('ZT_QUERY_EXIT=' + $queryExit)
            if ($queryExit -ne 0) { exit $queryExit }
            exit 0
            """;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(arg);
        var clock = Stopwatch.StartNew();
        try
        {
            using var child = Process.Start(info) ?? throw new InvalidOperationException("無法啟動 PowerShell。");
            Task<string> stdout = child.StandardOutput.ReadToEndAsync();
            Task<string> stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(10000))
            {
                try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                string observed; try { observed = registry.Read().ToString(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { observed = "unknown:" + ex.GetType().Name; }
                diagnostics?.Write("powershell_timeout", new { executable, elapsedMs = clock.ElapsedMilliseconds, observed });
                throw new InvalidOperationException("PowerShell 逾時；不自動重試，請檢查設定。");
            }
            Task.WaitAll(stdout, stderr);
            string output = stdout.Result;
            diagnostics?.Write("powershell_result", new { executable, args = info.ArgumentList.ToArray(), script, child.Id, child.ExitCode, stdout = output, stderr = stderr.Result, elapsedMs = clock.ElapsedMilliseconds, noProfile = true, createNoWindow = true });
            if (child.ExitCode != 0 || !output.Contains("ZT_WRITE_EXIT=0", StringComparison.Ordinal) || !output.Contains("ZT_QUERY_EXIT=0", StringComparison.Ordinal) ||
                !Regex.IsMatch(output, @"REG_SZ\s+" + target + @"(?:\s|$)", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("PowerShell 寫入或回讀失敗；實際輸出未確認。");
        }
        catch (Win32Exception ex) { throw new InvalidOperationException("無法啟動或控制 PowerShell。", ex); }
    }
}
