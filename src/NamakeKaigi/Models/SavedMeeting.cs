namespace NamakeKaigi.Models;

/// <summary>会議フォルダの meeting.json に保存する状態(開き直して続きから話すために使う)。</summary>
public sealed record MeetingState
{
    /// <summary>会議の開始時刻。</summary>
    public DateTime StartedAt { get; init; } = DateTime.Now;

    /// <summary>その会議で使っていた作業フォルダ。</summary>
    public string WorkDir { get; init; } = "";

    /// <summary>AIごとの会話ID(claudeのsession_id、geminiのconversation_id)。</summary>
    public Dictionary<string, string> ConversationIds { get; init; } = new();

    /// <summary>AIごとの既読位置(Transcriptのどこまで見たか)。</summary>
    public Dictionary<string, int> SeenPositions { get; init; } = new();

    /// <summary>AIごとの選ばれていたモデルID。</summary>
    public Dictionary<string, string> Models { get; init; } = new();

    /// <summary>AIごとの選ばれていた労力ID。</summary>
    public Dictionary<string, string> Efforts { get; init; } = new();

    /// <summary>会議の発言一覧。</summary>
    public List<ChatEntry> Entries { get; init; } = new();
}

/// <summary>「過去の会議を開く」一覧に出す1件分の要約。</summary>
/// <param name="FolderName">保存先から見た会議フォルダの場所(例: 20261031_173243/名簿づくり。古い形式は 20261025_013000)</param>
/// <param name="FolderPath">会議フォルダのフルパス</param>
/// <param name="LogPath">log.md のフルパス</param>
/// <param name="StartedAt">開始日時</param>
/// <param name="Title">最初の発言(見出し用)</param>
/// <param name="MessageCount">発言数</param>
/// <param name="HasConversationId">CLIの会話IDが保存されているか(なければlog.mdから再開)</param>
public sealed record SavedMeetingSummary(
    string FolderName,
    string FolderPath,
    string LogPath,
    DateTime StartedAt,
    string Title,
    int MessageCount,
    bool HasConversationId)
{
    /// <summary>会議の名前(会議室の名前)。新しい形式のフォルダ(起動時刻/会議名)から取る。古い形式は名前がないので空。</summary>
    public string MeetingName
    {
        get
        {
            var parts = FolderName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[^1] : "";
        }
    }

    /// <summary>一覧の先頭に出す、会議の名前と区切り(名前がなければ空)。</summary>
    public string MeetingNamePrefix => MeetingName.Length > 0 ? MeetingName + " · " : "";

    /// <summary>一覧に表示する日時文字列。</summary>
    public string DisplayTime => StartedAt.ToString("yyyy/MM/dd HH:mm");

    /// <summary>再開方式のバッジ文字列。</summary>
    public string ModeBadge => HasConversationId ? "ID引き継ぎ" : "ログから再開";

    /// <summary>発言数の表示。</summary>
    public string CountText => $"{MessageCount}件の発言";
}
