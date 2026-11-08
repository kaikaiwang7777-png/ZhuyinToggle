using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace ZhuyinToggle;

internal enum ImeSetting { Unknown, Traditional, Simplified }

internal interface IImeStore
{
    ImeSetting Read();
    void Write(ImeSetting setting);
}

internal sealed class RegistryImeStore : IImeStore
{
    internal const string Path = @"SOFTWARE\Microsoft\IME\15.0\IMETC";
    internal const string Name = "Enable Simplified Chinese Output";
    public ImeSetting Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path) ?? throw new InvalidOperationException("找不到微軟注音設定機碼。");
        if (!key.GetValueNames().Contains(Name)) throw new InvalidOperationException("找不到繁簡設定值。");
        if (key.GetValueKind(Name) != RegistryValueKind.String) throw new InvalidOperationException("繁簡設定不是 REG_SZ。");
        return key.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
        {
            "0x00000000" => ImeSetting.Traditional,
            "0x00000001" => ImeSetting.Simplified,
            _ => throw new InvalidOperationException("繁簡設定資料格式不受支援。")
        };
    }
    public void Write(ImeSetting setting)
    {
        if (setting == ImeSetting.Unknown) throw new ArgumentException("不可寫入未知狀態。");
        using var key = Registry.CurrentUser.OpenSubKey(Path, true) ?? throw new InvalidOperationException("找不到微軟注音設定機碼。");
        if (!key.GetValueNames().Contains(Name) || key.GetValueKind(Name) != RegistryValueKind.String)
            throw new InvalidOperationException("設定不存在或類型不是 REG_SZ。 ");
        var value = key.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        if (value is not ("0x00000000" or "0x00000001")) throw new InvalidOperationException("繁簡設定資料格式不受支援。 ");
        key.SetValue(Name, setting == ImeSetting.Simplified ? "0x00000001" : "0x00000000", RegistryValueKind.String);
    }
}

internal sealed class ImeModeService(IImeStore store, DiagnosticLog? diagnostics = null)
{
    private readonly object gate = new();
    public event Action? Changed;
    public ImeSetting Current { get; private set; } = ImeSetting.Unknown;
    public string? Error { get; private set; }
    public void Refresh()
    {
        lock (gate)
        {
            try { Current = store.Read(); Error = null; diagnostics?.Write("service_read", new { status = "ok", source = "IImeStore.Read", value = Current.ToString() }); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { Current = ImeSetting.Unknown; Error = ex.Message; diagnostics?.Write("service_read", new { status = "error", source = "IImeStore.Read", error = ex.GetType().Name }); }
        }
        Changed?.Invoke();
    }
    public bool Toggle(out string? error)
    {
        int id = diagnostics?.NextSequence() ?? 0;
        bool writeAttempted = false;
        long writeTick = 0;
        ImeSetting? expected = null;
        lock (gate)
        {
            try
            {
                diagnostics?.Snapshot(id, "before");
                var prior = store.Read();
                if (prior == ImeSetting.Unknown) throw new InvalidOperationException("無法確認目前設定。 ");
                var desired = prior == ImeSetting.Traditional ? ImeSetting.Simplified : ImeSetting.Traditional;
                expected = desired;
                diagnostics?.Write("write_attempt", new { id, prior = prior.ToString(), target = desired.ToString() });
                writeAttempted = true;
                store.Write(desired);
                writeTick = System.Diagnostics.Stopwatch.GetTimestamp();
                diagnostics?.Write("write_returned", new { id, success = true });
                diagnostics?.Snapshot(id, "immediate", desired);
                if (store.Read() != desired) throw new InvalidOperationException("寫入後回讀不一致。 ");
                Current = desired; Error = null; error = null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { diagnostics?.Write("toggle_error", new { id, type = ex.GetType().Name, writeAttempted }); Current = ImeSetting.Unknown; Error = ex.Message; error = ex.Message; }
        }
        if (writeAttempted) diagnostics?.ScheduleSnapshots(id, writeTick == 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : writeTick, expected);
        Changed?.Invoke();
        return error is null;
    }
}

internal sealed class RegistryChangeWatcher : IDisposable
{
    private readonly CancellationTokenSource cancel = new();
    private readonly Task worker;
    private readonly DiagnosticLog? diagnostics;
    public RegistryChangeWatcher(Action changed, DiagnosticLog? diagnostics = null)
    {
        this.diagnostics = diagnostics;
        worker = Task.Run(() => Watch(changed));
    }
    private void Watch(Action changed)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryImeStore.Path, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.Notify)
                    ?? throw new IOException("找不到設定機碼。");
                using var signal = new AutoResetEvent(false);
                while (!cancel.IsCancellationRequested)
                {
                    int result = RegNotifyChangeKeyValue(key.Handle, false, 4, signal.SafeWaitHandle, true);
                    if (result != 0) throw new IOException($"登錄通知失敗：{result}");
                    if (WaitHandle.WaitAny([signal, cancel.Token.WaitHandle]) != 0) return;
                    diagnostics?.Write("registry_notification", new { source = "RegNotifyChangeKeyValue", attribution = "unknown" });
                    diagnostics?.Snapshot(0, "notification");
                    changed();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                diagnostics?.Write("watcher_error", new { type = ex.GetType().Name });
                changed();
                if (cancel.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5))) return;
            }
        }
    }
    public void Dispose() { cancel.Cancel(); worker.GetAwaiter().GetResult(); cancel.Dispose(); }
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool subtree, uint filter, SafeWaitHandle signal, [MarshalAs(UnmanagedType.Bool)] bool async);
}
