namespace NamakeKaigi.Models;

/// <summary>会議の発言1件。</summary>
/// <param name="SpeakerId">発言者の識別名</param>
/// <param name="Text">本文</param>
/// <param name="Time">発言の時刻</param>
/// <param name="TargetIds">宛先の識別名(ユーザーの発言だけ。AIの返答はnull)</param>
public sealed record ChatEntry(string SpeakerId, string Text, DateTime Time, IReadOnlyList<string>? TargetIds = null);

/// <summary>AIから届く途中経過・結果の種類。</summary>
public enum ChatProgressKind
{
    /// <summary>使われているモデル名が分かった。</summary>
    Model,

    /// <summary>返答の続きの文字が届いた(流れてくる途中)。</summary>
    Delta,

    /// <summary>いまの作業(ファイルを読んでいる等)が分かった。空なら、その作業が終わった。</summary>
    Activity,

    /// <summary>使用量の枠(5時間・7日)が分かった。</summary>
    Usage,

    /// <summary>返答が完成した(本文は完成した全文)。</summary>
    Done,

    /// <summary>失敗した(本文はエラーの説明)。</summary>
    Error,

    /// <summary>そのAIに聞き始めた(順番のときの2人目は、ここで考え中の吹き出しを出す)。</summary>
    Started,

    /// <summary>道具を使う前に、ユーザーの承認を求めている(Approval に問い合わせが入る)。</summary>
    Approval,
}

/// <summary>AI1人からの途中経過・結果。</summary>
/// <param name="AgentId">答えたAIの識別名</param>
/// <param name="Kind">種類</param>
/// <param name="Text">内容(モデル名・続きの文字・完成した全文・エラーの説明・承認の問い合わせの文)</param>
/// <param name="Approval">承認の問い合わせ(Kind が Approval のときだけ)</param>
public sealed record ChatProgress(string AgentId, ChatProgressKind Kind, string Text, ToolApproval? Approval = null);
