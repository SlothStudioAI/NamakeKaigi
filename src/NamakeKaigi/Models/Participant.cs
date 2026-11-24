namespace NamakeKaigi.Models;

/// <summary>会議の参加者の固定情報。</summary>
/// <param name="Id">内部で使う識別名(user / claude / gemini)</param>
/// <param name="DisplayName">画面に出す名前</param>
/// <param name="SubTitle">名前の下に出す説明</param>
/// <param name="LogName">会議ログ(Markdown)に書く名前</param>
/// <param name="AvatarUri">アバター画像のリソースパス(絵がまだなければ空。頭文字のバッジで出す)</param>
/// <param name="IsGuest">ゲスト(名指しのときだけ答える。「全員」には入らず、「別々に見る」の列も作らない)ならtrue。基本の参加者はfalse</param>
/// <param name="HasControls">モデル・段階(読む/読み書き/全部)・ツール設定の欄があるか(あなた以外のAIは、いまは全員true)</param>
public sealed record Participant(string Id, string DisplayName, string SubTitle, string LogName, string AvatarUri,
    bool IsGuest = false, bool HasControls = true, string BadgeText = "")
{
    /// <summary>アバターの絵があるか。</summary>
    public bool HasAvatar => AvatarUri.Length > 0;

    /// <summary>カード上に表示する小さな目印バッジがあるか。</summary>
    public bool HasBadge => BadgeText.Length > 0;
}

/// <summary>参加者の一覧と、識別名の定数。</summary>
public static class Participants
{
    /// <summary>画像などのリソースを指す完全なパスの頭(どこから動かしても同じように読める)。</summary>
    private const string Pack = "pack://application:,,,/NamakeKaigi;component";

    public const string UserId = "user";
    public const string ClaudeId = "claude";
    public const string GeminiId = "gemini";
    public const string ChappyId = "chappy";
    public const string AgyClaudeId = "agy-claude";
    public const string AgyChappyId = "agy-chappy";

    public static readonly Participant User =
        new(UserId, "あなた", "ユーザー", "ユーザー", Pack + "/Assets/namake_icon.png");

    public static readonly Participant Claude =
        new(ClaudeId, "クローディ", "Claude", "Claude", Pack + "/Assets/Avatars/leader-claude.jpg");

    public static readonly Participant Gemini =
        new(GeminiId, "ジェミリー", "Gemini", "Gemini", Pack + "/Assets/Avatars/leader-gemini.jpg");

    /// <summary>基本の参加者のチャッピー(ChatGPT / Codex CLI)。絵はコウテイペンギン。</summary>
    public static readonly Participant Chappy =
        new(ChappyId, "チャッピー", "ChatGPT", "ChatGPT", Pack + "/Assets/Avatars/leader-chappy.jpg");

    /// <summary>agy経由で呼ぶ疑似クローディ(Claude系モデル)。</summary>
    public static readonly Participant AgyClaude =
        new(AgyClaudeId, "クローディ (agy)", "Claude via agy", "Claude(agy)", Pack + "/Assets/Avatars/leader-claude.jpg", BadgeText: "agy");

    /// <summary>agy経由で呼ぶ疑似チャッピー(GPT系モデル)。</summary>
    public static readonly Participant AgyChappy =
        new(AgyChappyId, "チャッピー (agy)", "ChatGPT via agy", "ChatGPT(agy)", Pack + "/Assets/Avatars/leader-chappy.jpg", BadgeText: "agy");

    /// <summary>標準のAI参加者(設定の初期メンバー対象)。</summary>
    public static IReadOnlyList<Participant> StandardAis { get; } = new[] { Claude, Gemini, Chappy };

    /// <summary>agy経由で追加できるAI参加者。</summary>
    public static IReadOnlyList<Participant> AgyProxies { get; } = new[] { AgyClaude, AgyChappy };

    /// <summary>全参加者(画面の並び順)。</summary>
    public static IReadOnlyList<Participant> All { get; } = new[] { User, Claude, Gemini, Chappy, AgyClaude, AgyChappy };

    /// <summary>agy経由の疑似参加者かどうか。</summary>
    public static bool IsAgyProxy(string id) => id is AgyClaudeId or AgyChappyId;

    /// <summary>識別名から参加者を引く。</summary>
    /// <param name="id">識別名</param>
    /// <returns>参加者(見つからなければ例外)</returns>
    public static Participant Get(string id) => All.First(p => p.Id == id);
}
