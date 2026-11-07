using ZhuyinToggle;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

Check(Hotkey.Default.IsValid, "default shortcut should be valid");
Check(DiagnosticLog.Create(false) is null, "diagnostics must be opt-in");
Check(Hotkey.Default.ToString() == "Win + Alt + C", "default shortcut label");
Check(!new Hotkey(0, Keys.C).IsValid, "modifier is required");
Check(!new Hotkey(16, Keys.C).IsValid, "unknown modifier must be rejected");
Check(!new Hotkey(2, Keys.ShiftKey).IsValid, "modifier cannot be the main key");
Check(new Hotkey(6, Keys.F12).IsValid, "Ctrl+Shift+F12 should be valid");
foreach (int size in new[] { 16, 20, 32 })
foreach (var state in Enum.GetValues<OutputState>())
{
    using var icon = TrayIconFactory.Create(state, size);
    Check(icon.Width == size && icon.Height == size, "icon dimension");
    using var bitmap = icon.ToBitmap();
    var corner = bitmap.GetPixel(0, 0);
    Check(state switch {
        OutputState.Traditional => corner.B > corner.G && corner.B > corner.R,
        OutputState.Simplified => corner.G > corner.B && corner.G > corner.R,
        _ => Math.Abs(corner.R - corner.B) < 20
    }, "icon background color");
    int brightPixels = 0;
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.R > 200 && color.G > 200 && color.B > 200 && color.A > 0) brightPixels++;
        }
    Check(brightPixels > 0, "icon must contain a visible light glyph");
}
var registrations = new FakeRegistration();
bool failSave = false;
using (var manager = new HotkeyCoordinator(registrations, _ => { if (failSave) throw new IOException("simulated"); }))
{
    var first = Hotkey.Default;
    var next = new Hotkey(2, Keys.D);
    Check(manager.Start(first, out _), "initial registration");
    registrations.FailNext = true;
    Check(!manager.Change(next, out _), "conflict should fail");
    Check(manager.Active == first && registrations.Keys.Count == 1, "conflict must retain old shortcut");
    failSave = true;
    Check(!manager.Change(next, out _), "save error should fail");
    Check(manager.Active == first && registrations.Keys.Count == 1, "save failure must retain old shortcut");
    failSave = false;
    Check(manager.Change(next, out _), "change should succeed");
    Check(manager.Active == next && registrations.Keys.Count == 1 && registrations.Keys.Values.Single() == next, "new shortcut replaces old only after save");
}
Check(registrations.Keys.Count == 0, "dispose must release registration");
var store = new FakeImeStore();
var ime = new ImeModeService(store);
int changes = 0;
ime.Changed += () => changes++;
ime.Refresh();
Check(ime.Current == ImeSetting.Traditional, "initial IME setting");
Check(ime.Toggle(out _) && ime.Current == ImeSetting.Simplified, "toggle to simplified");
Check(ime.Toggle(out _) && ime.Current == ImeSetting.Traditional, "toggle back");
store.FailWrite = true;
Check(!ime.Toggle(out _) && ime.Current == ImeSetting.Unknown, "write failure is unknown");
store.FailWrite = false; store.IgnoreWrite = true;
Check(!ime.Toggle(out _) && ime.Current == ImeSetting.Unknown, "mismatched readback is unknown");
Check(changes == 5, "notifications for refresh and toggles");
Console.WriteLine("快捷鍵規則與交易式替換測試通過；尚未測試 Windows 快捷鍵註冊。");

sealed class FakeRegistration : IHotkeyRegistration
{
    public Dictionary<int, Hotkey> Keys { get; } = new();
    public bool FailNext { get; set; }
    public bool Register(int id, Hotkey key, out string error)
    {
        if (FailNext) { FailNext = false; error = "conflict"; return false; }
        Keys.Add(id, key); error = ""; return true;
    }
    public void Unregister(int id) => Keys.Remove(id);
}

sealed class FakeImeStore : IImeStore
{
    public ImeSetting Value = ImeSetting.Traditional;
    public bool FailWrite;
    public bool IgnoreWrite;
    public ImeSetting Read() => Value;
    public void Write(ImeSetting value)
    {
        if (FailWrite) throw new IOException("test failure");
        if (!IgnoreWrite) Value = value;
    }
}
