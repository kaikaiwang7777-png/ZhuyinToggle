using System.IO;
using System.Diagnostics;
using System.Windows.Automation.Text;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

internal static class Program { [STAThread] static void Main(string[] args) { if(args.Length!=1 || args[0] is not ("--focus" or "--write-only" or "--observe")) return; ApplicationConfiguration.Initialize(); Application.Run(new Controller(args[0]=="--focus",args[0]=="--observe",Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"TestHost.exe")))); } }
internal sealed class Controller:Form
{
    const string PathKey=@"SOFTWARE\Microsoft\IME\15.0\IMETC", ValueName="Enable Simplified Chinese Output", T="0x00000000", S="0x00000001";
    readonly bool focusMode, observe; readonly string hostPath; readonly System.Windows.Forms.Timer observeTimer=new(){Interval=250}; string lastObservation=""; readonly Label label=new(){Dock=DockStyle.Fill,Text="背景控制器：在測試宿主按 Win+Alt+C。結束請關閉本視窗。",AutoSize=false};
    readonly System.Windows.Forms.Timer timer=new(){Interval=1500};
    string? original, expected; bool registered, blocked, running;
    readonly CancellationTokenSource closing=new(); readonly object eventLock=new(); TaskCompletionSource<bool>? returnEvent; AutomationElement? returnTarget;
    readonly object logLock=new();
    readonly string diagnostic=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhuyinToggle","CrossProcessBackup","focus-diagnostic.log");
    AutomationFocusChangedEventHandler? focusHandler; volatile int watchedPid;
    void Trace(string message) { lock(logLock) { try { Directory.CreateDirectory(Path.GetDirectoryName(diagnostic)!); File.AppendAllText(diagnostic,$"ticks={Stopwatch.GetTimestamp()} frequency={Stopwatch.Frequency} {message}\r\n"); } catch(IOException) { } } }
    static string Describe(AutomationElement? e) { if(e is null)return "<null>"; try { var c=e.Current; return $"Name={c.Name}; AutomationId={c.AutomationId}; ProcessId={c.ProcessId}; NativeWindowHandle={c.NativeWindowHandle}"; } catch(ElementNotAvailableException) {return "<unavailable>";} }
    bool Inspect(string stage, IntPtr fg, uint last, AutomationElement target, bool allowFocusPending=false) {
        IntPtr nowFg=GetForegroundWindow(); uint nowLast=LastInput(); var actual=AutomationElement.FocusedElement; TraceHierarchy(stage,fg,target);
        bool foreground=nowFg==fg, idle=nowLast==last, same=actual is not null && Automation.Compare(actual,target);
        Trace($"{stage} foreground={(foreground?"PASS":"FAIL")} expected={fg} actual={nowFg}");
        Trace($"{stage} lastInput={(idle?"PASS":"FAIL")} before={last} after={nowLast} delta={unchecked(nowLast-last)} ms");
        Trace($"{stage} focus={(same?"PASS":"FAIL")} actual=[{Describe(actual)}] expected=[{Describe(target)}]");
        if(!foreground || !idle || (!same && !allowFocusPending)) throw new InvalidOperationException($"{stage}: 前景={(foreground?"PASS":"FAIL")} 輸入={(idle?"PASS":"FAIL")} 焦點={(same?"PASS":"FAIL")}；停止不搶回");
        return same;
    }
    void Check(string stage, IntPtr fg, uint last, AutomationElement target)=>Inspect(stage,fg,last,target);
    public Controller(bool focusMode,bool observe,string hostPath)
    {
        this.focusMode=focusMode;this.observe=observe;this.hostPath=hostPath;Text="跨程序測試控制器（僅測試宿主）";Width=620;Height=180;Controls.Add(label);
        Shown+=(_,_)=> {if(observe){observeTimer.Start();Log("唯讀觀察模式，不修改設定、不控制焦點。");return;}try{using var k=Open(false); original=expected=Read(k); var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZhuyinToggle","CrossProcessBackup");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,$"{Guid.NewGuid():N}.txt"),$"原始 REG_SZ={original}\r\n");registered=RegisterHotKey(Handle,1,0x4009,0x43);Log(registered?"已註冊熱鍵；先聚焦測試宿主並完成注音組字。":"註冊失敗，停止。");}catch(Exception ex){blocked=true;Log("初始化失敗："+ex.Message);}};
        observeTimer.Tick+=(_,_)=>Observe();
        focusHandler=(sender,e)=> { try { if(sender is AutomationElement element && watchedPid!=0 && element.Current.ProcessId==watchedPid) { Trace($"UIA FocusChanged [{Describe(element)}]"); lock(eventLock) { if(returnTarget is not null && Automation.Compare(element,returnTarget)) returnEvent?.TrySetResult(true); } } } catch(ElementNotAvailableException) { Trace("UIA FocusChanged: element unavailable"); } };
        try { Automation.AddAutomationFocusChangedEventHandler(focusHandler); Trace("UIA focus handler registered"); } catch(Exception ex) { Trace("UIA handler registration failed: "+ex.GetType().Name); }
        timer.Tick+=(_,_)=> {timer.Stop(); Execute();}; FormClosing+=(_,e)=> {timer.Stop(); observeTimer.Stop(); closing.Cancel(); if(!Restore()) { e.Cancel=true;MessageBox.Show("設定已被外部變更或無法還原；未覆寫，請檢查原值。", "需要人工處理");return;} if(registered)UnregisterHotKey(Handle,1); if(focusHandler is not null)Automation.RemoveAutomationFocusChangedEventHandler(focusHandler);};
    }
    void Log(string s){Trace(s);label.Text=s;}
    protected override void WndProc(ref Message m){if(m.Msg==0x0312 && m.WParam.ToInt32()==1 && !blocked && !running && registered){timer.Stop();timer.Start();Log("已接收；等待使用者停止輸入後檢查。 ");}base.WndProc(ref m);}
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
    async void Execute()
    {
        if(blocked||running||original is null||expected is null)return;
        running=true;
        try
        {
            uint last=LastInput(); if(unchecked((uint)Environment.TickCount-last)<1000)throw new InvalidOperationException("仍有使用者輸入，未操作");
            IntPtr fg=GetForegroundWindow();if(fg==IntPtr.Zero)throw new InvalidOperationException("無前景視窗");
            var root=AutomationElement.FromHandle(fg); if(root is null||root.Current.Name!="ZhuyinToggle Cross-Process TestHost")throw new InvalidOperationException("前景不是專用測試宿主");
            watchedPid=root.Current.ProcessId; using var process=System.Diagnostics.Process.GetProcessById(root.Current.ProcessId);if(!string.Equals(Path.GetFullPath(process.MainModule!.FileName),hostPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("程序路徑不符");
            var input=Unique(root,"ZT_TEST_INPUT",ControlType.Edit); var button=Unique(root,"ZT_TEST_TARGET",ControlType.Button);
            var proof=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"ZT_IME_READY"));if(proof.Count!=1)throw new InvalidOperationException("測試宿主沒有已提交組字證據");
            if(AutomationElement.FocusedElement is not AutomationElement current||!Automation.Compare(current,input))throw new InvalidOperationException("原焦點不是指定測試欄");
            TraceHierarchy("before",fg,input); var before=Snapshot(input);if(GetForegroundWindow()!=fg||LastInput()!=last)throw new InvalidOperationException("使用者已操作或前景變動");
            using var k=Open(true);var prior=Read(k);if(prior!=expected)throw new InvalidOperationException("設定遭外部修改");
            var next=prior==T?S:T;Trace($"registry before={prior}, kind=REG_SZ, target={next}");k.SetValue(ValueName,next,RegistryValueKind.String);expected=next;var readback=Read(k);Trace($"registry readback={readback}, match={readback==next}");if(readback!=next)throw new InvalidOperationException("回讀不符");
            if(GetForegroundWindow()!=fg||LastInput()!=last || !Automation.Compare(AutomationElement.FocusedElement,input))throw new InvalidOperationException("寫入後焦點或輸入發生變化");
            if(!focusMode){Log($"唯寫入 {prior} → {next} 已回讀；請在宿主以注音輸入測試詞。");return;}
            Trace("button.SetFocus begin"); button.SetFocus(); Trace("button.SetFocus returned");
            Check("synchronous-after-button",fg,last,button);
            // Let the test host process its focus events before the second guard.
            await Task.Yield(); closing.Token.ThrowIfCancellationRequested();
            Check("begininvoke-before-return",fg,last,button);
            var signal=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock(eventLock) { returnTarget=input; returnEvent=signal; }
            try {
                long started=Stopwatch.GetTimestamp(); Trace("input.SetFocus begin"); input.SetFocus(); Trace("input.SetFocus returned");
                bool focused=Inspect("return-immediate",fg,last,input,true);
                if(!focused) {
                    Trace("waiting for UIA input focus event, deadline=250ms");
                    while(true) {
                        closing.Token.ThrowIfCancellationRequested();
                        if(GetForegroundWindow()!=fg || LastInput()!=last) throw new InvalidOperationException("等待期間前景或輸入變動，停止");
                        using var alive=System.Diagnostics.Process.GetProcessById(watchedPid);
                        if(!string.Equals(Path.GetFullPath(alive.MainModule!.FileName),hostPath,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("測試程序身分變動");
                        double elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        if(elapsed>=250) throw new InvalidOperationException($"等待 UIA 焦點事件逾時，{elapsed:F2} ms");
                        if(signal.Task.IsCompleted && Inspect("return-event-confirm",fg,last,input,true)) break;
                        await Task.WhenAny(signal.Task.IsCompleted ? Task.Delay(10,closing.Token) : signal.Task, Task.Delay(10,closing.Token));
                    }
                }
                Check("return-final",fg,last,input); Trace($"return confirmed in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2} ms");
                var after=Snapshot(input); Trace($"returned selection before={before.start},{before.length} after={after.start},{after.length}; textEqual={before.value==after.value}"); bool textSame=before.value==after.value, selectSame=before.start==after.start&&before.length==after.length;
                if(!textSame||!selectSame)throw new InvalidOperationException($"焦點已返回，但文字不變={textSame}、選取不變={selectSame}；停止，不修正。");
                Log($"焦點往返完成，文字及選取不變；{prior} → {next} 已回讀。請以注音輸入測試詞。");
            } finally { lock(eventLock) { returnTarget=null; returnEvent=null; } }
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or ElementNotAvailableException or System.ComponentModel.Win32Exception or NotSupportedException or OperationCanceledException or ArgumentException){blocked=true;Log("停止："+ex.Message);}
        finally { running=false; }
    }
    bool Restore(){if(observe)return true;if(original is null||expected is null)return true;try{using var k=Open(true);if(Read(k)!=expected){Log("外部改動，未還原。");return false;}if(expected!=original)k.SetValue(ValueName,original,RegistryValueKind.String);return Read(k)==original;}catch(Exception ex){Log("無法還原："+ex.Message);return false;}}
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left,top,right,bottom; }
    [StructLayout(LayoutKind.Sequential)] struct GUI { public uint size,flags; public IntPtr active,focus,capture,menuOwner,moveSize,caret; public RECT caretRect; }
    string Hierarchy(IntPtr host, AutomationElement? target) {
        IntPtr fg=GetForegroundWindow(); uint fgPid,hostPid; uint tid=GetWindowThreadProcessId(host,out hostPid); GetWindowThreadProcessId(fg,out fgPid);
        var info=new GUI{size=(uint)Marshal.SizeOf<GUI>()}; bool ok=GetGUIThreadInfo(tid,ref info); var actual=AutomationElement.FocusedElement;
        IntPtr input=target is null?IntPtr.Zero:new IntPtr(target.Current.NativeWindowHandle);
        string focused=actual is null?"<null>":actual.Current.ProcessId==(int)hostPid?Describe(actual):$"external processId={actual.Current.ProcessId} handle={actual.Current.NativeWindowHandle} (redacted)";
        return $"host={host} hostRoot={GetAncestor(host,2)} hostPid={hostPid} tid={tid}; foreground={fg} foregroundRoot={GetAncestor(fg,2)} foregroundPid={fgPid}; guiOk={ok} active={info.active} activeRoot={GetAncestor(info.active,2)} focus={info.focus} focusRoot={GetAncestor(info.focus,2)}; target={input} targetRoot={GetAncestor(input,2)} targetIsChild={IsChild(host,input)}; UIA=[{focused}]";
    }
    void TraceHierarchy(string stage,IntPtr host,AutomationElement? target){Trace($"hierarchy {stage}: {Hierarchy(host,target)}");}
    void Observe(){try { using var proc=System.Diagnostics.Process.GetProcessesByName("TestHost").SingleOrDefault(x=>string.Equals(Path.GetFullPath(x.MainModule!.FileName),hostPath,StringComparison.OrdinalIgnoreCase)); if(proc is null||proc.MainWindowHandle==IntPtr.Zero)return; var root=AutomationElement.FromHandle(proc.MainWindowHandle); var input=Unique(root,"ZT_TEST_INPUT",ControlType.Edit); var result=Hierarchy(proc.MainWindowHandle,input); if(result!=lastObservation){lastObservation=result;Trace("observe "+result);} } catch(Exception ex) when(ex is InvalidOperationException or ElementNotAvailableException or System.ComponentModel.Win32Exception or ArgumentException) {Trace("observe error "+ex.GetType().Name);} }
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint tid,ref GUI info);
    [DllImport("user32.dll")]static extern bool IsChild(IntPtr parent,IntPtr child);
    [StructLayout(LayoutKind.Sequential)]struct LAST {public uint size,tick;}
    static uint LastInput(){var x=new LAST{size=(uint)Marshal.SizeOf<LAST>()};if(!GetLastInputInfo(ref x))throw new InvalidOperationException("無法檢查輸入活動");return x.tick;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LAST x);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
}

