namespace NamakeKaigi.Services;

/// <summary>返答を声で読み上げる(WBS 11.35)。声の元(Windows標準・VOICEVOXなど)は、これを実装して差し替える。</summary>
public interface ISpeechService
{
    /// <summary>読み上げられる状態か(使える声が見つかったか)。</summary>
    bool IsAvailable { get; }

    /// <summary>使えない理由と、使えるようにする方法(使えるときは空)。ON/OFFのスイッチの説明に出す。</summary>
    string UnavailableReason { get; }

    /// <summary>読み上げている最中(順番待ちを含む)ならtrue。</summary>
    bool IsSpeaking { get; }

    /// <summary>読み上げの状態(読んでいる／止まった)が変わったときに呼ばれる。呼ばれるスレッドは決まっていない。</summary>
    event Action? SpeakingChanged;

    /// <summary>読み上げを順番待ちに足す(いま読んでいるものが終わってから読む)。読めない状態なら何もしない。</summary>
    /// <param name="text">読み上げる文</param>
    void Enqueue(string text);

    /// <summary>誰の発言かを添えて、読み上げを順番待ちに足す(声をメンバーごとに変える声の元は、これを使う)。</summary>
    /// <param name="text">読み上げる文</param>
    /// <param name="speakerId">話し手の識別名(Participants の識別名)</param>
    void Enqueue(string text, string speakerId) => Enqueue(text);

    /// <summary>文の先頭に、話し手の名前(「〇〇です。」)を添える必要があるか。声でメンバーを聞き分けられる声の元は、falseにする。</summary>
    bool NeedsSpeakerNamePrefix => true;

    /// <summary>いまの状態の知らせ(例: 代わりの声で読んでいる理由)。なければ空。ON/OFFのスイッチの説明に添える。</summary>
    string Note => "";

    /// <summary>使う前の準備をしておく(使えるかどうかを確かめる)。準備を後回しにする部品(LazySpeechService)だけが、中身を持つ。</summary>
    void Prepare() { }

    /// <summary>読んでいるものと、順番待ちのものを、すべて止める。</summary>
    void Stop();
}
