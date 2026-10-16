using System;
using System.Runtime.InteropServices;

// 用到的 user32 系统调用都集中在这里，对外只提供带异常保护的方法：
// 这些调用只管焦点和「已在运行」的提示，失败时按没成功处理（返回 0 / 什么也不做），不影响复制等主要功能
static class NativeMethods
{
    public const int WM_MOUSEACTIVATE = 0x21;
    public const int MA_NOACTIVATE = 3;
    public const int HTCLIENT = 1;
    static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    // 当前的前台窗口；失败时返回 IntPtr.Zero
    public static IntPtr GetForeground()
    {
        try { return GetForegroundWindow(); }
        catch { return IntPtr.Zero; }
    }

    // 把窗口切到前台；失败时什么也不做
    public static void SetForeground(IntPtr hWnd)
    {
        try { SetForegroundWindow(hWnd); }
        catch { }
    }

    // 按名字注册（或取得）跨进程通用的窗口消息号；失败时返回 0
    public static int RegisterMessage(string name)
    {
        try { return RegisterWindowMessage(name); }
        catch { return 0; }
    }

    // 通知已经在运行的实例：允许它抢到前台，再广播按名字注册的消息；失败时什么也不做
    public static void NotifyRunningInstance(string messageName)
    {
        try
        {
            AllowSetForegroundWindow(-1);
            PostMessage(HWND_BROADCAST, RegisterWindowMessage(messageName), IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
