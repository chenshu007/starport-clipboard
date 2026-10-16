using System;
using System.Globalization;

// 日期占位符：{今天}/{today}、{明天}/{tomorrow}
// 中文栏显示「10月15日」，英文栏显示「15 Oct」；凌晨分界时间前算作前一个营业日
static class DateTokens
{
    public static int CutoffHour;  // 0 = 按日历日期

    public static DateTime BusinessDate
    {
        get
        {
            DateTime now = DateTime.Now;
            return (now.Hour < CutoffHour ? now.AddDays(-1) : now).Date;
        }
    }

    internal static string Fmt(DateTime d, bool en)
    {
        return en ? d.ToString("d MMM", CultureInfo.InvariantCulture) : d.Month + "月" + d.Day + "日";
    }

    // 数量占位符：{数量}/{qty}，复制时弹窗询问
    public static bool HasQty(string text) { return text.Contains("{数量}") || text.Contains("{qty}"); }

    public static string FillQty(string text, string qty) { return text.Replace("{数量}", qty).Replace("{qty}", qty); }

    public static string Expand(string text, bool en)
    {
        if (text.IndexOf('{') < 0) return text;
        DateTime today = BusinessDate;
        string t = Fmt(today, en), m = Fmt(today.AddDays(1), en);
        return text.Replace("{今天}", t).Replace("{today}", t).Replace("{明天}", m).Replace("{tomorrow}", m);
    }
}
