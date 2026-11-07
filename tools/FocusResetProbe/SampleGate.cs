namespace FocusResetProbe;

// 僅在 UI 執行緒使用：阻止舊的 timer／回呼處理新樣本。
internal sealed class SampleGate
{
    public int Id { get; private set; }
    public int Revision { get; private set; }
    public bool Waiting { get; private set; }
    public int Begin() { Id++; Revision = 0; Waiting = true; return Id; }
    public int Changed() { if (Waiting) Revision++; return Revision; }
    public bool IsCurrent(int id) => id == Id;
    public bool CanJudge(int id, int revision) => Waiting && Id == id && Revision == revision;
    public bool TryClaim(int id, int revision) { if (!CanJudge(id, revision)) return false; Waiting = false; return true; }
    public void Invalidate() { Id++; Waiting = false; }
}
