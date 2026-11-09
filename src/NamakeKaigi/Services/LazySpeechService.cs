namespace NamakeKaigi.Services;

/// <summary>
/// 読み上げの部品を、最初に使うときまで作らない包み(WBS 11.51)。
/// 声の準備(Windowsの声のエンジンの読み込み)は、約0.4秒かかるので、使わない人の起動を遅くしないために、使うときまで待つ。
/// 作る前は「使える」と見なす。作って使えないと分かったら、状態の変化を知らせる(スイッチの表示が変わる)。
/// </summary>
public sealed class LazySpeechService : ISpeechService, IDisposable
{
    private readonly Func<ISpeechService> _factory;
    private readonly object _gate = new();
    private ISpeechService? _inner;

    /// <summary>包みを作る。eager が true なら、すぐに中身を作る(起動時に読み上げをONにする設定のとき)。</summary>
    /// <param name="factory">中身(本物の読み上げの部品)を作る関数</param>
    /// <param name="eager">すぐに作るか</param>
    public LazySpeechService(Func<ISpeechService> factory, bool eager = false)
    {
        _factory = factory;
        if (eager) _ = Inner;
    }

    /// <summary>中身をもう作ったか。</summary>
    public bool IsCreated
    {
        get { lock (_gate) return _inner is not null; }
    }

    /// <summary>中身を返す。まだなら、ここで作る(作るのは1回だけ)。</summary>
    private ISpeechService Inner
    {
        get
        {
            bool created = false;
            ISpeechService inner;
            lock (_gate)
            {
                if (_inner is null)
                {
                    _inner = _factory();
                    _inner.SpeakingChanged += () => SpeakingChanged?.Invoke();
                    created = true;
                }
                inner = _inner;
            }
            if (created && !inner.IsAvailable) SpeakingChanged?.Invoke(); // 使えないと分かった: スイッチの表示を直してもらう
            return inner;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _inner?.IsAvailable ?? true;

    /// <inheritdoc />
    public string UnavailableReason => _inner?.UnavailableReason ?? "";

    /// <inheritdoc />
    public bool IsSpeaking => _inner?.IsSpeaking ?? false;

    /// <inheritdoc />
    public bool NeedsSpeakerNamePrefix => _inner?.NeedsSpeakerNamePrefix ?? true;

    /// <inheritdoc />
    public string Note => _inner?.Note ?? "";

    /// <inheritdoc />
    public event Action? SpeakingChanged;

    /// <inheritdoc />
    public void Prepare() => _ = Inner;

    /// <inheritdoc />
    public void Enqueue(string text) => Inner.Enqueue(text);

    /// <inheritdoc />
    public void Enqueue(string text, string speakerId) => Inner.Enqueue(text, speakerId);

    /// <inheritdoc />
    public void Stop() => _inner?.Stop(); // 作っていなければ、止めるものもない

    /// <summary>作ってあれば、中身を片づける。</summary>
    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
