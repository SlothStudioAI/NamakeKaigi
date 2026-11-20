namespace NamakeKaigi.Services;

/// <summary>AIに送る本文を作る。</summary>
public static class PromptComposer
{
    /// <summary>まだ見ていない発言を先頭に添えた本文を作る。</summary>
    /// <param name="unseen">未読の発言(整形済み)</param>
    /// <param name="to">宛先のAI名</param>
    /// <param name="relayNote">最後に添える頼み書き(「全員」宛てを順番に答えるときの2人目だけ。空なら添えない)</param>
    /// <param name="initialSummary">前の会議から引き継いだ要約(各AIの初回発言時だけ渡す。空なら添えない)</param>
    /// <returns>送る本文</returns>
    public static string Compose(string unseen, string to, string? relayNote = null, string? initialSummary = null)
    {
        var prefix = string.IsNullOrWhiteSpace(initialSummary)
            ? ""
            : $"【前の会議からの引き継ぎ要約】\n{initialSummary.Trim()}\n\n";
        var body = $"{prefix}（会議の続きです。あなたは{to}。返答の先頭に「担当: …」の行は書かないでください。まだ見ていない発言は次の通り）\n\n{unseen}";
        return string.IsNullOrWhiteSpace(relayNote) ? body : $"{body}\n\n{relayNote}";
    }
}
