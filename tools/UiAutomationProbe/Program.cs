using System.Diagnostics;
using System.Windows.Automation;
using System.Runtime.InteropServices;

// 只觀察已開啟的設定視窗；不啟動視窗、不移動焦點、不點擊或改設定。
// 名稱與 AutomationId 目前未知，必須人工判讀；絕不把它當成安全的控制器。
if ((args.Length != 1 && args.Length != 3) || !long.TryParse(args[0], out long hwnd) || hwnd <= 0 ||
    (args.Length == 3 && (args[1] != "--set" || (args[2] != "traditional" && args[2] != "simplified"))))
{ Console.WriteLine("請提供已觀察到的 Windows 設定視窗控制代碼；可選 --set traditional|simplified。未執行任何操作。"); return 2; }
try
{
    var root = AutomationElement.FromHandle(new IntPtr(hwnd));
    if (root is null) { Console.WriteLine("無法取得 UI Automation 根節點；停止。"); return 2; }
    string owner = Process.GetProcessById(root.Current.ProcessId).ProcessName;
    if (!string.Equals(owner, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && !string.Equals(owner, "SystemSettings", StringComparison.OrdinalIgnoreCase))
    { Console.WriteLine("視窗不是 Windows 設定程序；停止。"); return 2; }
    IntPtr foreground = Native.GetForegroundWindow();
    var condition = new AndCondition(
        new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemSettings_Language_CHTBopomofoIME_OutputLanguage_ComboBox"),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
    var matches = root.FindAll(TreeScope.Descendants, condition);
    if (matches.Count != 1) { Console.WriteLine("目標選項無法唯一辨識；未操作任何設定。"); return 1; }
    var combo = matches[0];
    if (!combo.TryGetCurrentPattern(SelectionPattern.Pattern, out object? raw))
    { Console.WriteLine("目標選項不支援讀取目前選取值；未操作任何設定。"); return 1; }
    var selected = ((SelectionPattern)raw).Current.GetSelection();
    if (selected.Length != 1) { Console.WriteLine("目前選取值不唯一；停止。"); return 1; }
    string name = selected[0].Current.Name;
    string[] supported = ["繁體中文", "简体中文", "繁体中文", "簡體中文", "Traditional Chinese", "Simplified Chinese"];
    if (!supported.Contains(name, StringComparer.OrdinalIgnoreCase)) { Console.WriteLine("目前選取值不在已知語言清單；停止。"); return 1; }
    Console.WriteLine($"已辨識 Bopomofo 輸出選項；設定頁選取值：{name}");
    if (args.Length == 3)
    {
        if (!combo.Current.IsEnabled || !combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? rawExpand))
        { Console.WriteLine("選項無法展開；未更改設定。"); return 1; }
        var expand = (ExpandCollapsePattern)rawExpand;
        string[] wanted = args[2] == "traditional" ? ["繁體中文", "繁体中文", "Traditional Chinese"] : ["簡體中文", "简体中文", "Simplified Chinese"];
        try
        {
            expand.Expand();
            var entries = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var candidates = entries.Cast<AutomationElement>().Where(x => wanted.Contains(x.Current.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1 || !candidates[0].Current.IsEnabled || !candidates[0].TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? rawItem))
            { Console.WriteLine("目標項目不唯一或不能選取；停止，未變更選項。"); return 1; }
            ((SelectionItemPattern)rawItem).Select();
            var result = ((SelectionPattern)raw).Current.GetSelection();
            if (result.Length != 1 || !wanted.Contains(result[0].Current.Name, StringComparer.OrdinalIgnoreCase))
            { Console.WriteLine("無法核對修改後選項，狀態未知；請手動確認。"); return 1; }
            Console.WriteLine($"設定頁已回報選取：{result[0].Current.Name}；尚未驗證實際注音輸出。");
        }
        finally { if (expand.Current.ExpandCollapseState == ExpandCollapseState.Expanded) expand.Collapse(); }
    }
    IntPtr after = Native.GetForegroundWindow();
    Console.WriteLine(foreground == IntPtr.Zero || after == IntPtr.Zero
        ? "無法取得前景視窗，不能判斷是否干擾焦點。"
        : $"觀察前後前景視窗相同：{foreground == after}");
    Console.WriteLine("設定頁狀態不代表實際輸出已驗證；此原型不適用正式產品。");
    return 0;
}

catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
{
    Console.WriteLine($"UI Automation 不可用（{ex.GetType().Name}）；未操作任何設定。");
    return 2;
}

static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
