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
    FileCrypto crypto;  // 开了启动密码时不为 null：读写都经过加密
    bool locked;        // 文件加了密码却没解开：不读也不写，免得覆盖掉加密的内容

    // 读写出错时的提示文字（界面上用红字显示）
    public event Action<string> Error;

    public PhraseStore(string dir) : this(dir, null) { }

    public PhraseStore(string dir, string localBackupDir)
    {
        Stats = new UsageStats(Settings);  // 统计的设置行登记在其他设置之后
        file = Path.Combine(dir, "phrases.txt");
        backupFile = Path.Combine(dir, "phrases.bak");
    }

    void OnError(string msg) { if (Error != null) Error(msg); }

    public bool PasswordEnabled { get { return crypto != null; } }

    static byte[] ReadIfExists(string path) { return File.Exists(path) ? File.ReadAllBytes(path) : null; }

    // 开了启动密码时（词条文件或备份是加密的），程序启动先用密码解开。
    // ask(第几次) 返回输入的密码，返回 null 表示放弃：这时返回 false，程序直接退出，不改动任何文件。
    // 没开密码时直接返回 true。
    public bool Unlock(Func<int, string> ask)
    {
        byte[] main = ReadIfExists(file), bak = ReadIfExists(backupFile);
        bool mainEnc = FileCrypto.IsEncrypted(main), bakEnc = FileCrypto.IsEncrypted(bak);
        if (!mainEnc && !bakEnc) return true;
        // 两份都坏到连格式都不完整：没法核对密码，只能用输入的密码重新开始
        bool checkable = FileCrypto.IsWellFormed(main) || FileCrypto.IsWellFormed(bak);
        for (int attempt = 1; ; attempt++)
        {
            string password = ask(attempt);
            if (password == null) return false;
            FileCrypto c = null;
            byte[] plain;
            // 主文件被截断时它校验不过，用备份核对密码，读入时再从备份恢复
            if ((mainEnc && FileCrypto.TryOpen(main, password, ref c, out plain)) ||
                (bakEnc && FileCrypto.TryOpen(bak, password, ref c, out plain)))
            {
                crypto = c;
                return true;
            }
            if (!checkable)
            {
                crypto = FileCrypto.Create(password);
                return true;
            }
        }
    }

    // 文件内容（加密的先解密）；加密的文件解不开（损坏）时返回 null
    byte[] ReadPlain(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!FileCrypto.IsEncrypted(data)) return data;
        byte[] plain;
        return crypto != null && crypto.TryDecrypt(data, out plain) ? plain : null;
    }

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

    // 与 File.ReadAllLines(path, Encoding.UTF8) 相同的分行方式（自动识别 BOM）
    static string[] ToLines(byte[] data)
    {
        List<string> lines = new List<string>();
        using (StreamReader r = new StreamReader(new MemoryStream(data), Encoding.UTF8, true))
        {
            string line;
            while ((line = r.ReadLine()) != null) lines.Add(line);
        }
        return lines.ToArray();
    }

    static bool EndsWithNewline(byte[] data) { return data.Length > 0 && (data[data.Length - 1] == '\n' || data[data.Length - 1] == '\r'); }

    // 读入全部行，返回其中完整词条的条数：程序保存的文件总以换行结尾，
    // 没有换行结尾时最后一行可能是截断留下的半行，即使读成了词条也不算完整
    int ParseLinesCountingWhole(string[] lines, bool endsWithNewline)
    {
        if (endsWithNewline || lines.Length == 0)
        {
            ParseLines(lines);
            return Phrases.Count;
        }
        string[] head = new string[lines.Length - 1];
        Array.Copy(lines, head, head.Length);
        ParseLines(head);
        int whole = Phrases.Count;
        ParseLines(new string[] { lines[lines.Length - 1] });
        return whole;
    }

    // 程序启动时读入。需要时顺带保存：从备份恢复、首次写入默认词条、补默认词条、首次启用统计
    public void Load()
    {
        // 加了密码却没有先 Unlock：什么都不做（之后的保存也会跳过），绝不覆盖加密的文件
        if (crypto == null && (FileCrypto.IsEncrypted(ReadIfExists(file)) || FileCrypto.IsEncrypted(ReadIfExists(backupFile))))
        {
            locked = true;
            OnError("词条文件已加密，需要输入启动密码");
            return;
        }
        try
        {
            bool recovered = false, fillDefaults = false;
            if (File.Exists(file))
            {
                byte[] data = ReadPlain(file);  // 加密文件解不开（损坏）时为 null，当作空文件
                string[] lines = data != null ? ToLines(data) : new string[0];
                int whole = data != null ? ParseLinesCountingWhole(lines, EndsWithNewline(data)) : 0;
                // 一条完整的词条也读不出来（空文件，或保存时断电被截断成半截），而备份还在：从备份恢复
                if (whole == 0 && File.Exists(backupFile) && new FileInfo(backupFile).Length > 0)
                {
                    byte[] bak = ReadPlain(backupFile);
                    if (bak != null)
                    {
                        Phrases.Clear();  // 丢掉截断留下的半行
                        ParseLines(ToLines(bak));
                        recovered = Phrases.Count > 0;
                    }
                }
                // 空文件、又没能从备份恢复：和第一次运行一样写入完整的默认词条
                fillDefaults = !recovered && lines.Length == 0;
                if (data == null && !recovered) OnError("词条文件已损坏，无法解密");
            }
            else ParseLines(Defaults);
            if (recovered) { Save(); OnError("词条文件为空或已损坏，已从备份恢复"); }
            if (fillDefaults) ParseLines(Defaults);
            if (!File.Exists(file) || fillDefaults) { Settings.DefaultsVersion = DefaultsVersion; Save(); }
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
                // 当前文件比备份小，可能是被截断的坏文件：不能用它覆盖唯一的好备份
                if (new FileInfo(file).Length < new FileInfo(backupFile).Length) return;
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

    // 保存到 phrases.txt（开了密码时加密）；失败时提示并返回 false
    public bool Save()
    {
        if (locked) return false;
        try
        {
            List<string> lines = new List<string>();
            Settings.WriteTo(lines);
            foreach (Phrase p in Phrases)
                lines.Add(Phrase.Clean(p.Zh) + "|" + Phrase.Clean(p.En) + "|" + p.Count + (p.Pinned ? "|1" : ""));
            byte[] data = new UTF8Encoding(false).GetBytes(string.Join("\r\n", lines.ToArray()) + "\r\n");
            if (crypto != null) data = crypto.Encrypt(data);
            WriteReplacing(file, data);
            return true;
        }
        catch { OnError("保存失败（目录只读？）"); return false; }
        finally { if (File.Exists(file)) HideFile(); }
    }

    // 先完整写入临时文件并刷到磁盘，再替换正式文件；中途断电也不会把正式文件写成半截
    static void WriteReplacing(string path, byte[] data)
    {
        string tmp = path + ".tmp";
        if (File.Exists(tmp)) { File.SetAttributes(tmp, FileAttributes.Normal); File.Delete(tmp); }
        using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
        {
            fs.Write(data, 0, data.Length);
            fs.Flush(true);
        }
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);  // 隐藏文件不能直接被替换
            try { File.Replace(tmp, path, null); }
            catch
            {
                // 某些磁盘（如网络共享）不支持替换，退回为覆盖复制
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }
        else File.Move(tmp, path);
    }

    // ---------- 启动密码 ----------

    public bool CheckPassword(string password) { return crypto != null && crypto.Matches(password); }

    // 开启或修改启动密码：词条文件和备份都改用新密码加密。失败时保持原样并返回 false
    public bool SetPassword(string password) { return ChangeEncryption(FileCrypto.Create(password)); }

    // 关闭启动密码：词条文件和备份都还原成明文（与从没加密过时完全一样）
    public bool RemovePassword() { return ChangeEncryption(null); }

    bool ChangeEncryption(FileCrypto next)
    {
        if (locked) return false;
        FileCrypto old = crypto;
        // 先用旧密钥读出备份的明文（备份可能是明文，也可能是旧密码加密的）
        byte[] bak = ReadIfExists(backupFile);
        byte[] bakPlain = null;
        if (bak != null && !FileCrypto.IsEncrypted(bak)) bakPlain = bak;
        else if (bak != null && old != null) old.TryDecrypt(bak, out bakPlain);
        crypto = next;
        if (!Save()) { crypto = old; return false; }
        try
        {
            if (bak == null) return true;
            if (bakPlain == null)
            {
                // 解不开的旧备份（不是当前密码加密的）已经没用，留着只会让启动时还要密码
                File.SetAttributes(backupFile, FileAttributes.Normal);
                File.Delete(backupFile);
                return true;
            }
            DateTime when = File.GetLastWriteTime(backupFile);  // 保留备份日期，每日备份的节奏不变
            WriteReplacing(backupFile, next != null ? next.Encrypt(bakPlain) : bakPlain);
            File.SetLastWriteTime(backupFile, when);
            File.SetAttributes(backupFile, FileAttributes.Hidden);
        }
        catch { OnError("备份文件更新失败"); }
        return true;
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
        foreach (Phrase p in Phrases) lines.Add(Phrase.Clean(p.Zh) + "|" + Phrase.Clean(p.En));
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
            string zh = Phrase.Clean(parts[0]);
            if (zh.Length == 0) continue;
            string en = parts.Length > 1 ? Phrase.Clean(parts[1]) : "";
            if (Exists(zh, null)) { dup++; continue; }
            if (Phrases.Count >= MaxPhrases) { over++; continue; }
            Phrases.Add(new Phrase(zh, en, 0));
            added++;
        }
        return added;
    }
}
