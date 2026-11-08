namespace ZhuyinToggle;

internal interface IHotkeyRegistration
{
    bool Register(int id, Hotkey key, out string error);
    void Unregister(int id);
}

// 先保留原快捷鍵、試註冊新快捷鍵並持久化；成功後才釋放原快捷鍵。
internal sealed class HotkeyCoordinator(IHotkeyRegistration registration, Action<Hotkey> save) : IDisposable
{
    public Hotkey? Active { get; private set; }
    private int activeId;

    public bool Start(Hotkey key, out string error)
    {
        error = "";
        if (!key.IsValid) { error = "快捷鍵格式無效。"; return false; }
        if (!registration.Register(1, key, out error)) return false;
        activeId = 1;
        Active = key;
        return true;
    }

    public bool Change(Hotkey key, out string error)
    {
        error = "";
        if (!key.IsValid) { error = "請使用至少一個修飾鍵加英數字或 F1–F12。"; return false; }
        if (key == Active) return true;
        int candidateId = activeId == 1 ? 2 : 1;
        if (!registration.Register(candidateId, key, out error)) return false;
        try { save(key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            registration.Unregister(candidateId);
            error = "快捷鍵儲存失敗；原快捷鍵維持不變。";
            return false;
        }
        if (Active is not null) registration.Unregister(activeId);
        activeId = candidateId;
        Active = key;
        return true;
    }

    public void Dispose()
    {
        if (Active is not null) registration.Unregister(activeId);
        Active = null;
    }
}
