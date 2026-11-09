using System.IO;

namespace NamakeKaigi.Services;

/// <summary>
/// AI1人ぶんの、起動したままのCLI(11.2①)。発言のたびに起動しないので、2回目からの返事が速い。
/// 起動の条件(引数・作業フォルダ)か、続ける会話が変わったときだけ、起動し直す。
/// </summary>
public sealed class KeptCli : IDisposable
{
    private readonly IProcessRunner _runner;
    private readonly object _lock = new();
    private IProcessSession? _session;
    private string _key = "";
    private string? _conversationId;

    /// <summary>係を作る。</summary>
    /// <param name="runner">プロセス実行(StartSession に対応しているもの)</param>
    public KeptCli(IProcessRunner runner) => _runner = runner;

    /// <summary>いまCLIが起動したままか。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock) return _session is { IsAlive: true };
        }
    }

    /// <summary>起動してからいままでに、起動し直した回数を含む起動の回数(試験・確認用)。</summary>
    public int StartCount { get; private set; }

    /// <summary>直近の1回で、CLIを新しく起動したか(false なら、起動したままのCLIを使い回した)。発言ごとの時間の記録に使う(WBS 11.51)。</summary>
    public bool LastTurnStartedNew { get; private set; }

    /// <summary>
    /// 直近の1回で起動し直した理由(使い回したときはnull)。first=最初の起動、dead=CLIが終了していた、
    /// key=実行ファイル・作業フォルダ・引数が変わった、conversation=続ける会話が変わった。
    /// </summary>
    public string? LastStartReason { get; private set; }

    /// <summary>
    /// 1回分の発言を送り、答えの終わりまで読む。起動の条件と会話が同じなら、起動したままのCLIを使い回す。
    /// 止められたとき・時間切れ・失敗したときは、CLIを終わらせる(次の発言で起動し直す)。
    /// </summary>
    /// <param name="fileName">実行ファイル</param>
    /// <param name="baseArgs">起動の引数(会話を続ける指定は除く。これが変わったら起動し直す)</param>
    /// <param name="resumeArgs">会話を続けるときに足す引数(新しく起動するときだけ使う)</param>
    /// <param name="workDir">作業フォルダ</param>
    /// <param name="conversationId">続ける会話のID(新しい会話ならnull)</param>
    /// <param name="input">標準入力へ送る1行(発言)</param>
    /// <param name="timeout">1回の制限時間(承認待ちの間は止められる)</param>
    /// <param name="onLine">出力の1行ごとに呼ぶ。答えの終わりの行ならtrueを返す</param>
    /// <param name="ct">キャンセル(停止ボタン)</param>
    public async Task RunTurnAsync(string fileName, IReadOnlyList<string> baseArgs, IReadOnlyList<string> resumeArgs,
        string workDir, string? conversationId, string input, TimeSpan timeout,
        Func<string, IProcessChannel, Task<bool>> onLine, CancellationToken ct)
    {
        var key = fileName + "\n" + workDir + "\n" + string.Join("\n", baseArgs);
        var startsBefore = StartCount;
        var session = Prepare(fileName, baseArgs, resumeArgs, workDir, conversationId, key);
        LastTurnStartedNew = StartCount != startsBefore;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var channel = new TurnChannel(session, cts, timeout);
        try
        {
            session.Send(input);
            while (true)
            {
                var line = await session.ReadLineAsync(cts.Token);
                if (line is null)
                    throw new InvalidOperationException($"{Path.GetFileName(fileName)} が途中で終わりました: {session.RecentErrors}");
                if (await onLine(line, channel)) return;
            }
        }
        catch (OperationCanceledException)
        {
            Stop();
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"{Path.GetFileName(fileName)} が {timeout.TotalSeconds:0}秒で答え終わりませんでした。");
        }
        catch
        {
            Stop(); // 途中の状態が分からないので、次の発言は起動し直す
            throw;
        }
    }

    /// <summary>答えが済んだ会話のIDを覚える(次の発言が同じ会話なら、使い回す)。</summary>
    /// <param name="conversationId">会話のID</param>
    public void MarkConversation(string conversationId)
    {
        lock (_lock) _conversationId = conversationId;
    }

    /// <summary>CLIを終わらせる(次の発言で起動し直す)。</summary>
    public void Stop()
    {
        IProcessSession? old;
        lock (_lock)
        {
            old = _session;
            _session = null;
            _key = "";
            _conversationId = null;
        }
        old?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    /// <summary>使い回せるならそのCLIを、だめなら起動し直したCLIを返す。</summary>
    private IProcessSession Prepare(string fileName, IReadOnlyList<string> baseArgs, IReadOnlyList<string> resumeArgs,
        string workDir, string? conversationId, string key)
    {
        string reason;
        lock (_lock)
        {
            if (_session is { IsAlive: true } && _key == key && _conversationId == conversationId)
            {
                LastStartReason = null;
                return _session;
            }
            reason = StartCount == 0 ? "first"
                : _session is not { IsAlive: true } ? "dead"
                : _key != key ? "key"
                : "conversation";
        }
        LastStartReason = reason;
        Stop();
        var args = string.IsNullOrEmpty(conversationId) ? baseArgs : baseArgs.Concat(resumeArgs).ToList();
        var started = _runner.StartSession(fileName, args, workDir);
        lock (_lock)
        {
            _session = started;
            _key = key;
            _conversationId = conversationId;
            StartCount++;
        }
        return started;
    }

    /// <summary>1回分の発言のあいだの、プロセスへの窓口(入力は閉じない)。</summary>
    private sealed class TurnChannel(IProcessSession session, CancellationTokenSource cts, TimeSpan timeout) : IProcessChannel
    {
        /// <inheritdoc />
        public void Send(string line) => session.Send(line);

        /// <inheritdoc />
        public void CloseInput() { } // 起動したままにするので、入力は閉じない

        /// <inheritdoc />
        public IDisposable PauseTimeout()
        {
            cts.CancelAfter(Timeout.InfiniteTimeSpan);
            return new Resume(() =>
            {
                try { cts.CancelAfter(timeout); }
                catch (ObjectDisposedException) { } // もう終わっていれば、数え直す必要はない
            });
        }

        /// <summary>Dispose で、渡した処理を1回だけ呼ぶ。</summary>
        private sealed class Resume(Action action) : IDisposable
        {
            private Action? _action = action;

            /// <inheritdoc />
            public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}
