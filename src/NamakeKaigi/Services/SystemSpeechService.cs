using System.Speech.Synthesis;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>Windows標準の読み上げ(System.Speech)で、返答を読み上げる。日本語の声が見つからないときは、使えない状態になる(WBS 11.35)。</summary>
public sealed class SystemSpeechService : ISpeechService, IDisposable
{
    /// <summary>日本語の声が見つからないときの、使えない理由と直し方。</summary>
    public const string NoJapaneseVoiceReason =
        "日本語の声が見つかりません。Windowsの設定 → 時刻と言語 → 音声 で、日本語の音声を追加すると使えます。";

    /// <summary>読み上げの機能そのものを始められなかったときの理由。</summary>
    public const string StartFailedReason = "Windowsの読み上げ機能を始められませんでした。";

    private readonly SpeechSynthesizer? _synth;
    private readonly object _gate = new();
    private int _pending;

    /// <summary>声を探して、読み上げの準備をする。見つからなければ、使えない状態で作る。</summary>
    /// <param name="settings">読み上げの設定(声の名前・速さ)</param>
    public SystemSpeechService(VoiceSettings settings)
    {
        UnavailableReason = StartFailedReason;
        try
        {
            var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices().Where(v => v.Enabled)
                .Select(v => (v.VoiceInfo.Name, v.VoiceInfo.Culture.Name)).ToList();
            var name = PickVoice(voices, settings.VoiceName);
            if (name is null)
            {
                synth.Dispose();
                UnavailableReason = NoJapaneseVoiceReason;
                return;
            }

            synth.SelectVoice(name);
            synth.Rate = Math.Clamp(settings.Rate, -10, 10);
            synth.SetOutputToDefaultAudioDevice();
            synth.SpeakCompleted += (_, _) => Finished();
            _synth = synth;
            UnavailableReason = "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException
                                       or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _synth = null; // 声の機能が使えない環境でも、アプリは動かす(読み上げだけ使えない)
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _synth is not null;

    /// <inheritdoc />
    public string UnavailableReason { get; }

    /// <inheritdoc />
    public bool IsSpeaking
    {
        get { lock (_gate) return _pending > 0; }
    }

    /// <inheritdoc />
    public event Action? SpeakingChanged;

    /// <inheritdoc />
    public void Enqueue(string text)
    {
        if (_synth is null || string.IsNullOrWhiteSpace(text)) return;
        lock (_gate) _pending++;
        SpeakingChanged?.Invoke();
        _synth.SpeakAsync(text); // 順番に読まれる(重ならない)
    }

    /// <inheritdoc />
    public void Stop()
    {
        _synth?.SpeakAsyncCancelAll(); // 止めた分も SpeakCompleted が来て、順番待ちの数が減る
    }

    /// <summary>1件の読み上げが終わった(止められた)。</summary>
    private void Finished()
    {
        lock (_gate) _pending = Math.Max(0, _pending - 1);
        SpeakingChanged?.Invoke();
    }

    /// <summary>入っている声の中から、使う声を決める。名前の指定があって入っていればそれ、なければ日本語の声のうち最初のもの。</summary>
    /// <param name="voices">入っている声(名前, 言語。例: "ja-JP")</param>
    /// <param name="preferredName">設定で指定された声の名前(なければnullか空)</param>
    /// <returns>使う声の名前。使える声がなければnull</returns>
    public static string? PickVoice(IEnumerable<(string Name, string Culture)> voices, string? preferredName)
    {
        var list = voices.ToList();
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = list.FirstOrDefault(v => string.Equals(v.Name, preferredName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (preferred.Name is not null) return preferred.Name;
        }
        var japanese = list.FirstOrDefault(v => v.Culture.StartsWith("ja", StringComparison.OrdinalIgnoreCase));
        return japanese.Name;
    }

    /// <summary>読み上げの部品を片づける。</summary>
    public void Dispose() => _synth?.Dispose();
}
