using System;
using System.Collections.Generic;
using System.Drawing;

// phrases.txt 开头的设置行，例如 #topmost=1。
// 每个设置项只在这里登记一次，读法和写法写在一起；读入（TryRead）和保存（WriteTo）都按登记表走。
// 登记顺序就是保存时的写出顺序。
class Settings
{
    class Item
    {
        public string Prefix;        // "#key="
        public Action<string> Read;  // 参数是 = 后面的原文
        public Func<string> Write;   // 返回 null 表示这次不写这一行
    }

    readonly List<Item> items = new List<Item>();

    public bool TopMost = true;
    public bool UserMode;            // 用户模式：复制时自动加后缀，界面不显示
    public string UserSuffix = "";
    public int LastQty = 2;
    public int DefaultsVersion = 1;  // 已经补到第几版默认词条，见 PhraseStore
    public Point WinPos;             // 上次关闭/拖动后的窗口位置
    public bool HasWinPos;

    public Settings()
    {
        Register("topmost",
            delegate(string v) { TopMost = v.Trim() != "0"; },
            delegate { return TopMost ? "1" : "0"; });
        Register("user_mode",
            delegate(string v) { UserMode = v.Trim() == "1"; },
            delegate { return UserMode ? "1" : "0"; });
        Register("user_suffix",
            delegate(string v) { UserSuffix = v.Trim(); },
            delegate { return UserSuffix; });
        Register("day_cutoff",
            delegate(string v) { int.TryParse(v.Trim(), out DateTokens.CutoffHour); },
            delegate { return DateTokens.CutoffHour.ToString(); });
        Register("last_qty",
            delegate(string v) { int.TryParse(v.Trim(), out LastQty); },
            delegate { return LastQty.ToString(); });
        Register("defaults",
            delegate(string v) { int.TryParse(v.Trim(), out DefaultsVersion); },
            delegate { return DefaultsVersion.ToString(); });
        Register("win_pos",
            delegate(string v)
            {
                string[] xy = v.Split(',');
                int x, y;
                if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y))
                {
                    WinPos = new Point(x, y);
                    HasWinPos = true;
                }
            },
            delegate { return HasWinPos ? WinPos.X + "," + WinPos.Y : null; });
    }

    // 登记一个设置项（其他类也可以登记自己的，例如用量统计）
    public void Register(string key, Action<string> read, Func<string> write)
    {
        Item it = new Item();
        it.Prefix = "#" + key + "=";
        it.Read = read;
        it.Write = write;
        items.Add(it);
    }

    // 是登记过的设置行就读入并返回 true
    public bool TryRead(string line)
    {
        foreach (Item it in items)
        {
            if (line.StartsWith(it.Prefix))
            {
                it.Read(line.Substring(it.Prefix.Length));
                return true;
            }
        }
        return false;
    }

    // 按登记顺序写出全部设置行
    public void WriteTo(List<string> lines)
    {
        foreach (Item it in items)
        {
            string v = it.Write();
            if (v != null) lines.Add(it.Prefix + v);
        }
    }

    // 设置行格式：# + 小写英文/下划线 + =，例如 #topmost=1
    public static bool IsSettingLine(string line)
    {
        int eq = line.IndexOf('=');
        if (!line.StartsWith("#") || eq < 2) return false;
        for (int i = 1; i < eq; i++)
        {
            char ch = line[i];
            if (!((ch >= 'a' && ch <= 'z') || ch == '_')) return false;
        }
        return true;
    }
}
