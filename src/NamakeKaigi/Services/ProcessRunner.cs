using System.Diagnostics;
using System.IO;
using System.Text;

namespace NamakeKaigi.Services;

/// <summary>実際にプロセスを起動する実装。</summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public Task RunLinesAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, Action<string> onLine, CancellationToken ct) =>
        RunCoreAsync(fileName, args, workDir, timeout, null, onLine, ct);

    /// <inheritdoc />
    public Task RunWithInputAsync(string fileName, IReadOnlyList<string> args, string workDir, TimeSpan timeout,
        string input, Action<string> onLine, CancellationToken ct) =>
        RunCoreAsync(fileName, args, workDir, timeout, input, onLine, ct);

    /// <summary>コマンドを実行し、標準入力に文字を渡して(なければ何も渡さず)閉じてから、標準出力を1行ずつ受け取る。</summary>
    private static async Task RunCoreAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, string? input, Action<string> onLine, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ResolveExecutable(fileName))
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        DisableAgyAutoUpdate(info);
        foreach (var a in args) info.ArgumentList.Add(a);

        using var process = new Process { StartInfo = info };
        process.Start();
        Track(process);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close(); // 入力待ちで止まらないよう、渡し終えたらすぐ閉じる
            var stderr = process.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) != null)
                    onLine(line);
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                KillIfRunning(process);
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException($"{Path.GetFileName(fileName)} が {timeout.TotalSeconds:0}秒で終わりませんでした。");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(fileName)} が失敗しました(終了コード {process.ExitCode}): {Shorten(await stderr)}");
        }
        finally
        {
            // 取り消し・時間切れ以外の例外(読み取りの失敗など)で抜けたときも、CLIを残さない
            Untrack(process);
            KillIfRunning(process);
        }
    }

    // 発言ごとに起動して動いているCLIの一覧(アプリの終了時に、残っているものをまとめて止めるため)
    private static readonly HashSet<Process> Running = new();

    static ProcessRunner() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        Process[] left;
        lock (Running) left = Running.ToArray();
        foreach (var p in left) KillIfRunning(p);
    };

    /// <summary>動いているCLIの一覧に入れる(アプリの終了時に止める対象にする)。</summary>
    /// <param name="process">起動したプロセス</param>
    private static void Track(Process process)
    {
        lock (Running) Running.Add(process);
    }

    /// <summary>動いているCLIの一覧から外す。</summary>
    /// <param name="process">終わった(または止める)プロセス</param>
    private static void Untrack(Process process)
    {
        lock (Running) Running.Remove(process);
    }

    /// <summary>まだ動いていれば、子プロセスごと止める。すでに終わっている・止められないときは、何もしない。</summary>
    /// <param name="process">止めるプロセス</param>
    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // すでに終わった・破棄済み・権限がない場合は、止める必要がない(または止められない)
        }
    }

    /// <inheritdoc />
    public async Task RunConversationAsync(string fileName, IReadOnlyList<string> args, string workDir,
        TimeSpan timeout, string firstInput, Func<string, IProcessChannel, Task> onLine, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ResolveExecutable(fileName))
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), // BOMを付けると、1行目のJSONが読めなくなる
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        DisableAgyAutoUpdate(info);
        foreach (var a in args) info.ArgumentList.Add(a);

        using var process = new Process { StartInfo = info };
        process.Start();
        Track(process);
        try
        {
            var stderr = process.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var channel = new Channel(process.StandardInput, cts, timeout);
            try
            {
                channel.Send(firstInput);
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) != null)
                    await onLine(line, channel);
                channel.CloseInput();
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                KillIfRunning(process);
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException($"{Path.GetFileName(fileName)} が {timeout.TotalSeconds:0}秒で終わりませんでした。");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(fileName)} が失敗しました(終了コード {process.ExitCode}): {Shorten(await stderr)}");
        }
        finally
        {
            // 取り消し・時間切れ以外の例外(解析・承認の処理の失敗、入力の書き込みの失敗など)で抜けたときも、CLIを残さない
            Untrack(process);
            KillIfRunning(process);
        }
    }

    /// <summary>実行中のプロセスへの窓口。</summary>
    private sealed class Channel : IProcessChannel
    {
        private readonly StreamWriter _input;
        private readonly CancellationTokenSource _cts;
        private readonly TimeSpan _timeout;
        private readonly object _lock = new();
        private bool _closed;

        /// <summary>窓口を作る。</summary>
        /// <param name="input">標準入力</param>
        /// <param name="cts">タイムアウトを数えるキャンセル</param>
        /// <param name="timeout">制限時間</param>
        public Channel(StreamWriter input, CancellationTokenSource cts, TimeSpan timeout)
        {
            _input = input;
            _cts = cts;
            _timeout = timeout;
        }

        /// <inheritdoc />
        public void Send(string line)
        {
            lock (_lock)
            {
                if (_closed) return;
                _input.WriteLine(line);
                _input.Flush();
            }
        }

        /// <inheritdoc />
        public void CloseInput()
        {
            lock (_lock)
            {
                if (_closed) return;
                _closed = true;
                try { _input.Close(); }
                catch (IOException) { } // 先にプロセスが終わっていれば、閉じる先がない
            }
        }

        /// <inheritdoc />
        public IDisposable PauseTimeout()
        {
            _cts.CancelAfter(Timeout.InfiniteTimeSpan);
            return new Resume(() =>
            {
                try { _cts.CancelAfter(_timeout); }
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

    /// <inheritdoc />
    public bool SupportsSessions => true;

    /// <inheritdoc />
    public IProcessSession StartSession(string fileName, IReadOnlyList<string> args, string workDir)
    {
        var info = new ProcessStartInfo(ResolveExecutable(fileName))
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        DisableAgyAutoUpdate(info);
        foreach (var a in args) info.ArgumentList.Add(a);
        return new LiveSession(info);
    }

    /// <summary>起動したままのプロセス。アプリが終わるときは、残っているものをすべて終わらせる。</summary>
    private sealed class LiveSession : IProcessSession
    {
        // 生きているプロセスの一覧(アプリの終了時に、まとめて終わらせるため)
        private static readonly HashSet<LiveSession> Alive = new();

        // 標準エラーを覚えておく行数(失敗の説明に使う分だけ)
        private const int KeptErrorLines = 5;

        private readonly Process _process;
        private readonly Queue<string> _errors = new();
        private readonly object _lock = new();
        private bool _disposed;

        static LiveSession() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            LiveSession[] all;
            lock (Alive) all = Alive.ToArray();
            foreach (var s in all) s.Dispose();
        };

        /// <summary>プロセスを起動する。</summary>
        /// <param name="info">起動の情報</param>
        public LiveSession(ProcessStartInfo info)
        {
            _process = new Process { StartInfo = info };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (_errors)
                {
                    _errors.Enqueue(e.Data);
                    while (_errors.Count > KeptErrorLines) _errors.Dequeue();
                }
            };
            _process.Start();
            _process.BeginErrorReadLine(); // 読まずにためると、プロセスが書けずに止まるので、読み続ける
            lock (Alive) Alive.Add(this);
        }

        /// <inheritdoc />
        public bool IsAlive
        {
            get
            {
                lock (_lock) return !_disposed && !_process.HasExited;
            }
        }

        /// <inheritdoc />
        public string RecentErrors
        {
            get
            {
                lock (_errors) return string.Join("\n", _errors);
            }
        }

        /// <inheritdoc />
        public void Send(string line)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LiveSession));
                _process.StandardInput.WriteLine(line);
                _process.StandardInput.Flush();
            }
        }

        /// <inheritdoc />
        public Task<string?> ReadLineAsync(CancellationToken ct) =>
            // 読み取りそのものは止められないことがあるので、待つ側で止める(止めたあとは Dispose でプロセスごと終わらせる)
            _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(ct);

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }
            lock (Alive) Alive.Remove(this);
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { } // すでに終わっていた
            _process.Dispose();
        }
    }

    /// <summary>名前だけの実行ファイル(claude等)を、PATHから探して .exe のパスにする。
    /// .exe がなければ .cmd も探す(npm で入れたCLI(codex など)は .cmd になるため)。</summary>
    /// <param name="fileName">実行ファイル名またはパス</param>
    /// <returns>起動に使うパス(見つからなければそのまま)</returns>
    public static string ResolveExecutable(string fileName)
    {
        if (Path.IsPathRooted(fileName)) return fileName;
        var names = Path.HasExtension(fileName) ? new[] { fileName } : new[] { fileName + ".exe", fileName + ".cmd" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        foreach (var name in names)
        {
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return fileName;
    }

    /// <summary>agy の自動更新を止める環境変数の名前(値が "true" のとき止まる。"1" では止まらない。11/7 実測)。</summary>
    public const string AgyDisableAutoUpdateVariable = "AGY_CLI_DISABLE_AUTO_UPDATE";

    /// <summary>
    /// agy の自動更新を止める。更新係(agy --bg-updater)が版を確かめるときに、黒いコンソールの窓が一瞬出るため。
    /// 意図せず窓を出さないよう、このクラスで起動するものには必ず付ける(更新は、ジェミの設定画面のボタンで行う)。
    /// </summary>
    /// <param name="info">起動の設定</param>
    public static void DisableAgyAutoUpdate(ProcessStartInfo info) =>
        info.Environment[AgyDisableAutoUpdateVariable] = "true";

    /// <summary>エラー文を長すぎないように切る。</summary>
    private static string Shorten(string text) => text.Length > 300 ? text[..300] : text.Trim();
}
