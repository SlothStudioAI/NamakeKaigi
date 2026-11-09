using System.IO;
using System.Text;
using System.Text.Json;

namespace NamakeKaigi.Services;

/// <summary>
/// 1回の発言(1人のAIに聞いて、答えが返るまで)にかかった時間(WBS 11.51)。
/// 「遅い」の原因が、順番待ち・起動・モデルの返答づくりのどれかを、あとから数字で見るための記録。
/// </summary>
/// <param name="At">記録した時刻</param>
/// <param name="Who">答えたAIの識別名</param>
/// <param name="Outcome">結果(ok=答えが届いた、stopped=停止、error=失敗)</param>
/// <param name="QueueMs">順番待ち(前の返答が終わるまで待った時間)。ミリ秒</param>
/// <param name="FirstOutputMs">聞いてから、最初の知らせ(モデル名・作業の様子など、何でも)が届くまで。届かなければ -1</param>
/// <param name="FirstTextMs">聞いてから、返答の最初の文字が届くまで。届かなければ -1</param>
/// <param name="TotalMs">聞いてから、答え終わるまで(順番待ちは含まない)</param>
/// <param name="ReusedCli">起動したままのCLIを使い回したか(true=使い回し、false=新しく起動、null=該当なし)</param>
/// <param name="ReplyChars">返答の文字数</param>
/// <param name="RestartReason">起動し直した理由(first / dead / key / conversation。保険のやり直しが入ったときは retry+ が付く。使い回しや該当なしはnull)</param>
public sealed record TurnTiming(DateTime At, string Who, string Outcome, long QueueMs, long FirstOutputMs,
    long FirstTextMs, long TotalMs, bool? ReusedCli, int ReplyChars, string? RestartReason = null);

/// <summary>発言ごとの時間を、会議フォルダの timing.jsonl に1行ずつ足す。記録できなくても、会議は止めない。</summary>
public static class TurnTimingLog
{
    /// <summary>記録のファイル名(会議フォルダの中)。</summary>
    public const string FileName = "timing.jsonl";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>1行を足す。フォルダがまだなければ、何もしない。</summary>
    /// <param name="folder">会議フォルダ</param>
    /// <param name="timing">記録する内容</param>
    public static void Append(string? folder, TurnTiming timing)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            File.AppendAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(timing, Options) + "\n", Utf8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 記録できなくても、会話は続ける
        }
    }
}
