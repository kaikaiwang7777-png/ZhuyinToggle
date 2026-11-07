using System.IO;
using System.Windows.Automation.Text;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

internal static class Program { [STAThread] static void Main(string[] args) { if(args.Length!=1 || args[0] is not ("--focus" or "--write-only")) return; ApplicationConfiguration.Initialize(); Application.Run(new Controller(args[0]=="--focus",Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"TestHost.exe")))); } }
internal sealed class Controller:Form
{
    const string PathKey=@"SOFTWARE\Microsoft\IME\15.0\IMETC", ValueName="Enable Simplified Chinese Output", T="0x00000000", S="0x00000001";
    readonly bool focusMode; readonly string hostPath; readonly Label label=new(){Dock=DockStyle.Fill,Text="背景控制器：在測試宿主按 Win+Alt+C。結束請關閉本視窗。",AutoSize=false};
    readonly System.Windows.Forms.Timer timer=new(){Interval=1500};
    string? original, expected; bool registered, blocked;
    public Controller(bool focusMode,string hostPath)
    {
        this.focusMode=focusMode;this.hostPath=hostPath;Text="跨程序測試控制器（僅測試宿主）";Width=620;Height=180;Controls.Add(label);
        Shown+=(_,_)=> {try{using var k=Open(false); original=expected=Read(k); var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhuyinToggle","CrossProcessBackup");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,$"{Guid.NewGuid():N}.txt"),$"原始 REG_SZ={original}\r\n");registered=RegisterHotKey(Handle,1,0x4009,0x43);Log(registered?"已註冊熱鍵；先聚焦測試宿主並完成注音組字。":"註冊失敗，停止。");}catch(Exception ex){blocked=true;Log("初始化失敗："+ex.Message);}};
        timer.Tick+=(_,_)=> {timer.Stop(); Execute();}; FormClosing+=(_,e)=> {timer.Stop(); if(!Restore()) { e.Cancel=true;MessageBox.Show("設定已被外部變更或無法還原；未覆寫，請檢查原值。", "需要人工處理");return;} if(registered)UnregisterHotKey(Handle,1);};
    }
    void Log(string s){label.Text=s;}
    protected override void WndProc(ref Message m){if(m.Msg==0x0312 && m.WParam.ToInt32()==1 && !blocked && registered){timer.Stop();timer.Start();Log("已接收；等待使用者停止輸入後檢查。 ");}base.WndProc(ref m);}
    static RegistryKey Open(bool write)=>Registry.CurrentUser.OpenSubKey(PathKey,write)??throw new InvalidOperationException("設定不存在");
    static string Read(RegistryKey k){if(!k.GetValueNames().Contains(ValueName)||k.GetValueKind(ValueName)!=RegistryValueKind.String)throw new InvalidOperationException("設定不是 REG_SZ");var s=k.GetValue(ValueName) as string;return s is T or S?s:throw new InvalidOperationException("資料不正確");}
    static AutomationElement Unique(AutomationElement root,string name,ControlType type){var all=root.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.NameProperty,name),new PropertyCondition(AutomationElement.ControlTypeProperty,type))); if(all.Count!=1)throw new InvalidOperationException("控制項無法唯一辨識");return all[0];}
    static (string value,int start,int length) Snapshot(AutomationElement e)
    {
        if(!e.TryGetCurrentPattern(ValuePattern.Pattern,out object value) || !e.TryGetCurrentPattern(TextPattern.Pattern,out object text))throw new InvalidOperationException("無法無侵入式核對文字與選取");
        var pattern=(TextPattern)text;var selections=pattern.GetSelection();if(selections.Length!=1)throw new InvalidOperationException("選取範圍不唯一");
        var prefix=pattern.DocumentRange.Clone();prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,selections[0],TextPatternRangeEndpoint.Start);
        return (((ValuePattern)value).Current.Value,prefix.GetText(-1).Length,selections[0].GetText(-1).Length);
    }
    void Execute()
    {
        if(blocked||original is null||expected is null)return;
        try
        {
            uint last=LastInput(); if(unchecked((uint)Environment.TickCount-last)<1000)throw new InvalidOperationException("仍有使用者輸入，未操作");
            IntPtr fg=GetForegroundWindow();if(fg==IntPtr.Zero)throw new InvalidOperationException("無前景視窗");
            var root=AutomationElement.FromHandle(fg); if(root is null||root.Current.Name!="ZhuyinToggle Cross-Process TestHost")throw new InvalidOperationException("前景不是專用測試宿主");
            using var process=System.Diagnostics.Process.GetProcessById(root.Current.ProcessId);if(!string.Equals(Path.GetFullPath(process.MainModule!.FileName),hostPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("程序路徑不符");
            var input=Unique(root,"ZT_TEST_INPUT",ControlType.Edit); var button=Unique(root,"ZT_TEST_TARGET",ControlType.Button);
            var proof=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"ZT_IME_READY"));if(proof.Count!=1)throw new InvalidOperationException("測試宿主沒有已提交組字證據");
            if(AutomationElement.FocusedElement is not AutomationElement current||!Automation.Compare(current,input))throw new InvalidOperationException("原焦點不是指定測試欄");
            var before=Snapshot(input);if(GetForegroundWindow()!=fg||LastInput()!=last)throw new InvalidOperationException("使用者已操作或前景變動");
            using var k=Open(true);var prior=Read(k);if(prior!=expected)throw new InvalidOperationException("設定遭外部修改");
            var next=prior==T?S:T;k.SetValue(ValueName,next,RegistryValueKind.String);expected=next;if(Read(k)!=next)throw new InvalidOperationException("回讀不符");
            if(GetForegroundWindow()!=fg||LastInput()!=last || !Automation.Compare(AutomationElement.FocusedElement,input))throw new InvalidOperationException("寫入後焦點或輸入發生變化");
            if(!focusMode){Log($"唯寫入 {prior} → {next} 已回讀；請在宿主以注音輸入測試詞。");return;}
            button.SetFocus();
            if(GetForegroundWindow()!=fg||LastInput()!=last||!Automation.Compare(AutomationElement.FocusedElement,button))throw new InvalidOperationException("無法確認目標焦點，停止不搶回");
            // 焦點操作後放回訊息循環，並核對使用者與焦點未變。
            BeginInvoke(()=> { try {
                if(GetForegroundWindow()!=fg||LastInput()!=last||!Automation.Compare(AutomationElement.FocusedElement,button))throw new InvalidOperationException("焦點或使用者輸入已變，不搶回");
                input.SetFocus();
                if(GetForegroundWindow()!=fg||!Automation.Compare(AutomationElement.FocusedElement,input))throw new InvalidOperationException("未能恢復焦點");
                var after=Snapshot(input); bool textSame=before.value==after.value, selectSame=before.start==after.start&&before.length==after.length;
                if(!textSame||!selectSame)throw new InvalidOperationException($"焦點已返回，但文字不變={textSame}、選取不變={selectSame}；停止，不修正。");
                Log($"焦點往返完成，文字及選取不變；{prior} → {next} 已回讀。請以注音輸入測試詞。");
            } catch(Exception ex){blocked=true;Log("停止："+ex.Message);} });
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or ElementNotAvailableException or System.ComponentModel.Win32Exception or NotSupportedException){blocked=true;Log("停止："+ex.Message);}
    }
    bool Restore(){if(original is null||expected is null)return true;try{using var k=Open(true);if(Read(k)!=expected){Log("外部改動，未還原。");return false;}if(expected!=original)k.SetValue(ValueName,original,RegistryValueKind.String);return Read(k)==original;}catch(Exception ex){Log("無法還原："+ex.Message);return false;}}
    [StructLayout(LayoutKind.Sequential)]struct LAST {public uint size,tick;}
    static uint LastInput(){var x=new LAST{size=(uint)Marshal.SizeOf<LAST>()};if(!GetLastInputInfo(ref x))throw new InvalidOperationException("無法檢查輸入活動");return x.tick;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LAST x);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
}

