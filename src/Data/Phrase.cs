using System;

class Phrase
{
    public string Zh, En;
    public int Count;
    public bool Pinned;  // 钉住：固定在最上面，不参与按次数排序
    public Phrase(string zh, string en, int count) { Zh = zh; En = en; Count = count; }
}
