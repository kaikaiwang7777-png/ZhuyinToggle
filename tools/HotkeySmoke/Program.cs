using System.Runtime.InteropServices;

// 實機診斷工具：只短暫註冊，不攔截或注入鍵盤，超時和結束時均解除。
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using var window = new Receiver();
        if (!Native.RegisterHotKey(window.Handle, 1, 0x4009, (uint)Keys.C))
        { Console.WriteLine($"DEFAULT_REGISTER_FAIL {Marshal.GetLastWin32Error()}"); return 2; }
        bool duplicate = Native.RegisterHotKey(window.Handle, 2, 0x4009, (uint)Keys.C);
        Console.WriteLine(duplicate ? "CONFLICT_UNEXPECTED_SUCCESS" : $"CONFLICT_REJECTED {Marshal.GetLastWin32Error()}");
        if (duplicate) Native.UnregisterHotKey(window.Handle, 2);
        bool custom = Native.RegisterHotKey(window.Handle, 3, 0x4006, (uint)Keys.F11);
        Console.WriteLine(custom ? "CUSTOM_REGISTERED" : $"CUSTOM_REGISTER_FAIL {Marshal.GetLastWin32Error()}");
        Console.WriteLine("READY: Win+Alt+C then Ctrl+Shift+F11; timeout 60s");
        using var timer = new System.Windows.Forms.Timer { Interval = 60000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        try { Application.Run(); }
        finally { timer.Stop(); Native.UnregisterHotKey(window.Handle, 1); if (custom) Native.UnregisterHotKey(window.Handle, 3); }
        Console.WriteLine($"RESULT default={window.DefaultSeen} custom={window.CustomSeen} conflictRejected={!duplicate}");
        return window.DefaultSeen && window.CustomSeen && !duplicate ? 0 : 1;
    }
    private sealed class Receiver : NativeWindow, IDisposable
    {
        public bool DefaultSeen { get; private set; }
        public bool CustomSeen { get; private set; }
        public Receiver() => CreateHandle(new CreateParams());
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 1) { DefaultSeen = true; Console.WriteLine("DEFAULT_HOTKEY_RECEIVED"); }
                if (m.WParam.ToInt32() == 3) { CustomSeen = true; Console.WriteLine("CUSTOM_HOTKEY_RECEIVED"); }
                if (DefaultSeen && CustomSeen) Application.ExitThread();
            }
            base.WndProc(ref m);
        }
        public void Dispose() => DestroyHandle();
    }
    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
