using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Starport Haiku（固定剪贴板）：点卡片复制中文，点 EN 复制英文；窗口置顶，点击卡片不抢目标软件焦点
static class Program
{
    public const string Website = "starport1116.com";

    // 同一个文件夹只允许运行一个实例（两个实例同时写 phrases.txt 会互相覆盖）；
    // 也用作「已在运行，把窗口调到前面」的窗口消息名
    public static string InstanceKey;

    static string MakeInstanceKey()
    {
        string path = AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant();
        uint h = 2166136261;  // FNV-1a，跨进程稳定
        foreach (char ch in path) { h ^= ch; h *= 16777619; }
        return "StarportFixedClipboard_" + h.ToString("X8");  // 沿用旧名，新旧版本放同一文件夹也不会同时运行
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0) return SelfTest.Run();
        InstanceKey = MakeInstanceKey();
        Mutex mutex = null;
        bool first = true;
        try { mutex = new Mutex(true, "Local\\" + InstanceKey, out first); } catch { first = true; }
        if (!first)
        {
            // 已经在运行：通知原来的窗口显示到最前面，然后退出
            NativeMethods.NotifyRunningInstance(InstanceKey);
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        PhraseStore store = new PhraseStore(AppDomain.CurrentDomain.BaseDirectory);  // 词条文件和 exe 放在同一文件夹
        if (!store.Unlock(PasswordDialog.AskToUnlock)) return 0;  // 开了启动密码却不输：直接退出，不碰文件
        Application.Run(new MainForm(store));
        GC.KeepAlive(mutex);
        return 0;
    }

    // 版本号来自编译时打包进 exe 的 VERSION 文件（见 build.bat 的 /resource:VERSION,version.txt）
    public static string ReadVersion()
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("version.txt"))
            {
                if (s == null) return "";
                using (StreamReader r = new StreamReader(s)) return r.ReadToEnd().Trim();
            }
        }
        catch { return ""; }
    }
}
