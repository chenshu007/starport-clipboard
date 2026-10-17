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
    readonly string localDir, localBackupFile;  // 本机（%APPDATA%）再存的一份备份；为 null 时不用
    readonly string[] backups;                  // 恢复时依次尝试：共享文件夹里的 phrases.bak，再是本机的
    FileCrypto crypto;  // 开了启动密码时不为 null：读写都经过加密
    bool locked;        // 文件加了密码却没解开：不读也不写，免得覆盖掉加密的内容

    // 读写出错时的提示文字（界面上用红字显示）
    public event Action<string> Error;

    public PhraseStore(string dir) : this(dir, null) { }

    // localBackupDir：本机再存一份备份的文件夹。exe 放在共享文件夹时，同事把 phrases.txt 和 phrases.bak 一起删了也还有这份
    public PhraseStore(string dir, string localBackupDir)
    {
        Stats = new UsageStats(Settings);  // 统计的设置行登记在其他设置之后
        file = Path.Combine(dir, "phrases.txt");
        backupFile = Path.Combine(dir, "phrases.bak");
        localDir = localBackupDir;
        localBackupFile = localBackupDir != null ? Path.Combine(localBackupDir, "phrases.bak") : null;
        backups = localBackupFile != null ? new string[] { backupFile, localBackupFile } : new string[] { backupFile };
    }

    // 本机备份放在 %APPDATA%\StarportHaiku\<文件夹编号>，每个 exe 所在文件夹各一份
    public static string LocalBackupDirFor(string folderId)
    {
        return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarportHaiku"), folderId);
    }

    void OnError(string msg) { if (Error != null) Error(msg); }

    public bool PasswordEnabled { get { return crypto != null; } }

    static byte[] ReadIfExists(string path) { return File.Exists(path) ? File.ReadAllBytes(path) : null; }

    // 开了启动密码时（词条文件或备份是加密的），程序启动先用密码解开。
    // ask(第几次) 返回输入的密码，返回 null 表示放弃：这时返回 false，程序直接退出，不改动任何文件。
    // 没开密码时直接返回 true。
    public bool Unlock(Func<int, string> ask)
    {
        List<byte[]> encrypted = new List<byte[]>();  // 词条文件和各份备份中加密的那些
        bool checkable = false;
        foreach (string path in AllFiles())
        {
            byte[] data = ReadIfExists(path);
            if (!FileCrypto.IsEncrypted(data)) continue;
            encrypted.Add(data);
            checkable |= FileCrypto.IsWellFormed(data);
        }
        if (encrypted.Count == 0) return true;
        for (int attempt = 1; ; attempt++)
        {
            string password = ask(attempt);
            if (password == null) return false;
            // 都坏到没法核对密码：不能随便接受一个密码，否则会用默认词条覆盖原文件。只能一直问，直到放弃
            if (!checkable) continue;
            // 词条文件被截断、改动或删掉时它核对不过，就用备份核对，读入时再从备份恢复
            FileCrypto c = null;
            byte[] plain;
            foreach (byte[] data in encrypted)
            {
                if (FileCrypto.TryOpen(data, password, ref c, out plain))
                {
                    crypto = c;
                    return true;
                }
            }
        }
    }

    // 词条文件和全部备份
    IEnumerable<string> AllFiles()
    {
        yield return file;
        foreach (string b in backups) yield return b;
    }

    // 文件内容（加密的先解密）；加密的文件解不开（损坏）时返回 null
    // 开了密码时只认加密的文件：明文的（比如被人换掉的）不可信，同样返回 null
    byte[] ReadPlain(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (!FileCrypto.IsEncrypted(data)) return crypto == null ? data : null;
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
        bool anyEncrypted = false;
        foreach (string path in AllFiles()) anyEncrypted |= FileCrypto.IsEncrypted(ReadIfExists(path));
        if (crypto == null && anyEncrypted)
        {
            locked = true;
            OnError("词条文件已加密，需要输入启动密码");
            return;
        }
        try
        {
            bool missing = !File.Exists(file);
            byte[] data = missing ? null : ReadPlain(file);  // 加密文件解不开（损坏、被改动）时为 null，当作空文件
            string[] lines = data != null ? ToLines(data) : new string[0];
            int whole = data != null ? ParseLinesCountingWhole(lines, EndsWithNewline(data)) : 0;
            // 一条完整的词条也读不出来（空文件、被截断、被改动，或整个文件被删），而备份还在：从备份恢复
            bool recovered = whole == 0 && RecoverFromBackup();
            if (recovered) { Save(); OnError("词条文件为空、已损坏或被删除，已从备份恢复"); }
            else if (!missing && data == null) OnError("词条文件已损坏，无法解密");
            // 第一次运行，或空文件又没能从备份恢复：写入完整的默认词条
            if (!recovered && (missing || lines.Length == 0))
            {
                Phrases.Clear();
                ParseLines(Defaults);
                Settings.DefaultsVersion = DefaultsVersion;
                Save();
            }
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

    // 依次试各份备份，读出词条就用它
    bool RecoverFromBackup()
    {
        List<Phrase> before = new List<Phrase>(Phrases);
        foreach (string path in backups)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) continue;
            byte[] data = ReadPlain(path);
            if (data == null) continue;
            Phrases.Clear();  // 丢掉截断留下的半行
            ParseLines(ToLines(data));
            if (Phrases.Count > 0) return true;
        }
        Phrases.Clear();  // 没能恢复：保留原来读到的
        Phrases.AddRange(before);
        return false;
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

    // 每天第一次启动时把 phrases.txt 备份成隐藏的 phrases.bak，本机再存一份
    void BackupDaily()
    {
        BackupTo(backupFile, true);
        if (localBackupFile == null) return;
        try
        {
            Directory.CreateDirectory(localDir);
            // 写明这份备份来自哪个文件夹，方便手动找回
            string note = Path.Combine(localDir, "folder.txt");
            if (!File.Exists(note)) File.WriteAllText(note, Path.GetDirectoryName(file) + "\r\n", new UTF8Encoding(false));
        }
        catch { return; }
        BackupTo(localBackupFile, false);
    }

    void BackupTo(string target, bool hidden)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length == 0) return;
            if (File.Exists(target))
            {
                if (File.GetLastWriteTime(target).Date >= DateTime.Today) return;
                // 当前文件比备份小，可能是被截断的坏文件：不能用它覆盖唯一的好备份
                if (new FileInfo(file).Length < new FileInfo(target).Length) return;
                File.SetAttributes(target, FileAttributes.Normal);  // 隐藏文件不能被 File.Copy 覆盖
            }
            File.Copy(file, target, true);
            File.SetLastWriteTime(target, DateTime.Now);  // Copy 会沿用原文件时间，这里改成今天
            if (hidden) File.SetAttributes(target, FileAttributes.Hidden);
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
        // 先用旧密钥读出各份备份的明文（备份可能是明文，也可能是旧密码加密的）
        byte[][] plains = new byte[backups.Length][];
        bool[] present = new bool[backups.Length];
        for (int i = 0; i < backups.Length; i++)
        {
            byte[] bak = ReadIfExists(backups[i]);
            present[i] = bak != null;
            if (bak != null && !FileCrypto.IsEncrypted(bak)) plains[i] = bak;
            else if (bak != null && old != null) old.TryDecrypt(bak, out plains[i]);
        }
        crypto = next;
        if (!Save()) { crypto = old; return false; }
        for (int i = 0; i < backups.Length; i++)
        {
            if (!present[i]) continue;
            string path = backups[i];
            try
            {
                if (plains[i] == null)
                {
                    // 解不开的旧备份（不是当前密码加密的）已经没用，留着只会让启动时还要密码
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    continue;
                }
                DateTime when = File.GetLastWriteTime(path);  // 保留备份日期，每日备份的节奏不变
                WriteReplacing(path, next != null ? next.Encrypt(plains[i]) : plains[i]);
                File.SetLastWriteTime(path, when);
                if (path == backupFile) File.SetAttributes(path, FileAttributes.Hidden);
            }
            catch { OnError("备份文件更新失败"); }
        }
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
