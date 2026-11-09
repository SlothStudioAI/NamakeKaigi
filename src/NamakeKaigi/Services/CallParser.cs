using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>発言の先頭の呼びかけを見つけた結果。</summary>
/// <param name="TargetKey">呼ばれた宛先(Participants の識別名、または全員を表す CallParser.EveryoneKey)。呼びかけがなければnull</param>
/// <param name="Body">呼びかけを取り除いた本文(呼びかけがなければ、元の本文そのまま)</param>
public sealed record CallResult(string? TargetKey, string Body)
{
    /// <summary>呼びかけがあったか。</summary>
    public bool IsCalled => TargetKey is not null;
}

/// <summary>発言の先頭の「ジェミ、〜」のような呼びかけを見つけて、宛先と本文に分ける(WBS 11.34)。画面は知らない。</summary>
public static class CallParser
{
    /// <summary>「全員」を表す宛先の識別名。</summary>
    public const string EveryoneKey = "everyone";

    // 呼び名のあとに付く区切り(読点・カンマ・空白)。これが付いたときだけ、呼びかけとみなす
    private static readonly char[] Separators = ['、', ',', '，', ' ', '　', '\t'];

    /// <summary>発言の先頭に呼びかけがあれば、宛先と、呼びかけを除いた本文を返す。呼び名のあとに区切りが付いたときだけ呼びかけとし、2人を並べた呼びかけ(「クロとジェミ、〜」)は扱わない。</summary>
    /// <param name="text">送信する発言(前後の空白は無視する)</param>
    /// <param name="names">呼び名の一覧</param>
    /// <returns>呼びかけの結果</returns>
    public static CallResult Parse(string text, CallNameSettings names)
    {
        var trimmed = (text ?? "").Trim();
        foreach (var (key, candidates) in Groups(names))
        {
            foreach (var name in candidates.Where(n => !string.IsNullOrWhiteSpace(n)).OrderByDescending(n => n.Length))
            {
                if (trimmed.Length <= name.Length || !trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (Array.IndexOf(Separators, trimmed[name.Length]) < 0) continue;
                return new CallResult(key, trimmed[(name.Length + 1)..].TrimStart(Separators));
            }
        }
        return new CallResult(null, trimmed);
    }

    /// <summary>宛先ごとの呼び名の一覧。</summary>
    private static IEnumerable<(string Key, IReadOnlyList<string> Names)> Groups(CallNameSettings names)
    {
        yield return (Participants.ClaudeId, names.ClaudeNames);
        yield return (Participants.GeminiId, names.GeminiNames);
        yield return (Participants.ChappyId, names.ChappyNames);
        yield return (EveryoneKey, names.EveryoneNames);
    }
}
