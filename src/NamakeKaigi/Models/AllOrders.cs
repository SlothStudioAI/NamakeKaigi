namespace NamakeKaigi.Models;

/// <summary>
/// 「全員」宛ての答える順番。同時、または「〇〇が先」(1人目が答えてから、残りの人がその答えを読んで、同時に答える)。
/// 2人の会議では、「〇〇が先」は1人目→2人目の順番になる。
/// </summary>
public static class AllOrders
{
    /// <summary>全員が同時に答える(互いの今回の答えは見ない。独立した意見)。</summary>
    public const string Parallel = "parallel";

    /// <summary>クローディが先に答え、残りの人がその答えを読んで答える。</summary>
    public const string ClaudeFirst = "claude-first";

    /// <summary>ジェミリーが先に答え、残りの人がその答えを読んで答える。</summary>
    public const string GeminiFirst = "gemini-first";

    /// <summary>チャッピーが先に答え、残りの人がその答えを読んで答える。</summary>
    public const string ChappyFirst = "chappy-first";

    /// <summary>知らない値・空を「同時」にそろえる。</summary>
    /// <param name="order">順番の識別名</param>
    /// <returns>そろえた識別名</returns>
    public static string Normalize(string? order) =>
        order is ClaudeFirst or GeminiFirst or ChappyFirst ? order : Parallel;

    /// <summary>先に答える人の識別名(同時なら空)。</summary>
    /// <param name="order">順番の識別名</param>
    /// <returns>先に答える人の識別名</returns>
    public static string FirstOf(string? order) => Normalize(order) switch
    {
        ClaudeFirst => Participants.ClaudeId,
        GeminiFirst => Participants.GeminiId,
        ChappyFirst => Participants.ChappyId,
        _ => "",
    };

    /// <summary>先に答える人から、順番の識別名を返す。</summary>
    /// <param name="firstId">先に答える人の識別名</param>
    /// <returns>順番の識別名(知らない人なら同時)</returns>
    public static string OrderFor(string firstId) => firstId switch
    {
        Participants.ClaudeId => ClaudeFirst,
        Participants.GeminiId => GeminiFirst,
        Participants.ChappyId => ChappyFirst,
        _ => Parallel,
    };

    /// <summary>順番に答える形か(宛先が2人以上で、先に答える人が宛先に入っているとき)。先の人が参加していなければ、同時にする。</summary>
    /// <param name="order">順番の識別名</param>
    /// <param name="targetIds">宛先</param>
    /// <returns>順番に答えるならtrue</returns>
    public static bool IsSequential(string order, IReadOnlyList<string> targetIds) =>
        targetIds.Count > 1 && targetIds.Contains(FirstOf(order));

    /// <summary>宛先を、答える順に並べる(先に答える人を先頭にし、残りは元の並びのまま)。同時のとき・宛先が1人のときは、元の並びのまま。</summary>
    /// <param name="order">順番の識別名</param>
    /// <param name="targetIds">宛先</param>
    /// <returns>答える順の宛先</returns>
    public static IReadOnlyList<string> Arrange(string order, IReadOnlyList<string> targetIds)
    {
        if (!IsSequential(order, targetIds)) return targetIds;
        var firstId = FirstOf(order);
        return targetIds.OrderBy(id => id == firstId ? 0 : 1).ToList();
    }
}
