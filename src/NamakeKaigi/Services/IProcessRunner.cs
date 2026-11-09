namespace NamakeKaigi.Services;

/// <summary>外部のコマンドを実行して、標準出力を1行ずつ受け取る(試験では偽物に差し替える)。</summary>
public interface IProcessRunner
{
    /// <summary>コマンドを実行し、標準出力が1行出るたびに通知する。</summary>
    /// <param name="fileName">実行ファイル</param>
    /// <param name="args">引数(1要素=1引数。引用符の処理は不要)</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="timeout">タイムアウト(全体)</param>
    /// <param name="onLine">1行ごとに呼ぶ(読み取りのスレッドから呼ばれる)</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>プロセスが終わったら完了(終了コードが0以外なら例外)</returns>
    Task RunLinesAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, Action<string> onLine, CancellationToken ct);

    /// <summary>
    /// コマンドを実行し、標準入力を開いたまま、やりとりする(許可の問い合わせに答えるため)。
    /// 最初に firstInput を1行送り、標準出力が1行出るたびに onLine を呼ぶ(終わるまで次の行は読まない)。
    /// </summary>
    /// <param name="fileName">実行ファイル</param>
    /// <param name="args">引数</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="timeout">タイムアウト(全体。止めている間は数えない)</param>
    /// <param name="firstInput">最初に標準入力へ送る1行</param>
    /// <param name="onLine">1行ごとに呼ぶ(返事を送る・入力を閉じるための窓口つき)</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>プロセスが終わったら完了(終了コードが0以外なら例外)</returns>
    Task RunConversationAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, string firstInput, Func<string, IProcessChannel, Task> onLine, CancellationToken ct) =>
        throw new NotSupportedException("この実行役は、標準入力でのやりとりに対応していません。");

    /// <summary>コマンドを実行し、標準入力に文字を渡して閉じてから、標準出力を1行ずつ受け取る(本文を引数で渡せないとき)。</summary>
    /// <param name="fileName">実行ファイル</param>
    /// <param name="args">引数</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="timeout">タイムアウト(全体)</param>
    /// <param name="input">標準入力に渡す文字(改行を含んでよい)</param>
    /// <param name="onLine">1行ごとに呼ぶ</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>プロセスが終わったら完了(終了コードが0以外なら例外)</returns>
    Task RunWithInputAsync(string fileName, IReadOnlyList<string> args, string workDir, TimeSpan timeout,
        string input, Action<string> onLine, CancellationToken ct) =>
        throw new NotSupportedException("この実行役は、標準入力に文字を渡す実行に対応していません。");

    /// <summary>起動したままのプロセス(StartSession)に対応しているか。対応していなければ、発言ごとに起動する。</summary>
    bool SupportsSessions => false;

    /// <summary>コマンドを起動し、終わらせずに持ち続ける(標準入力で、何回でも発言を送れる)。</summary>
    /// <param name="fileName">実行ファイル</param>
    /// <param name="args">引数</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <returns>起動したプロセス</returns>
    IProcessSession StartSession(string fileName, IReadOnlyList<string> args, string workDir) =>
        throw new NotSupportedException("この実行役は、起動したままのプロセスに対応していません。");
}

/// <summary>起動したままのプロセス1つ。Dispose で終わらせる。</summary>
public interface IProcessSession : IDisposable
{
    /// <summary>まだ動いているか。</summary>
    bool IsAlive { get; }

    /// <summary>標準入力へ1行送る。</summary>
    /// <param name="line">送る行(改行は付けない)</param>
    void Send(string line);

    /// <summary>標準出力の次の1行を待つ。</summary>
    /// <param name="ct">キャンセル</param>
    /// <returns>1行(プロセスが終わって、もう出力がなければnull)</returns>
    Task<string?> ReadLineAsync(CancellationToken ct);

    /// <summary>標準エラーに出た最後の数行(失敗の説明に使う)。</summary>
    string RecentErrors { get; }
}

/// <summary>実行中のプロセスへの窓口(標準入力への送信・タイムアウトの一時停止)。</summary>
public interface IProcessChannel
{
    /// <summary>標準入力へ1行送る。</summary>
    /// <param name="line">送る行(改行は付けない)</param>
    void Send(string line);

    /// <summary>標準入力を閉じる(CLIに、もう送るものがないと伝えて終わらせる)。</summary>
    void CloseInput();

    /// <summary>タイムアウトを止める(ユーザーの返事を待つ間)。戻り値を Dispose すると、制限時間を最初から数え直す。</summary>
    /// <returns>再開の合図</returns>
    IDisposable PauseTimeout();
}
