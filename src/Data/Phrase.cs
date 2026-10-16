using System;

class Phrase
{
    public string Zh, En;
    public int Count;
    public bool Pinned;  // 钉住：固定在最上面，不参与按次数排序
    public Phrase(string zh, string en, int count) { Zh = zh; En = en; Count = count; }

    // 中文/英文字段的规范写法：竖线是 phrases.txt 的分隔符，换成全角「｜」；去掉换行（否则一条会被拆成两行）和首尾空白
    public static string Clean(string s) { return s.Replace('|', '｜').Replace("\r", "").Replace("\n", "").Trim(); }
}
