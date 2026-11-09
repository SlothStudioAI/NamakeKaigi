using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services.Voicevox;

/// <summary>
/// VOICEVOXの声で読み上げる(WBS 11.36)。メンバーごとに声を変え、文ごとに音声を作って、読んでいる間に次の文を先に作る(先読み)。
/// VOICEVOXにつながらないとき(起動していない等)は、代わりの声(Windows標準)で読む。この機能を外すときは、Services/Voicevox フォルダと、設定の VoicevoxSettings を消す。
/// </summary>
public sealed class VoicevoxSpeechService : ISpeechService, IDisposable
{
    private static readonly Regex SentenceEnd = new(@"[^。！？!?\n]*[。！？!?]+|[^。！？!?\n]+", RegexOptions.Compiled);

    private static readonly Regex ClauseEnd = new(@"[^、,，]*[、,，]+|[^、,，]+", RegexOptions.Compiled);

    private readonly VoicevoxSettings _settings;
    private readonly IVoicevoxClient _client;
    private readonly IWavPlayer _player;
    private readonly ISpeechService _fallback;
    private readonly object _gate = new();
    private readonly Queue<(string Text, string SpeakerId)> _queue = new();
    private CancellationTokenSource _cts = new();
    private bool _running;
    private int _active; // 順番待ち・読んでいる最中の発言の数
    private string _note = "";

    /// <summary>VOICEVOXで読む部品を作る。</summary>
    /// <param name="settings">VOICEVOXの設定</param>
    /// <param name="client">VOICEVOXに問い合わせる部品</param>
    /// <param name="player">WAVを鳴らす部品</param>
    /// <param name="fallback">VOICEVOXにつながらないときに使う、代わりの声</param>
    public VoicevoxSpeechService(VoicevoxSettings settings, IVoicevoxClient client, IWavPlayer player, ISpeechService fallback)
    {
        _settings = settings;
        _client = client;
        _player = player;
        _fallback = fallback;
        _fallback.SpeakingChanged += () => SpeakingChanged?.Invoke();
    }

    /// <summary>設定から、本物のVOICEVOX版を作る。</summary>
    /// <param name="settings">VOICEVOXの設定</param>
    /// <param name="fallback">VOICEVOXにつながらないときに使う、代わりの声</param>
    /// <returns>VOICEVOXで読む部品</returns>
    public static VoicevoxSpeechService Create(VoicevoxSettings settings, ISpeechService fallback) =>
        new(settings, new VoicevoxClient(settings), new SoundWavPlayer(), fallback);

    /// <inheritdoc />
    public bool IsAvailable => true; // つながらなければ、代わりの声で読む(それも使えなければ、何も鳴らさず、知らせに書く)

    /// <inheritdoc />
    public string UnavailableReason => "";

    /// <inheritdoc />
    public bool NeedsSpeakerNamePrefix => false; // 声で聞き分けられる(代わりの声のときだけ、中で名前を添える)

    /// <inheritdoc />
    public string Note
    {
        get { lock (_gate) return _note; }
    }

    /// <inheritdoc />
    public bool IsSpeaking
    {
        get
        {
            lock (_gate)
            {
                if (_active > 0) return true;
            }
            return _fallback.IsSpeaking;
        }
    }

    /// <inheritdoc />
    public event Action? SpeakingChanged;

    /// <inheritdoc />
    public void Enqueue(string text) => Enqueue(text, "");

    /// <inheritdoc />
    public void Enqueue(string text, string speakerId)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_gate)
        {
            _queue.Enqueue((text, speakerId));
            _active++;
            if (!_running)
            {
                _running = true;
                _ = Task.Run(WorkerAsync);
            }
        }
        SpeakingChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Stop()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            _active = Math.Max(0, _active - _queue.Count);
            _queue.Clear();
            old = _cts;
            _cts = new CancellationTokenSource();
        }
        old.Cancel();
        _player.Stop();
        _fallback.Stop();
        SpeakingChanged?.Invoke();
    }

    /// <summary>順番待ちの発言を、1つずつ読む。</summary>
    private async Task WorkerAsync()
    {
        try
        {
            while (true)
            {
                (string Text, string SpeakerId) item;
                CancellationToken token;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    item = _queue.Dequeue();
                    token = _cts.Token; // 止められたあとに足された発言は、新しい取り消しの印で読む
                }

                try
                {
                    await SpeakAsync(item.Text, item.SpeakerId, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 止められた
                }
                catch (Exception)
                {
                    // 音声合成や再生中の想定外のエラー(破損データなど)はスキップして次の発言へ進む
                }
                finally
                {
                    lock (_gate) _active = Math.Max(0, _active - 1);
                    SpeakingChanged?.Invoke();
                }
            }
        }
        finally
        {
            lock (_gate) _running = false;
        }
    }

    /// <summary>1つの発言を、文ごとに音声にして読む(読んでいる間に、次の文を先に作る)。</summary>
    private async Task SpeakAsync(string text, string speakerId, CancellationToken token)
    {
        var sentences = SplitForSpeaking(text, _settings.MaxChunkChars);
        if (sentences.Count == 0) return;

        var speaker = _settings.SpeakerOf(speakerId);
        if (speaker is null)
        {
            SpeakWithFallback(speakerId, sentences, 0); // この人の声が決まっていない
            return;
        }

        Task<byte[]?> Synthesize(int index) => SynthesizeOrNullAsync(sentences[index], speaker, token);
        var next = Synthesize(0);
        for (var i = 0; i < sentences.Count; i++)
        {
            var current = next;
            next = i + 1 < sentences.Count ? Synthesize(i + 1) : Task.FromResult<byte[]?>(null);
            var wav = await current.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            if (wav is null)
            {
                SetNote(_fallback.IsAvailable
                    ? "VOICEVOXにつながらないので、Windowsの声で読んでいます。"
                    : "VOICEVOXにつながらず、Windowsの声も使えないので、読めません。");
                SpeakWithFallback(speakerId, sentences, i);
                return;
            }
            SetNote("");
            await _player.PlayAsync(wav, token).ConfigureAwait(false);
        }
    }

    /// <summary>1文を音声にする。つながらない・失敗したときはnull(取り消されたときは、そのまま取り消しにする)。</summary>
    private async Task<byte[]?> SynthesizeOrNullAsync(string sentence, VoicevoxSpeaker speaker, CancellationToken token)
    {
        try
        {
            return await _client.SynthesizeAsync(sentence, speaker, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or InvalidOperationException or IOException)
        {
            return null; // VOICEVOXが起動していない・時間切れ・失敗
        }
    }

    /// <summary>代わりの声で、残りの文を読む(声で聞き分けられないので、名前を添える)。</summary>
    private void SpeakWithFallback(string speakerId, IReadOnlyList<string> sentences, int from)
    {
        var rest = string.Concat(sentences.Skip(from));
        var name = Participants.All.FirstOrDefault(p => p.Id == speakerId)?.DisplayName;
        _fallback.Enqueue(name is null ? rest : $"{name}です。{rest}");
    }

    private void SetNote(string note)
    {
        lock (_gate)
        {
            if (_note == note) return;
            _note = note;
        }
        SpeakingChanged?.Invoke();
    }

    /// <summary>本文を、文の終わり(。！？)と改行で、文ごとに分ける。</summary>
    /// <param name="text">読み上げる文</param>
    /// <returns>文の一覧(空の文は含めない)</returns>
    public static IReadOnlyList<string> SplitSentences(string text) =>
        SentenceEnd.Matches(text ?? "").Select(m => m.Value.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>
    /// 音声にする単位に分ける。文ごとに分けたうえで、長い文は読点(、)で区切る
    /// (音声を作る時間は短い文ほど短いので、鳴り始めが早くなり、次の文の準備も間に合いやすい)。
    /// </summary>
    /// <param name="text">読み上げる文</param>
    /// <param name="maxChunkChars">1回の長さの目安(文字数)。0以下なら、文のまま</param>
    /// <returns>音声にする単位の一覧</returns>
    public static IReadOnlyList<string> SplitForSpeaking(string text, int maxChunkChars)
    {
        var chunks = new List<string>();
        foreach (var sentence in SplitSentences(text))
        {
            if (maxChunkChars <= 0 || sentence.Length <= maxChunkChars)
            {
                chunks.Add(sentence);
                continue;
            }

            // 読点のところで切った節を、目安の長さに収まる間だけつなぐ(1つの節が長くても、それ以上は切らない)
            var current = "";
            foreach (var clause in ClauseEnd.Matches(sentence).Select(m => m.Value))
            {
                if (current.Length > 0 && current.Length + clause.Length > maxChunkChars)
                {
                    chunks.Add(current);
                    current = "";
                }
                current += clause;
            }
            if (current.Length > 0) chunks.Add(current);
        }
        return chunks;
    }

    /// <summary>通信の部品を片づける。</summary>
    public void Dispose() => (_client as IDisposable)?.Dispose();
}
