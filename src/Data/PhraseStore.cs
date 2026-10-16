using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 词条文件 phrases.txt（UTF-8，和 exe 放在同一文件夹，隐藏）：
// 开头是设置行（见 Settings，用量统计也存在这里），之后每行一条词条：中文|English|点击次数|钉住（后三项可省略）
// 负责读取、保存（临时文件 + 替换）、每日备份、空文件从备份恢复、默认词条迁移，以及导入/导出
class PhraseStore
{
    public const int MaxPhrases = 20;

    static readonly string[] Defaults =
    {
        "港币1000押金|HKD 1,000 deposit",
        "VAPB，预授权1000|VAPB, pre-auth 1,000",
        "请收取押金|Please collect the deposit",
        "加急，客人赶时间|Urgent – guest is in a hurry",
        "遗落物品，客人上去拿|Guest will go up to collect left items",
        "客人遗落物品不要了|Guest does not want the left items",
        "借用酒店的充电器在前台|Hotel charger on loan – return to front desk",
        "无视请勿打扰（DND），进行查房|Override DND – proceed with room check",
        "提前退房 {今天}|Early out {today}",
        "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}",
    };

    // 默认词条版本：升级后给老用户补上新增的默认词条（只补一次）
    const int DefaultsVersion = 3;
    static readonly string[] DefaultsV2 =
    {
        "提前退房 {今天}|Early out {today}",
        "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}",
    };

    public readonly List<Phrase> Phrases = new List<Phrase>();
    public readonly Settings Settings = new Settings();
    public readonly UsageStats Stats;
    readonly string file, backupFile;

    // 读写出错时的提示文字（界面上用红字显示）
    public event Action<string> Error;

    public PhraseStore(string dir)
    {
        Stats = new UsageStats(Settings);  // 统计的设置行登记在其他设置之后
        file = Path.Combine(dir, "phrases.txt");
        backupFile = Path.Combine(dir, "phrases.bak");
    }

    void OnError(string msg) { if (Error != null) Error(msg); }

    // phrases.txt 每行：中文|English|点击次数|钉住（后三项可省略）
    void ParseLines(string[] lines)
    {
        foreach (string line in lines)
        {
            if (Settings.TryRead(line)) continue;
            if (Settings.IsSettingLine(line)) continue;  // 未知设置行（如新版本写入的）跳过；「#1203 ...」这类词条照常读取
            string[] parts = line.Split('|');
            string zh = parts[0].Trim();
            if (zh.Length == 0) continue;
            string en = parts.Length > 1 ? parts[1].Trim() : "";
            int n = 0;
            if (parts.Length > 2) int.TryParse(parts[2].Trim(), out n);
            Phrase ph = new Phrase(zh, en, Math.Max(0, n));
            ph.Pinned = parts.Length > 3 && parts[3].Trim() == "1";
            Phrases.Add(ph);
        }
    }

    // 程序启动时读入。需要时顺带保存：从备份恢复、首次写入默认词条、补默认词条、首次启用统计
    public void Load()
    {
        try
        {
            bool recovered = false;
            if (File.Exists(file))
            {
                ParseLines(File.ReadAllLines(file, Encoding.UTF8));
                // 文件是空的（多半是保存时断电），而备份还在：从备份恢复
                if (new FileInfo(file).Length == 0 && File.Exists(backupFile))
                {
                    ParseLines(File.ReadAllLines(backupFile, Encoding.UTF8));
                    recovered = Phrases.Count > 0;
                }
            }
            else ParseLines(Defaults);
            if (recovered) { Save(); OnError("词条文件为空，已从备份恢复"); }
            if (!File.Exists(file)) { Settings.DefaultsVersion = DefaultsVersion; Save(); }
            else
            {
                if (Settings.DefaultsVersion < 2)
                {
                    foreach (string d in DefaultsV2)
                    {
                        string[] parts = d.Split('|');
                        if (!Exists(parts[0], null)) Phrases.Add(new Phrase(parts[0], parts[1], 0));
                    }
                }
                if (Settings.DefaultsVersion < 3)
                {
                    // v1.4 的固定 2 份 EBF 改为复制时输入份数（保留点击次数）
                    foreach (Phrase p in Phrases)
                        if (p.Zh == "加购2份自助早餐 {明天}" && p.En == "2 EBF {tomorrow}")
                        {
                            p.Zh = "加购{数量}份自助早餐 {明天}";
                            p.En = "{qty} EBF {tomorrow}";
                        }
                }
                if (Settings.DefaultsVersion < DefaultsVersion)
                {
                    Settings.DefaultsVersion = DefaultsVersion;
                    Save();
                }
                HideFile();  // 旧版本留下的可见 phrases.txt 也设为隐藏
            }
        }
        catch { OnError("读取 phrases.txt 失败"); }

        // 首次启用统计：用已有的点击次数估算累计次数和字数
        if (!Stats.Started)
        {
            Stats.StartFrom(Phrases);
            Save();
        }

        Sort(true);
        BackupDaily();
    }

    // 钉住的排最前（保持原顺序）；其余按点击次数从高到低（稳定排序）。byCount=false 时只把钉住的提到前面
    public void Sort(bool byCount)
    {
        List<Phrase> pinned = new List<Phrase>(), rest = new List<Phrase>();
        foreach (Phrase p in Phrases) (p.Pinned ? pinned : rest).Add(p);
        if (byCount)
        {
            for (int i = 1; i < rest.Count; i++)
            {
                Phrase cur = rest[i];
                int j = i - 1;
                while (j >= 0 && rest[j].Count < cur.Count) { rest[j + 1] = rest[j]; j--; }
                rest[j + 1] = cur;
            }
        }
        Phrases.Clear();
        Phrases.AddRange(pinned);
        Phrases.AddRange(rest);
    }

    // 每天第一次启动时把 phrases.txt 备份成隐藏的 phrases.bak
    void BackupDaily()
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length == 0) return;
            if (File.Exists(backupFile))
            {
                if (File.GetLastWriteTime(backupFile).Date >= DateTime.Today) return;
                File.SetAttributes(backupFile, FileAttributes.Normal);  // 隐藏文件不能被 File.Copy 覆盖
            }
            File.Copy(file, backupFile, true);
            File.SetLastWriteTime(backupFile, DateTime.Now);  // Copy 会沿用原文件时间，这里改成今天
            File.SetAttributes(backupFile, FileAttributes.Hidden);
        }
        catch { }
    }

    // phrases.txt 设为隐藏，文件夹里看起来只有一个 exe
    void HideFile()
    {
        try { File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.Hidden); }
        catch { }
    }

    public void Save()
    {
        try
        {
            List<string> lines = new List<string>();
            Settings.WriteTo(lines);
            foreach (Phrase p in Phrases)
                lines.Add(p.Zh + "|" + p.En + "|" + p.Count + (p.Pinned ? "|1" : ""));
            byte[] data = new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines.ToArray()) + "\r\n");

            // 先完整写入临时文件并刷到磁盘，再替换正式文件；中途断电也不会把 phrases.txt 写成半截
            string tmp = file + ".tmp";
            if (File.Exists(tmp)) { File.SetAttributes(tmp, FileAttributes.Normal); File.Delete(tmp); }
            using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (File.Exists(file))
            {
                File.SetAttributes(file, FileAttributes.Normal);  // 隐藏文件不能直接被替换
                try { File.Replace(tmp, file, null); }
                catch
                {
                    // 某些磁盘（如网络共享）不支持替换，退回为覆盖复制
                    File.Copy(tmp, file, true);
                    File.Delete(tmp);
                }
            }
            else File.Move(tmp, file);
        }
        catch { OnError("保存失败（目录只读？）"); }
        finally { if (File.Exists(file)) HideFile(); }
    }

    public bool Exists(string zh, Phrase except)
    {
        foreach (Phrase p in Phrases)
            if (p != except && p.Zh == zh) return true;
        return false;
    }

    // 导出：只导出词条，不含个人点击次数
    public void Export(string path)
    {
        List<string> lines = new List<string>();
        foreach (Phrase p in Phrases) lines.Add(p.Zh + "|" + p.En);
        File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
    }

    // 合并导入：已有的（中文相同）跳过，不删除现有词条，最多到 20 条；返回新增条数（不在这里保存）
    public int Import(string[] lines, out int dup, out int over)
    {
        int added = 0;
        dup = 0;
        over = 0;
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0 || Settings.IsSettingLine(line)) continue;
            string[] parts = line.Split('|');
            string zh = parts[0].Trim();
            if (zh.Length == 0) continue;
            string en = parts.Length > 1 ? parts[1].Trim() : "";
            if (Exists(zh, null)) { dup++; continue; }
            if (Phrases.Count >= MaxPhrases) { over++; continue; }
            Phrases.Add(new Phrase(zh, en, 0));
            added++;
        }
        return added;
    }
}
