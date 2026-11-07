using System.Runtime.InteropServices;
using System.Diagnostics;
internal static class Program { [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new Host()); } }
internal sealed class TestText : TextBox
{
    public bool Composing, Candidate; public int Starts, Ends, Results;
    public Action? Changed;
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0302) return;
        if(m.Msg==0x010D){Composing=true;Starts++;} if(m.Msg==0x010E){Composing=false;Ends++;}
        if(m.Msg==0x010F && (m.LParam.ToInt64()&0x800)!=0) Results++;
        if(m.Msg==0x0282 && m.WParam.ToInt64()==5) Candidate=true;
        if(m.Msg==0x0282 && m.WParam.ToInt64()==4) Candidate=false;
        base.WndProc(ref m); if(m.Msg is 0x010D or 0x010E or 0x010F or 0x0282) Changed?.Invoke();
    }
    public bool Settled()
    {
        var h=ImmGetContext(Handle); if(h==IntPtr.Zero)return false;
        try { uint count; int comp=ImmGetCompositionStringW(h,8,IntPtr.Zero,0); uint bytes=ImmGetCandidateListCountW(h,out count); return comp==0 && count==0 && !Composing && !Candidate && Starts>0 && Ends>=Starts && Results>0 && (bytes>0 || Ends>0); }
        finally { ImmReleaseContext(Handle,h); }
    }
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr h);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr h,IntPtr imc);
    [DllImport("imm32.dll",CharSet=CharSet.Unicode)] static extern int ImmGetCompositionStringW(IntPtr h,int i,IntPtr b,int size);
    [DllImport("imm32.dll")] static extern uint ImmGetCandidateListCountW(IntPtr h,out uint count);
}
internal sealed class Host:Form
{
    readonly TestText box=new(){Name="DisposableInput",AccessibleName="ZT_TEST_INPUT",Width=580,Height=35,Font=new Font("Microsoft JhengHei UI",16)};
    readonly Button target=new(){Name="DisposableTarget",AccessibleName="ZT_TEST_TARGET",Text="測試焦點目標",Width=130};
    readonly Label state=new(){AccessibleName="ZT_IME_UNKNOWN",Text="IME 狀態未確認",AutoSize=true};
    readonly TextBox log=new(){AccessibleName="ZT_TEST_LOG",ReadOnly=true,Multiline=true,Width=650,Height=250,ScrollBars=ScrollBars.Vertical};
    public Host()
    {
        Text="ZhuyinToggle Cross-Process TestHost"; Width=750; Height=480; StartPosition=FormStartPosition.CenterScreen;
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};
        panel.Controls.Add(new Label{Text="專用可丟棄文字；請勿輸入私人資料。",AutoSize=true}); panel.Controls.Add(box);panel.Controls.Add(target);panel.Controls.Add(state);panel.Controls.Add(log);Controls.Add(panel);
        box.Enter+=(_,_)=>Record("輸入框取得焦點");box.Leave+=(_,_)=>Record("輸入框失去焦點");
        target.Enter+=(_,_)=>Record("按鈕取得焦點");target.Leave+=(_,_)=>Record("按鈕失去焦點");
        box.Changed+=()=>BeginInvoke(UpdateState);box.TextChanged+=(_,_)=>UpdateState();
    }
    void UpdateState(){bool ready=box.Settled() && Profile.IsBopomofo(out _); state.AccessibleName=ready?"ZT_IME_READY":"ZT_IME_BUSY";state.Text=ready?"已觀察組字提交":"組字或狀態未知";}
    void Record(string text) { log.AppendText($"ticks={Stopwatch.GetTimestamp()} frequency={Stopwatch.Frequency} {text}；文字長度={box.TextLength}，選取={box.SelectionStart},{box.SelectionLength}；IME開始/結束/結果={box.Starts}/{box.Ends}/{box.Results}。\r\n"); }
}

internal static class Profile
{
    [StructLayout(LayoutKind.Sequential)] struct InputProfile { public uint type; public ushort language; public Guid clsid, profile, category; public IntPtr substitute; public uint caps; public IntPtr hkl; public uint flags; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetActive(IntPtr self, ref Guid category, out InputProfile profile);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr value);
    public static bool IsBopomofo(out string detail)
    {
        detail = "未知"; IntPtr ptr = IntPtr.Zero;
        try
        {
            Guid cls = new("33C53A50-F456-4884-B049-85FD643ECFED"), iid = new("71C6E74C-0F28-11D8-A82A-00065B84435C"), cat = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
            if (CoCreateInstance(ref cls, IntPtr.Zero, 1, ref iid, out ptr) != 0 || ptr == IntPtr.Zero) return false;
            var vtable = Marshal.ReadIntPtr(ptr); var f = Marshal.GetDelegateForFunctionPointer<GetActive>(Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size));
            int hr = f(ptr, ref cat, out var p); detail = $"HRESULT=0x{hr:X8}, lang=0x{p.language:X4}, clsid={p.clsid}, profile={p.profile}";
            return hr == 0 && p.language == 0x0404 && p.clsid == new Guid("B115690A-EA02-48D5-A231-E3578D2FDF80") && p.profile == new Guid("B2F9C502-1742-11D4-9790-0080C882687E");
        }
        catch (Exception ex) when (ex is COMException or ArgumentException) { detail = ex.Message; return false; }
        finally { if (ptr != IntPtr.Zero) Marshal.Release(ptr); }
    }
}
