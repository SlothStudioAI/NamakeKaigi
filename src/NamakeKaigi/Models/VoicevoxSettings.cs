namespace NamakeKaigi.Models;

/// <summary>VOICEVOXで読むときの、1人分の声(話者のスタイルID・抑揚・話速)。</summary>
/// <param name="StyleId">VOICEVOXのスタイルID(例: 冥鳴ひまり ノーマル=14)</param>
/// <param name="IntonationScale">抑揚(動画と同じ1.7が標準)</param>
/// <param name="SpeedScale">話速(1.0が標準)</param>
public sealed record VoicevoxSpeaker(int StyleId, double IntonationScale = 1.7, double SpeedScale = 1.0);

/// <summary>VOICEVOXで読み上げるときの設定(settings.json の voice.voicevox)。voice.engine が voicevox のときだけ使う。</summary>
/// <param name="BaseUrl">起動中のVOICEVOXのアドレス</param>
/// <param name="TimeoutSec">VOICEVOXへの1回の問い合わせの制限時間(秒。過ぎたら、代わりの声で読む)</param>
/// <param name="Speakers">メンバーごとの声(キーは claude / gemini / chappy。書かないメンバーは標準の声。標準にもないメンバーは代わりの声)</param>
/// <param name="MaxChunkChars">1回に音声にする長さの目安(文字数。これより長い文は、読点「、」で区切る。短いほど鳴り始めが早いが、区切りが増える。0なら区切らない)</param>
public sealed record VoicevoxSettings(
    string BaseUrl = "http://127.0.0.1:50021",
    int TimeoutSec = 20,
    IReadOnlyDictionary<string, VoicevoxSpeaker>? Speakers = null,
    int MaxChunkChars = 25)
{
    /// <summary>標準の声(動画で決めた声。チャッピーは玄野武宏)。</summary>
    public static readonly IReadOnlyDictionary<string, VoicevoxSpeaker> DefaultSpeakers = new Dictionary<string, VoicevoxSpeaker>
    {
        [Participants.ClaudeId] = new(14),  // 冥鳴ひまり ノーマル
        [Participants.GeminiId] = new(54),  // 春歌ナナ ノーマル
        [Participants.ChappyId] = new(11),  // 玄野武宏 ノーマル
    };

    /// <summary>そのメンバーの声を返す(設定にあればそれ、なければ標準。どちらにもなければnull)。</summary>
    /// <param name="speakerId">メンバーの識別名</param>
    /// <returns>声。なければnull</returns>
    public VoicevoxSpeaker? SpeakerOf(string speakerId)
    {
        if (Speakers is not null && Speakers.TryGetValue(speakerId, out var configured)) return configured;
        return DefaultSpeakers.GetValueOrDefault(speakerId);
    }
}
