namespace NamakeKaigi.Models;

/// <summary>
/// AIが道具(コマンド・ファイルの書き換えなど)を使う前の、ユーザーへの承認の問い合わせ1件。
/// 画面が Answer で答えると、待っているAIの処理が続く。
/// </summary>
public sealed class ToolApproval
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>問い合わせを作る。</summary>
    /// <param name="toolName">道具の名前(PowerShell、Write など)</param>
    /// <param name="detail">何をするか(コマンドの文、書き換えるファイルなど。なければ空)</param>
    public ToolApproval(string toolName, string detail)
    {
        ToolName = toolName;
        Detail = detail;
    }

    /// <summary>道具の名前。</summary>
    public string ToolName { get; }

    /// <summary>何をするか(コマンドの文、書き換えるファイルなど)。</summary>
    public string Detail { get; }

    /// <summary>画面に出す問い合わせの文。</summary>
    public string Text => Detail.Length > 0 ? $"{ToolName}: {Detail}" : ToolName;

    /// <summary>答え(許可ならtrue)。答えが出るまで待つ。</summary>
    public Task<bool> Result => _answer.Task;

    /// <summary>もう答えたか。</summary>
    public bool IsAnswered => _answer.Task.IsCompleted;

    /// <summary>答える(2回目以降は無視する)。</summary>
    /// <param name="allow">許可ならtrue</param>
    public void Answer(bool allow) => _answer.TrySetResult(allow);
}
