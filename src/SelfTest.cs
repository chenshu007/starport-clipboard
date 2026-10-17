using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// 最基本的自动检查：StarportHaiku.exe --self-test，全部通过返回 0，否则返回 1（CI 里运行）
// 覆盖 phrases.txt 读写往返、设置行识别、占位符替换；只在临时目录里读写，不碰 exe 旁边的 phrases.txt
// 窗口程序没有控制台，结果写到标准输出，要重定向才能看到：StarportHaiku.exe --self-test > selftest.log
static class SelfTest
{
    static TextWriter log;
    static int passed, failed;

    public static int Run()
    {
        StreamWriter w = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        w.AutoFlush = true;
        log = w;
        string root = Path.Combine(Path.GetTempPath(), "StarportHaiku-selftest-" + Guid.NewGuid().ToString("N"));
        int cutoff = DateTokens.CutoffHour;
        try
        {
            Directory.CreateDirectory(root);
            SettingLines();
            Placeholders();
            RoundTrip(root);
            Integrity(root);
            Encryption(root);
            SharedFolder(root);
        }
        catch (Exception ex) { Check("没有未处理的异常", false, ex.ToString()); }
        finally
        {
            DateTokens.CutoffHour = cutoff;
            try { Directory.Delete(root, true); } catch { }
        }
        log.WriteLine();
        log.WriteLine((failed == 0 ? "PASS" : "FAIL") + "  通过 " + passed + "，失败 " + failed);
        return failed == 0 ? 0 : 1;
    }

    // ---------- 断言 ----------

    static void Section(string name) { log.WriteLine(); log.WriteLine("[" + name + "]"); }

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { passed++; log.WriteLine("  ok    " + name); return; }
        failed++;
        log.WriteLine("  FAIL  " + name);
        if (detail != null) log.WriteLine("        " + detail.Replace("\n", "\n        "));
    }

    static string Show(string s) { return s == null ? "(null)" : "「" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "」"; }

    static void Same(string name, string expected, string actual)
    {
        Check(name, expected == actual, "期望 " + Show(expected) + "\n实际 " + Show(actual));
    }

    static void Same(string name, bool expected, bool actual)
    {
        Check(name, expected == actual, "期望 " + expected + "，实际 " + actual);
    }

    // 逐行比较，失败时指出第一处不同
    static void SameLines(string name, string[] expected, string[] actual)
    {
        int n = Math.Max(expected.Length, actual.Length);
        for (int i = 0; i < n; i++)
        {
            string e = i < expected.Length ? expected[i] : null, a = i < actual.Length ? actual[i] : null;
            if (e != a)
            {
                Check(name, false, "第 " + (i + 1) + " 行：期望 " + Show(e) + "\n" + new string(' ', 4 + (i + 1).ToString().Length) + "实际 " + Show(a));
                return;
            }
        }
        Check(name, true, null);
    }

    // ---------- 设置行识别 ----------

    static void SettingLines()
    {
        Section("设置行识别");
        string[] yes = { "#topmost=1", "#user_suffix=-Mike", "#stats_days=", "#future_option=abc", "#a=1" };
        string[] no = { "#1203 房间|Room 1203", "#1203=1", "#Topmost=0", "#=1", "#top-most=1", "#topmost", "topmost=1",
                        "", "#", "＃topmost=1", " #topmost=1", "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}" };
        foreach (string s in yes) Same("是设置行 " + Show(s), true, IsSettingLine(s));
        foreach (string s in no) Same("不是设置行 " + Show(s), false, IsSettingLine(s));
    }

    // ---------- 占位符替换 ----------

    static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    static string Zh(DateTime d) { return d.Month + "月" + d.Day + "日"; }
    static string En(DateTime d) { return d.Day + " " + Months[d.Month - 1]; }

    static void Placeholders()
    {
        Section("占位符替换");
        DateTokens.CutoffHour = 0;
        DateTime today = DateTime.Now.Date, tomorrow = today.AddDays(1);
        Same("{今天} 换成中文日期", "提前退房 " + Zh(today), DateTokens.Expand("提前退房 {今天}", false));
        Same("{today} 换成英文日期", "Early out " + En(today), DateTokens.Expand("Early out {today}", true));
        Same("{明天} 换成中文日期", "早餐 " + Zh(tomorrow), DateTokens.Expand("早餐 {明天}", false));
        Same("{tomorrow} 换成英文日期", "EBF " + En(tomorrow), DateTokens.Expand("EBF {tomorrow}", true));
        Same("中文栏里的 {today} 也按中文格式", Zh(today) + "/" + Zh(tomorrow), DateTokens.Expand("{today}/{tomorrow}", false));
        Same("同一占位符出现多次都替换", Zh(today) + Zh(today), DateTokens.Expand("{今天}{今天}", false));
        Same("没有占位符时原样返回", "港币1000押金", DateTokens.Expand("港币1000押金", false));
        Same("不认识的花括号原样保留", "{明日} {Today}", DateTokens.Expand("{明日} {Today}", true));
        Same("中文日期格式：月、日不补零", "1月5日", DateTokens.Fmt(new DateTime(2027, 1, 5), false));
        Same("英文日期格式：日不补零 + 英文月份缩写", "5 Jan", DateTokens.Fmt(new DateTime(2027, 1, 5), true));
        Same("英文日期格式：两位数的日", "31 Dec", DateTokens.Fmt(new DateTime(2026, 12, 31), true));

        DateTokens.CutoffHour = 24;  // 任何时刻都早于分界时间：{今天} 算前一天
        Same("营业日分界前 {今天} 算前一天", Zh(today.AddDays(-1)), DateTokens.Expand("{今天}", false));
        Same("营业日分界前 {明天} 是日历上的今天", En(today), DateTokens.Expand("{tomorrow}", true));
        DateTokens.CutoffHour = 0;

        Same("有 {数量}", true, DateTokens.HasQty("加购{数量}份自助早餐"));
        Same("有 {qty}", true, DateTokens.HasQty("{qty} EBF"));
        Same("没有数量占位符", false, DateTokens.HasQty("港币1000押金 {今天}"));
        Same("{QTY} 区分大小写，不算", false, DateTokens.HasQty("{QTY} EBF"));
        Same("{数量} 和 {qty} 都换成份数", "加购3份 3 EBF", DateTokens.FillQty("加购{数量}份 {qty} EBF", "3"));
        Same("卡片上的显示：日期替换，份数显示为 ?", "加购?份自助早餐 " + Zh(tomorrow),
            DateTokens.FillQty(DateTokens.Expand("加购{数量}份自助早餐 {明天}", false), "?"));

        Same("用户模式加后缀（空格隔开）", "港币1000押金 -Mike", WithSuffix("港币1000押金", true, "-Mike"));
        Same("用户模式关闭不加后缀", "港币1000押金", WithSuffix("港币1000押金", false, "-Mike"));
        Same("后缀为空不加空格", "港币1000押金", WithSuffix("港币1000押金", true, ""));
    }

    // ---------- phrases.txt 读写往返 ----------

    // 程序保存时写出的规范格式：设置行在前，词条按钉住、点击次数排好序
    static readonly string[] Canonical =
    {
        "#topmost=0",
        "#user_mode=1",
        "#user_suffix=-Mike",
        "#day_cutoff=4",
        "#last_qty=3",
        "#defaults=3",
        "#win_pos=-1200,340",
        "#stats_since=2026-10-01",
        "#stats_total=57",
        "#stats_chars=812",
        "#stats_days=2026-10-14:5,2026-10-15:9",
        "提前退房 {今天}|Early out {today}|7|1",
        "#1203 房间|Room 1203|9",
        "港币1000押金|HKD 1,000 deposit|9",
        "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}|4",
        "只有中文||0",
    };

    // 第一次运行（没有 phrases.txt）时写出的完整内容
    static string[] NewFileLines()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Concat(DefaultSettings, new string[]
        {
            "#stats_since=" + today, "#stats_total=0", "#stats_chars=0", "#stats_days=",
            "港币1000押金|HKD 1,000 deposit|0",
            "VAPB，预授权1000|VAPB, pre-auth 1,000|0",
            "请收取押金|Please collect the deposit|0",
            "加急，客人赶时间|Urgent – guest is in a hurry|0",
            "遗落物品，客人上去拿|Guest will go up to collect left items|0",
            "客人遗落物品不要了|Guest does not want the left items|0",
            "借用酒店的充电器在前台|Hotel charger on loan – return to front desk|0",
            "无视请勿打扰（DND），进行查房|Override DND – proceed with room check|0",
            "提前退房 {今天}|Early out {today}|0",
            "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}|0",
        });
    }

    static readonly string[] NoStats = { "#stats_since=2026-10-01", "#stats_total=0", "#stats_chars=0", "#stats_days=" };

    static string[] Concat(params string[][] parts)
    {
        List<string> all = new List<string>();
        foreach (string[] p in parts) all.AddRange(p);
        return all.ToArray();
    }

    // 其余设置都是默认值时写出的设置行
    static readonly string[] DefaultSettings = { "#topmost=1", "#user_mode=0", "#user_suffix=", "#day_cutoff=0", "#last_qty=2", "#defaults=3" };

    static byte[] Utf8(string[] lines, string newline, bool bom)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(string.Join(newline, lines) + newline);
        if (!bom) return body;
        byte[] withBom = new byte[body.Length + 3];
        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
        Array.Copy(body, 0, withBom, 3, body.Length);
        return withBom;
    }

    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static string NewDir(string root, string name)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string[] ReadLines(string file)
    {
        string text = new UTF8Encoding(false).GetString(File.ReadAllBytes(file));
        if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
        return text.Split(new string[] { "\r\n" }, StringSplitOptions.None);
    }

    static void RoundTrip(string root)
    {
        Section("phrases.txt 读写往返");
        string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string tomorrowZh = Zh(DateTime.Now.Date.AddDays(1));
        byte[] canonical = Utf8(Canonical, "\r\n", false);

        // 1. 规范文件：读入再保存，逐字节不变；当天第一次启动时备份成 phrases.bak
        string dir = NewDir(root, "canonical");
        string file = Path.Combine(dir, "phrases.txt"), bak = Path.Combine(dir, "phrases.bak");
        File.WriteAllBytes(file, canonical);
        Load(dir, true);
        byte[] saved = File.ReadAllBytes(file);
        Check("规范文件读入再保存，逐字节不变（UTF-8 无 BOM、CRLF）", SameBytes(canonical, saved),
            "保存后的内容：\n" + new UTF8Encoding(false).GetString(saved));
        Check("当天第一次启动时备份为 phrases.bak", File.Exists(bak) && SameBytes(canonical, File.ReadAllBytes(bak)), null);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            Check("phrases.txt 是隐藏文件", (File.GetAttributes(file) & FileAttributes.Hidden) != 0, null);
            Check("phrases.bak 是隐藏文件", (File.GetAttributes(bak) & FileAttributes.Hidden) != 0, null);
        }
        Check("保存后不留临时文件", !File.Exists(file + ".tmp"), null);

        // 2. 带 BOM、LF 换行的文件也能读，保存成规范格式
        dir = NewDir(root, "bom-lf");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, Utf8(Canonical, "\n", true));
        Load(dir, true);
        Check("带 BOM、LF 换行的文件读入后按规范格式保存", SameBytes(canonical, File.ReadAllBytes(file)), null);

        // 3. 不需要迁移、恢复或初始化统计时，只读不写：不规范的写法、没排过的顺序都原样留在文件里
        dir = NewDir(root, "read-only");
        file = Path.Combine(dir, "phrases.txt");
        byte[] untouched = Utf8(Concat(new string[] { "#future_option=1", "#defaults=3" }, NoStats, new string[]
        {
            "只有中文",
            " 港币1000押金 | HKD 1,000 deposit | 9 ",
        }), "\n", false);
        File.WriteAllBytes(file, untouched);
        Load(dir, false);
        Check("读入时不改写 phrases.txt", SameBytes(untouched, File.ReadAllBytes(file)), null);

        // 4. 手写的不规范内容：设置值容错、词条字段补齐、未知设置行跳过、按钉住和次数排序
        dir = NewDir(root, "messy");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, Utf8(new string[]
        {
            "#topmost=abc",
            "#user_mode=yes",
            "#user_suffix=  -Amy  ",
            "#last_qty=x",
            "#day_cutoff=0",
            "#defaults=3",
            "#win_pos=1,2,3",
            "#stats_since=2026-10-01",
            "#stats_total=0",
            "#stats_chars=0",
            "#stats_days=",
            "#future_option=1",
            "#Topmost=0",
            "  空格前后  |  spaced  | 3 ",
            "仅中文",
            "|没有中文|5",
            "",
            "负数次数|neg|-5",
            "次数不是数字|nan|abc",
            "钉住|pin|2|1",
            "不是钉住|x|2|yes",
        }, "\r\n", false));
        Load(dir, true);
        SameLines("不规范内容读入后按规范格式保存", new string[]
        {
            "#topmost=1",
            "#user_mode=0",
            "#user_suffix=-Amy",
            "#day_cutoff=0",
            "#last_qty=0",
            "#defaults=3",
            "#stats_since=2026-10-01",
            "#stats_total=0",
            "#stats_chars=0",
            "#stats_days=",
            "钉住|pin|2|1",
            "空格前后|spaced|3",
            "不是钉住|x|2",
            "#Topmost=0||0",
            "仅中文||0",
            "负数次数|neg|0",
            "次数不是数字|nan|0",
        }, ReadLines(file));

        // 以下几种情况，读入过程本身就会保存，直接检查读入后的文件

        // 5. 没有 phrases.txt：写入默认词条，统计从今天开始
        dir = NewDir(root, "new");
        Load(dir, false);
        SameLines("首次运行写入默认词条", NewFileLines(), ReadLines(Path.Combine(dir, "phrases.txt")));

        // 6. 默认词条迁移：#defaults=2 把旧的固定 2 份 EBF 改为数量占位符（保留次数）；迁移后保存时还没排序
        dir = NewDir(root, "defaults-2");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, Utf8(Concat(new string[] { "#defaults=2" }, NoStats, new string[]
        {
            "加购2份自助早餐 {明天}|2 EBF {tomorrow}|3",
            "港币1000押金|HKD 1,000 deposit|5",
        }), "\r\n", false));
        Load(dir, false);
        SameLines("#defaults=2：旧 EBF 词条改为 {数量}", Concat(DefaultSettings, NoStats, new string[]
        {
            "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}|3",
            "港币1000押金|HKD 1,000 deposit|5",
        }), ReadLines(file));

        // 7. 默认词条迁移：#defaults=1 补上 v2 新增的默认词条（已有的不重复）
        dir = NewDir(root, "defaults-1");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, Utf8(Concat(new string[] { "#defaults=1" }, NoStats, new string[]
        {
            "提前退房 {今天}|Early out {today}|1",
            "港币1000押金|HKD 1,000 deposit|5",
        }), "\r\n", false));
        Load(dir, false);
        SameLines("#defaults=1：补上新增的默认词条", Concat(DefaultSettings, NoStats, new string[]
        {
            "提前退房 {今天}|Early out {today}|1",
            "港币1000押金|HKD 1,000 deposit|5",
            "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}|0",
        }), ReadLines(file));

        // 8. phrases.txt 是空文件、备份还在：从备份恢复
        dir = NewDir(root, "recover");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, new byte[0]);
        File.WriteAllBytes(Path.Combine(dir, "phrases.bak"), canonical);
        Load(dir, false);
        Check("空文件从 phrases.bak 恢复", SameBytes(canonical, File.ReadAllBytes(file)), null);

        // 9. 没有统计记录：用已有的点击次数估算累计次数和字数（估算时份数按 2 算）；保存时还没排序
        dir = NewDir(root, "stats-start");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, Utf8(new string[]
        {
            "#day_cutoff=0",
            "#defaults=3",
            "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}|2",
            "港币1000押金|HKD 1,000 deposit|5",
        }, "\r\n", false));
        Load(dir, false);
        long chars = 2 * ("加购2份自助早餐 " + tomorrowZh).Length + 5 * "港币1000押金".Length;
        SameLines("首次统计按已有次数估算", Concat(DefaultSettings, new string[]
        {
            "#stats_since=" + today, "#stats_total=7", "#stats_chars=" + chars, "#stats_days=",
            "加购{数量}份自助早餐 {明天}|{qty} EBF {tomorrow}|2",
            "港币1000押金|HKD 1,000 deposit|5",
        }), ReadLines(file));
    }

    // ---------- 数据完整性（STA-39：审计 H1 / M1 / M2 / M3） ----------

    static byte[] Head(byte[] data, int length)
    {
        byte[] part = new byte[length];
        Array.Copy(data, part, length);
        return part;
    }

    static void Integrity(string root)
    {
        Section("数据完整性");
        byte[] canonical = Utf8(Canonical, "\r\n", false);
        DateTime yesterday = DateTime.Now.AddDays(-1);

        // H1：phrases.txt 被截断成半截（非空，但读不出词条），备份是昨天的好副本：应从备份恢复，备份不被覆盖
        string dir = NewDir(root, "truncated-settings");
        string file = Path.Combine(dir, "phrases.txt"), bak = Path.Combine(dir, "phrases.bak");
        File.WriteAllBytes(file, Head(canonical, 50));
        File.WriteAllBytes(bak, canonical);
        File.SetLastWriteTime(bak, yesterday);
        Load(dir, false);
        Check("截断成读不出词条：从 phrases.bak 恢复", SameBytes(canonical, File.ReadAllBytes(file)), null);
        Check("截断成读不出词条：备份保持完好", SameBytes(canonical, File.ReadAllBytes(bak)), null);

        // H1：截断在词条中间（还能读出一部分词条）：不恢复，但更小的半截文件不能覆盖备份
        dir = NewDir(root, "truncated-phrases");
        file = Path.Combine(dir, "phrases.txt");
        bak = Path.Combine(dir, "phrases.bak");
        byte[] half = Head(canonical, canonical.Length - 20);
        File.WriteAllBytes(file, half);
        File.WriteAllBytes(bak, canonical);
        File.SetLastWriteTime(bak, yesterday);
        Load(dir, false);
        Check("截断在词条中间：比备份小的文件不覆盖备份", SameBytes(canonical, File.ReadAllBytes(bak)), null);

        // 手写的文件最后一行没有换行：照常读取，不当成损坏
        dir = NewDir(root, "no-trailing-newline");
        file = Path.Combine(dir, "phrases.txt");
        bak = Path.Combine(dir, "phrases.bak");
        byte[] handEdited = new UTF8Encoding(false).GetBytes(string.Join("\r\n", Concat(DefaultSettings, NoStats,
            new string[] { "港币1000押金|HKD 1,000 deposit|5", "手写的最后一行|Last line" })));
        File.WriteAllBytes(file, handEdited);
        File.WriteAllBytes(bak, canonical);
        File.SetLastWriteTime(bak, yesterday);
        PhraseStore hand = Open(dir);
        Same("最后一行没有换行：照常读入", "手写的最后一行", hand.Phrases.Count == 2 ? hand.Phrases[1].Zh : null);
        Check("最后一行没有换行：不从备份恢复、不改写文件", SameBytes(handEdited, File.ReadAllBytes(file)), null);

        // 只有一条词条、最后没有换行、也没有备份：这条照常保留
        dir = NewDir(root, "single-no-newline");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes("#defaults=3\r\n唯一的一条|Only one"));
        hand = Open(dir);
        Same("只有一条且没有换行、没有备份：保留这条", "唯一的一条", hand.Phrases.Count == 1 ? hand.Phrases[0].Zh : null);

        // M1：phrases.txt 存在但为空、也没有备份：写入完整的 10 条默认词条
        dir = NewDir(root, "empty-no-backup");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, new byte[0]);
        Load(dir, false);
        SameLines("空文件且没有备份：写入完整默认词条", NewFileLines(), ReadLines(file));

        // M2 / M3：字段里有竖线或换行，保存后再读入，仍是同一条词条、文字不丢
        dir = NewDir(root, "pipe-newline");
        file = Path.Combine(dir, "phrases.txt");
        File.WriteAllBytes(file, canonical);
        PhraseStore store = Open(dir);
        store.Phrases.Add(new Phrase("竖线|中文", "line1\r\nline2|tail", 3));
        store.Save();
        store = Open(dir);
        Same("含竖线/换行：词条数不变", (Canonical.Length - 11 + 1).ToString(), store.Phrases.Count.ToString());
        Phrase last = store.Phrases.Find(delegate(Phrase p) { return p.Zh.StartsWith("竖线"); });
        Same("含竖线：中文完整保留（| 存为 ｜）", "竖线｜中文", last != null ? last.Zh : null);
        Same("含换行：英文完整保留（去掉换行，| 存为 ｜）", "line1line2｜tail", last != null ? last.En : null);
        Same("含竖线/换行：次数保留", "3", last != null ? last.Count.ToString() : null);
        store.Save();
        byte[] first = File.ReadAllBytes(file);
        Open(dir).Save();
        Check("含竖线/换行：再读入保存一次内容不变", SameBytes(first, File.ReadAllBytes(file)), null);

        Same("字段净化：竖线换全角、去掉换行和首尾空白", "a｜bc", Phrase.Clean(" a|b\r\nc "));
        int dup, over;
        int added = store.Import(new string[] { " 导入的中文 | Imported en | 5 | 1" }, out dup, out over);
        Phrase imported = store.Phrases.Find(delegate(Phrase p) { return p.Zh == "导入的中文"; });
        Check("导入：字段同样净化", added == 1 && imported != null && imported.En == "Imported en" && imported.Count == 0, null);
    }

    // ---------- 启动密码加密 ----------

    static string Hex(byte[] b)
    {
        StringBuilder sb = new StringBuilder();
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    static bool Contains(byte[] data, string text)
    {
        byte[] needle = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i + needle.Length <= data.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && data[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    // 解锁：按顺序给出 passwords 里的密码，用完返回 null（放弃）；tries 是实际问了几次
    static bool Unlock(PhraseStore store, string[] passwords, out int tries)
    {
        int asked = 0;
        bool ok = store.Unlock(delegate(int attempt) { asked = attempt; return attempt <= passwords.Length ? passwords[attempt - 1] : null; });
        tries = asked;
        return ok;
    }

    static void Encryption(string root)
    {
        Section("启动密码加密");
        // PBKDF2-HMAC-SHA256 标准测试向量（RFC 7914 第 11 节同款参数）
        byte[] pw = Encoding.ASCII.GetBytes("password"), salt = Encoding.ASCII.GetBytes("salt");
        Same("PBKDF2-HMAC-SHA256，1 次", "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b", Hex(FileCrypto.Pbkdf2(pw, salt, 1, 32)));
        Same("PBKDF2-HMAC-SHA256，4096 次", "c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a", Hex(FileCrypto.Pbkdf2(pw, salt, 4096, 32)));
        DateTime t0 = DateTime.Now;
        FileCrypto.Pbkdf2(pw, salt, FileCrypto.DefaultIterations, 64);
        log.WriteLine("        （实际迭代次数 " + FileCrypto.DefaultIterations + " 派生一次用时 " + (int)(DateTime.Now - t0).TotalMilliseconds + " ms）");

        int saved = FileCrypto.NewIterations;
        FileCrypto.NewIterations = 1000;  // 以下用例只验证逻辑，调小迭代次数加快速度
        try
        {
            byte[] canonical = Utf8(Canonical, "\r\n", false);
            const string Pwd = "开门 Sesame 123";

            // 开启：词条文件和备份都加密，看不到明文；每行都是设置行的样子（旧版本不会当成词条）
            string dir = NewDir(root, "encrypt");
            string file = Path.Combine(dir, "phrases.txt"), bak = Path.Combine(dir, "phrases.bak");
            File.WriteAllBytes(file, canonical);
            PhraseStore store = Open(dir);  // 当天第一次启动：备份成明文的 phrases.bak
            Check("开启密码成功", store.SetPassword(Pwd) && store.PasswordEnabled, null);
            byte[] enc = File.ReadAllBytes(file), encBak = File.ReadAllBytes(bak);
            Check("词条文件已加密，看不到明文", FileCrypto.IsEncrypted(enc) && !Contains(enc, "港币") && !Contains(enc, "Mike"), null);
            Check("备份也已加密，看不到明文", FileCrypto.IsEncrypted(encBak) && !Contains(encBak, "港币"), null);
            bool allSettings = true;
            foreach (string line in ReadLines(file)) allSettings &= Settings.IsSettingLine(line);
            Check("加密文件每行都是设置行的样子", allSettings, null);
            Check("密码核对", store.CheckPassword(Pwd) && !store.CheckPassword("wrong"), null);

            // 没解锁就读入：不读也不写
            PhraseStore lockedStore = new PhraseStore(dir);
            lockedStore.Load();
            Check("没输入密码时不改动文件", !lockedStore.Save() && SameBytes(enc, File.ReadAllBytes(file)) && SameBytes(encBak, File.ReadAllBytes(bak)), null);

            // 取消输入密码：返回 false，不改动文件
            int tries;
            Check("取消输入密码：程序退出、不改动文件", !Unlock(new PhraseStore(dir), new string[0], out tries) && SameBytes(enc, File.ReadAllBytes(file)), null);

            // 密码错一次再输对：能打开；关闭密码后逐字节还原成明文（备份也是）
            store = new PhraseStore(dir);
            Check("密码错了要求重输，输对后打开", Unlock(store, new string[] { "wrong", Pwd }, out tries) && tries == 2, "尝试次数 " + tries);
            DateTokens.CutoffHour = 0;
            store.Load();
            Check("解密后词条完整", store.Phrases.Count == 5 && store.Phrases[0].Zh == "提前退房 {今天}", null);
            Check("关闭密码成功", store.RemovePassword() && !store.PasswordEnabled, null);
            Check("关闭密码后词条文件逐字节还原为明文", SameBytes(canonical, File.ReadAllBytes(file)), null);
            Check("关闭密码后备份逐字节还原为明文", SameBytes(canonical, File.ReadAllBytes(bak)), null);
            Check("关闭后启动不再要密码", Unlock(new PhraseStore(dir), new string[0], out tries) && tries == 0, null);

            // 修改密码：旧密码打不开，新密码可以
            store = Open(dir);
            store.SetPassword("old");
            store.SetPassword("new");
            Check("修改密码后旧密码打不开", !Unlock(new PhraseStore(dir), new string[] { "old" }, out tries), null);
            Check("修改密码后新密码能打开", Unlock(new PhraseStore(dir), new string[] { "new" }, out tries), null);

            // 加密状态下截断：用备份核对密码并恢复，备份不被覆盖
            dir = NewDir(root, "encrypt-truncated");
            file = Path.Combine(dir, "phrases.txt");
            bak = Path.Combine(dir, "phrases.bak");
            File.WriteAllBytes(file, canonical);
            Open(dir).SetPassword(Pwd);
            File.SetLastWriteTime(bak, DateTime.Now.AddDays(-1));
            enc = File.ReadAllBytes(file);
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllBytes(file, Head(enc, enc.Length / 2));
            store = new PhraseStore(dir);
            Check("截断的加密文件：用备份核对密码", Unlock(store, new string[] { Pwd }, out tries), null);
            DateTokens.CutoffHour = 0;
            store.Load();
            Check("截断的加密文件：从加密备份恢复", store.Phrases.Count == 5, "词条数 " + store.Phrases.Count);
            store.RemovePassword();
            Check("恢复后的内容逐字节一致", SameBytes(canonical, File.ReadAllBytes(file)), null);
            Check("截断的加密文件：备份仍是完好的内容", SameBytes(canonical, File.ReadAllBytes(bak)), null);

            // 加密状态下截断在中间、读不出词条以外的情况：更小的半截文件不覆盖加密备份
            dir = NewDir(root, "encrypt-backup-guard");
            file = Path.Combine(dir, "phrases.txt");
            bak = Path.Combine(dir, "phrases.bak");
            File.WriteAllBytes(file, canonical);
            store = Open(dir);
            store.SetPassword(Pwd);
            store.Phrases.RemoveRange(1, 4);  // 文件变小（比如删掉了几条）
            store.Save();
            File.SetLastWriteTime(bak, DateTime.Now.AddDays(-1));
            encBak = File.ReadAllBytes(bak);
            store = new PhraseStore(dir);
            Unlock(store, new string[] { Pwd }, out tries);
            store.Load();
            Check("加密时更小的文件同样不覆盖备份", SameBytes(encBak, File.ReadAllBytes(bak)), null);
        }
        finally { FileCrypto.NewIterations = saved; }
    }

    // ---------- 共享文件夹防护：输错密码不写文件、防篡改、本机多一份备份 ----------

    static void SharedFolder(string root)
    {
        Section("共享文件夹防护");
        int saved = FileCrypto.NewIterations;
        FileCrypto.NewIterations = 1000;
        try
        {
            byte[] canonical = Utf8(Canonical, "\r\n", false);
            const string Pwd = "开门 Sesame 123";
            int tries;

            // 输错密码、不输密码：一律不写文件
            string dir = NewDir(root, "wrong-password");
            string file = Path.Combine(dir, "phrases.txt"), bak = Path.Combine(dir, "phrases.bak");
            File.WriteAllBytes(file, canonical);
            Open(dir).SetPassword(Pwd);
            byte[] enc = File.ReadAllBytes(file), encBak = File.ReadAllBytes(bak);
            PhraseStore s = new PhraseStore(dir);
            bool opened = Unlock(s, new string[] { "a", "b", "c" }, out tries);
            s.Load();
            s.Save();
            Check("连续输错后放弃：不打开、不写文件", !opened && tries == 4 &&
                SameBytes(enc, File.ReadAllBytes(file)) && SameBytes(encBak, File.ReadAllBytes(bak)), null);

            // 词条文件和备份都坏到没法核对密码：不能随便接受一个密码去写默认词条
            dir = NewDir(root, "unverifiable");
            file = Path.Combine(dir, "phrases.txt");
            bak = Path.Combine(dir, "phrases.bak");
            byte[] broken = Encoding.ASCII.GetBytes("#encrypted=1\r\n#kdf=pbkdf2-sha256,1000,\r\n");
            File.WriteAllBytes(file, broken);
            File.WriteAllBytes(bak, broken);
            s = new PhraseStore(dir);
            opened = Unlock(s, new string[] { "随便一个密码" }, out tries);
            s.Load();
            s.Save();
            Check("无法核对密码时不打开、不写文件", !opened && SameBytes(broken, File.ReadAllBytes(file)) && SameBytes(broken, File.ReadAllBytes(bak)), null);

            // 密文被改动：校验不过，不当成词条，从备份恢复
            dir = NewDir(root, "tampered");
            file = Path.Combine(dir, "phrases.txt");
            File.WriteAllBytes(file, canonical);
            Open(dir).SetPassword(Pwd);
            string text = Encoding.ASCII.GetString(File.ReadAllBytes(file));
            int at = text.IndexOf("#data=") + 20;
            text = text.Substring(0, at) + (text[at] == 'A' ? 'B' : 'A') + text.Substring(at + 1);
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllText(file, text, Encoding.ASCII);
            s = new PhraseStore(dir);
            Check("密文被改动：用备份核对密码", Unlock(s, new string[] { Pwd }, out tries), null);
            DateTokens.CutoffHour = 0;
            s.Load();
            s.RemovePassword();
            Check("密文被改动：从备份恢复原内容", SameBytes(canonical, File.ReadAllBytes(file)), null);

            // 开了密码后，词条文件被换成明文：不读这些内容，从加密备份恢复
            dir = NewDir(root, "replaced-plain");
            file = Path.Combine(dir, "phrases.txt");
            File.WriteAllBytes(file, canonical);
            Open(dir).SetPassword(Pwd);
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllBytes(file, Utf8(new string[] { "#defaults=3", "被换进来的词条|Fake|99" }, "\r\n", false));
            s = new PhraseStore(dir);
            Unlock(s, new string[] { Pwd }, out tries);
            DateTokens.CutoffHour = 0;
            s.Load();
            Check("加密时被换成明文的文件不读入", s.Phrases.Count == 5 && !s.Exists("被换进来的词条", null), "词条数 " + s.Phrases.Count);

            // 本机（%APPDATA%）再存一份备份
            dir = NewDir(root, "local-backup");
            string local = Path.Combine(root, "local-backup-appdata");
            file = Path.Combine(dir, "phrases.txt");
            bak = Path.Combine(dir, "phrases.bak");
            string localBak = Path.Combine(local, "phrases.bak");
            File.WriteAllBytes(file, canonical);
            OpenWithLocal(dir, local);
            Check("本机也备份一份", File.Exists(localBak) && SameBytes(canonical, File.ReadAllBytes(localBak)), null);

            // 同事把共享文件夹里的 phrases.txt 和 phrases.bak 都删了：从本机备份恢复
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
            File.SetAttributes(bak, FileAttributes.Normal);
            File.Delete(bak);
            PhraseStore restored = OpenWithLocal(dir, local);
            Check("共享文件夹里的文件都被删：从本机备份恢复", restored.Phrases.Count == 5 && SameBytes(canonical, File.ReadAllBytes(file)),
                "词条数 " + restored.Phrases.Count);

            // 开了密码：本机备份也加密；共享文件夹里都被删时，用本机备份核对密码并恢复
            restored.SetPassword(Pwd);
            byte[] encLocal = File.ReadAllBytes(localBak);
            Check("开密码后本机备份也加密", FileCrypto.IsEncrypted(encLocal) && !Contains(encLocal, "港币"), null);
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
            File.SetAttributes(bak, FileAttributes.Normal);
            File.Delete(bak);
            s = new PhraseStore(dir, local);
            Check("文件都被删时用本机备份核对密码", Unlock(s, new string[] { Pwd }, out tries) && tries == 1, "尝试次数 " + tries);
            DateTokens.CutoffHour = 0;
            s.Load();
            Check("加密时从本机备份恢复", s.Phrases.Count == 5, "词条数 " + s.Phrases.Count);
            s.RemovePassword();
            Check("关闭密码后本机备份还原为明文", SameBytes(canonical, File.ReadAllBytes(localBak)), null);
        }
        finally { FileCrypto.NewIterations = saved; }
    }

    static PhraseStore OpenWithLocal(string dir, string local)
    {
        DateTokens.CutoffHour = 0;
        PhraseStore store = new PhraseStore(dir, local);
        store.Load();
        return store;
    }

    // ---------- 被测代码的入口（重构时只改这里） ----------

    static bool IsSettingLine(string line) { return Settings.IsSettingLine(line); }

    static string WithSuffix(string text, bool on, string suffix) { return MainForm.WithSuffix(text, on, suffix); }

    // 按程序启动时的流程读入 dir 里的 phrases.txt
    static PhraseStore Open(string dir)
    {
        DateTokens.CutoffHour = 0;  // 与刚启动的程序一致
        PhraseStore store = new PhraseStore(dir);
        store.Load();
        return store;
    }

    // 读入；save = true 时随后再保存一次（相当于点了一次复制）
    static void Load(string dir, bool save)
    {
        PhraseStore store = Open(dir);
        if (save) store.Save();
    }
}
