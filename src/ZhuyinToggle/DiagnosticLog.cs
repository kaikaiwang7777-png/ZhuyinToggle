using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace ZhuyinToggle;

// 僅在 --diagnostics 時啟用。檔案僅供本機診斷，含 SID，不應提交或分享。
internal sealed class DiagnosticLog
{
    private readonly string path;
    private readonly object gate = new();
    private int sequence;
    private DiagnosticLog(string path) => this.path = path;
    public static DiagnosticLog? Create(bool enabled, string executable = "ZhuyinToggle")
    {
        if (!enabled) return null;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhuyinToggle", "diagnostics");
            Directory.CreateDirectory(folder);
            var log = new DiagnosticLog(Path.Combine(folder, $"session-{Guid.NewGuid():N}.jsonl"));
            using (File.Create(log.path)) { }
            log.Write("start", new { sid = WindowsIdentity.GetCurrent().User?.Value, session = Process.GetCurrentProcess().SessionId,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), registryView = Environment.Is64BitProcess ? "Default (64-bit process)" : "Default (32-bit process)",
                executable, parentProcess = GetParentName(), launchContext = "使用 --diagnostics 啟動；確切啟動方式需依父程序及測試紀錄判定" });
            return log;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show("無法建立診斷紀錄，將不會執行診斷。", "ZhuyinToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }
    public int NextSequence() => Interlocked.Increment(ref sequence);
    public void Write(string name, object? detail = null)
    {
        try
        {
            string line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, tickMs = Environment.TickCount64, name, detail });
            lock (gate) File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
    public void Snapshot(int id, string stage, ImeSetting? expected = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryImeStore.Path);
            if (key is null || !key.GetValueNames().Contains(RegistryImeStore.Name)) { Write("registry", new { id, stage, status = "missing" }); return; }
            string kind = key.GetValueKind(RegistryImeStore.Name).ToString();
            object? value = key.GetValue(RegistryImeStore.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            string data = value is string s && (s == "0x00000000" || s == "0x00000001") ? s : "<other>";
            string? expectedData = expected switch { ImeSetting.Traditional => "0x00000000", ImeSetting.Simplified => "0x00000001", _ => null };
            Write("registry", new { id, stage, kind, data, expected = expectedData, differsFromExpected = expectedData is not null && data != expectedData,
                writerAttribution = "unknown; use ProcMon for attribution" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        { Write("registry", new { id, stage, error = ex.GetType().Name }); }
    }
    public void ScheduleSnapshots(int id, long writeTimestamp, ImeSetting? expected)
    {
        _ = Task.Run(async () =>
        {
            foreach (int target in new[] { 500, 1000, 3000 })
            {
                int delay = Math.Max(0, target - (int)Stopwatch.GetElapsedTime(writeTimestamp).TotalMilliseconds);
                await Task.Delay(delay).ConfigureAwait(false);
                Snapshot(id, $"post_{target}ms_actual_{(long)Stopwatch.GetElapsedTime(writeTimestamp).TotalMilliseconds}ms", expected);
            }
        });
    }
    private static string GetParentName()
    {
        try
        {
            var info = new ProcessBasicInformation();
            int status = NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            return status == 0 ? Process.GetProcessById(info.Parent.ToInt32()).ProcessName : "unknown";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException) { return "unknown"; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation { public IntPtr Reserved1, PebBase, Reserved2a, Reserved2b, ProcessId, Parent; }
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr handle, int informationClass, ref ProcessBasicInformation info, int size, out int returned);
}
