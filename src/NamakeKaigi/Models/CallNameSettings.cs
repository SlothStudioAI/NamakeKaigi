namespace NamakeKaigi.Models;

/// <summary>発言の先頭の呼びかけ(「ジェミ、〜」)で宛先を切り替えるための、呼び名の一覧(settings.json の callNames)。省略した宛先は、標準の呼び名になる。</summary>
/// <param name="Claude">クローディの呼び名</param>
/// <param name="Gemini">ジェミリーの呼び名(音声入力の誤変換「ゼミ」「ジェミー」も入れてある)</param>
/// <param name="Chappy">チャッピーの呼び名</param>
/// <param name="Everyone">「全員」の呼び名</param>
public sealed record CallNameSettings(
    IReadOnlyList<string>? Claude = null,
    IReadOnlyList<string>? Gemini = null,
    IReadOnlyList<string>? Chappy = null,
    IReadOnlyList<string>? Everyone = null)
{
    /// <summary>標準の呼び名。</summary>
    public static readonly CallNameSettings Default = new();

    /// <summary>クローディの標準の呼び名。</summary>
    public static readonly IReadOnlyList<string> DefaultClaude = new[] { "クローディ", "クロード", "クロ" };

    /// <summary>ジェミリーの標準の呼び名。</summary>
    public static readonly IReadOnlyList<string> DefaultGemini = new[] { "ジェミリー", "ジェミニ", "ジェミー", "ジェミ", "ゼミ" };

    /// <summary>チャッピーの標準の呼び名。</summary>
    public static readonly IReadOnlyList<string> DefaultChappy = new[] { "チャッピー", "チャッピ", "チャット" };

    /// <summary>「全員」の標準の呼び名。</summary>
    public static readonly IReadOnlyList<string> DefaultEveryone = new[] { "全員", "みんな" };

    /// <summary>クローディの呼び名(未設定なら標準)。</summary>
    public IReadOnlyList<string> ClaudeNames => Claude ?? DefaultClaude;

    /// <summary>ジェミリーの呼び名(未設定なら標準)。</summary>
    public IReadOnlyList<string> GeminiNames => Gemini ?? DefaultGemini;

    /// <summary>チャッピーの呼び名(未設定なら標準)。</summary>
    public IReadOnlyList<string> ChappyNames => Chappy ?? DefaultChappy;

    /// <summary>「全員」の呼び名(未設定なら標準)。</summary>
    public IReadOnlyList<string> EveryoneNames => Everyone ?? DefaultEveryone;
}
