using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>AIからの返答と、続きの会話に使う会話ID。</summary>
/// <param name="Text">完成した返答の全文</param>
/// <param name="ConversationId">続きの会話に使うID</param>
/// <param name="Model">実際に使われたモデル名(分からなければnull)</param>
public sealed record AgentReply(string Text, string ConversationId, string? Model = null);

/// <summary>返答の途中で届く知らせの種類。</summary>
public enum AgentUpdateKind
{
    /// <summary>使われているモデル名が分かった。</summary>
    Model,

    /// <summary>返答の続きの文字が届いた。</summary>
    Delta,

    /// <summary>いまの作業(ファイルを読んでいる等)が分かった。空なら、その作業が終わった。</summary>
    Activity,

    /// <summary>使用量の枠(5時間・7日)が分かった。</summary>
    Usage,

    /// <summary>道具を使う前に、ユーザーの承認を求めている(Approval に答えるまで、AIは待つ)。</summary>
    Approval,
}

/// <summary>返答の途中で届く知らせ。</summary>
/// <param name="Kind">種類</param>
/// <param name="Text">モデル名、続きの文字、作業の文、または承認の問い合わせの文</param>
/// <param name="Approval">承認の問い合わせ(Kind が Approval のときだけ)</param>
public sealed record AgentUpdate(AgentUpdateKind Kind, string Text, ToolApproval? Approval = null);

/// <summary>会議に参加するAI1人(CLIの種類ごとに実装する)。</summary>
public interface IChatAgent
{
    /// <summary>識別名(claude / gemini)。</summary>
    string Id { get; }

    /// <summary>プロンプトに書く名前(Claude / Gemini)。</summary>
    string Name { get; }

    /// <summary>いま選ばれているモデル(CLIの --model に渡す名前。空なら指定なし)。</summary>
    string ModelId { get; }

    /// <summary>画面に出すモデル名の初期表示(選ばれているモデル。指定なしなら、その旨の文)。</summary>
    string ModelLabel { get; }

    /// <summary>モデルを切り替える(次の発言から効く)。</summary>
    /// <param name="modelId">CLIの --model に渡す名前</param>
    void SetModel(string modelId);

    /// <summary>いま選ばれている労力(CLIの --effort に渡す値。空なら指定なし)。</summary>
    string EffortId => "";

    /// <summary>労力(low / medium / high / xhigh / max)を切り替える(次の発言から効く)。</summary>
    /// <param name="effortId">CLIの --effort に渡す値</param>
    void SetEffort(string effortId) { }

    /// <summary>労力(--effort)をモデルと別に選べるか。モデル名に労力が含まれるAI(agy)ではfalse。</summary>
    bool SupportsEffort => true;

    /// <summary>CLI1回の制限時間を変える(次の発言から効く)。</summary>
    /// <param name="timeout">制限時間</param>
    void SetTimeout(TimeSpan timeout) { }

    /// <summary>ファイルを書き換える「書く担当」になれるか(既定はなれない)。</summary>
    bool CanWrite => false;

    /// <summary>いま「書く担当」か(次の発言から効く。なれないAIでは、設定しても無視する)。</summary>
    bool WriteMode
    {
        get => false;
        set { }
    }

    /// <summary>書く担当にしたときに画面へ出す注意(なければ空)。</summary>
    string WriterWarning => "";

    /// <summary>決まったコマンド(dotnet build など)も使える「持ち物」にできるか(既定はできない)。</summary>
    bool CanRunCommands => false;

    /// <summary>いまコマンドも使えるか(書く担当のときだけ有効。次の発言から効く)。</summary>
    bool CommandMode
    {
        get => false;
        set { }
    }

    /// <summary>「何でもできる」(道具をすべて使い、許可は自動判定)にできるか(既定はできない)。</summary>
    bool CanFull => false;

    /// <summary>いま「何でもできる」か(書く担当のときだけ有効。次の発言から効く)。</summary>
    bool FullMode
    {
        get => false;
        set { }
    }

    /// <summary>「何でもできる」にしたときに画面へ出す注意(なければ空)。</summary>
    string FullWarning => "";

    /// <summary>いま有効なハーネス(使えるツール・許可コマンド)の説明文(ツールチップ表示用)。</summary>
    string HarnessSummary => "";

    /// <summary>CLIを動かす作業フォルダを切り替える(次の発言から効く)。</summary>
    /// <param name="workDir">作業フォルダ</param>
    void SetWorkDir(string workDir) { }

    /// <summary>次の発言で、作業フォルダの外も読めるよう一時的に追加するフォルダ(--add-dir 用)。</summary>
    /// <param name="dirs">追加するフォルダの一覧</param>
    void SetExtraDirs(IReadOnlyList<string> dirs) { }

    /// <summary>すぐ出せる、選べるモデルの一覧(設定にある分)。</summary>
    IReadOnlyList<ModelOption> ModelOptions { get; }

    /// <summary>選べるモデルの一覧を、CLIに聞いて最新にする(聞けなければ、設定にある分を返す)。</summary>
    /// <param name="ct">キャンセル</param>
    /// <returns>選べるモデルの一覧</returns>
    Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct);

    /// <summary>起動したままのCLIがあれば終わらせる(会議を閉じる・新しい会議にするとき)。</summary>
    void Shutdown() { }

    /// <summary>直近の発言で、起動したままのCLIを使い回したか(true=使い回し、false=新しく起動、null=起動したままにしない作りなど)。発言ごとの時間の記録に使う。</summary>
    bool? LastTurnReusedCli => null;

    /// <summary>直近の発言で、CLIを起動し直した理由(first / dead / key / conversation。使い回したときや該当なしはnull)。発言ごとの時間の記録に使う。</summary>
    string? LastTurnRestartReason => null;

    /// <summary>1発言を送って返答を受け取る。返答は、届いた分から順に onUpdate へ知らせる。</summary>
    /// <param name="prompt">送る本文</param>
    /// <param name="conversationId">続きの会話ID(初回はnull)</param>
    /// <param name="onUpdate">途中の知らせを受け取る処理(別のスレッドから呼ばれる。不要ならnull)</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>完成した返答と新しい会話ID</returns>
    Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
        CancellationToken ct);
}
