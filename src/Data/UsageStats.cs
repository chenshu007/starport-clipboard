using System;
using System.Collections.Generic;
using System.Globalization;

// 用量统计：#stats_since=、#stats_total=、#stats_chars=、#stats_days=日期:次数,…（只保留近 7 天）
// 和其他设置一起存在 phrases.txt 里；每条词条自己的点击次数在 Phrase.Count
class UsageStats
{
    public DateTime Since = DateTime.MinValue;  // MinValue = 还没开始统计
    public int Total;
    public long Chars;
    readonly Dictionary<string, int> days = new Dictionary<string, int>();

    // 四个设置行：只有开始统计后（Since 有值）才写出
    public UsageStats(Settings settings)
    {
        settings.Register("stats_since",
            delegate(string v)
            {
                DateTime d;
                if (DateTime.TryParseExact(v.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    Since = d;
            },
            delegate { return Started ? Since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null; });
        settings.Register("stats_total",
            delegate(string v) { int.TryParse(v.Trim(), out Total); },
            delegate { return Started ? Total.ToString() : null; });
        settings.Register("stats_chars",
            delegate(string v) { long.TryParse(v.Trim(), out Chars); },
            delegate { return Started ? Chars.ToString() : null; });
        settings.Register("stats_days",
            delegate(string v)
            {
                foreach (string kv in v.Split(','))
                {
                    string[] a = kv.Split(':');
                    int dayCount;
                    if (a.Length == 2 && a[0].Trim().Length == 10 && int.TryParse(a[1], out dayCount)) days[a[0].Trim()] = dayCount;
                }
            },
            delegate
            {
                if (!Started) return null;
                List<string> kv = new List<string>();
                foreach (KeyValuePair<string, int> d in days) kv.Add(d.Key + ":" + d.Value);
                return string.Join(",", kv.ToArray());
            });
    }

    public bool Started { get { return Since != DateTime.MinValue; } }

    // 首次启用统计：用已有的点击次数估算累计次数和字数
    public void StartFrom(List<Phrase> phrases)
    {
        Since = DateTokens.BusinessDate;
        Total = 0;
        Chars = 0;
        foreach (Phrase p in phrases)
        {
            Total += p.Count;
            Chars += (long)p.Count * DateTokens.FillQty(DateTokens.Expand(p.Zh, false), "2").Length;
        }
    }

    static string DayKey(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

    // 记一次复制：今天 +1、累计 +1、字数累加；只保留近 7 天的每日数据
    public void RecordCopy(int chars)
    {
        DateTime today = DateTokens.BusinessDate;
        string key = DayKey(today);
        int n;
        days.TryGetValue(key, out n);
        days[key] = n + 1;
        Total++;
        Chars += chars;
        List<string> old = new List<string>();
        foreach (string k in days.Keys)
        {
            DateTime d;
            if (!DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ||
                d < today.AddDays(-6) || d > today)
                old.Add(k);
        }
        foreach (string k in old) days.Remove(k);
    }

    // 某一天的复制次数
    public int CountOn(DateTime day)
    {
        int n;
        days.TryGetValue(DayKey(day), out n);
        return n;
    }

    // 清空统计，从今天重新开始（词条的点击次数由调用方归零）
    public void Reset()
    {
        Since = DateTokens.BusinessDate;
        Total = 0;
        Chars = 0;
        days.Clear();
    }

    // 点击次数最多的几条（次数为 0 的不算）：显示文字 + 次数
    public static List<KeyValuePair<string, int>> TopPhrases(List<Phrase> phrases, int count)
    {
        List<Phrase> sorted = new List<Phrase>(phrases);
        sorted.Sort(delegate(Phrase a, Phrase b) { return b.Count.CompareTo(a.Count); });
        List<KeyValuePair<string, int>> top = new List<KeyValuePair<string, int>>();
        for (int i = 0; i < sorted.Count && top.Count < count; i++)
            if (sorted[i].Count > 0)
                top.Add(new KeyValuePair<string, int>(DateTokens.FillQty(DateTokens.Expand(sorted[i].Zh, false), "?"), sorted[i].Count));
        return top;
    }
}
