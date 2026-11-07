using ZhuyinToggle;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

Check(Hotkey.Default.IsValid, "default shortcut should be valid");
Check(Hotkey.Default.ToString() == "Win + Alt + C", "default shortcut label");
Check(!new Hotkey(0, Keys.C).IsValid, "modifier is required");
Check(!new Hotkey(16, Keys.C).IsValid, "unknown modifier must be rejected");
Check(!new Hotkey(2, Keys.ShiftKey).IsValid, "modifier cannot be the main key");
Check(new Hotkey(6, Keys.F12).IsValid, "Ctrl+Shift+F12 should be valid");
Console.WriteLine("快捷鍵規則測試通過；尚未測試 Windows 快捷鍵註冊。");
