using System.IO;

namespace NamakeKaigi.Models;

/// <summary>簡易表示モード(イラストの代わりに頭文字を出す)と、参加者名のカスタム表示設定。アプリ名は変えられない。</summary>
/// <param name="IsBusinessMode">簡易表示モード(キャラ絵を隠して頭文字バッジにし、パスを省略表示する)ならtrue</param>
/// <param name="UserName">あなたの表示名(空なら「あなた」)</param>
/// <param name="ClaudeName">Claudeの表示名(空なら通常時「クローディ」、簡易表示時「Claude」)</param>
/// <param name="GeminiName">Geminiの表示名(空なら通常時「ジェミリー」、簡易表示時「Gemini」)</param>
public sealed record AppearanceSettings(
    bool IsBusinessMode = false,
    string? UserName = null,
    string? ClaudeName = null,
    string? GeminiName = null)
{
    public static readonly AppearanceSettings Default = new();

    /// <summary>アプリの名前(ウィンドウの Title=タスクバー・Alt+Tab)。製品の名前なので、設定では変えない。</summary>
    public const string AppName = "NamakeKaigi";

    /// <summary>タイトルバーに出す見出し(アプリ名と MDD)。簡易表示モードでも同じ。</summary>
    public const string WindowHeaderTitle = AppName + " — Meeting-Driven Development";

    /// <summary>参加者の識別名から、現在の画面に出す表示名を返す。</summary>
    public string ResolveParticipantName(string participantId) => participantId switch
    {
        Participants.UserId => !string.IsNullOrWhiteSpace(UserName) ? UserName.Trim() : "あなた",
        Participants.ClaudeId => !string.IsNullOrWhiteSpace(ClaudeName) ? ClaudeName.Trim()
            : IsBusinessMode ? "Claude" : Participants.Claude.DisplayName,
        Participants.GeminiId => !string.IsNullOrWhiteSpace(GeminiName) ? GeminiName.Trim()
            : IsBusinessMode ? "Gemini" : Participants.Gemini.DisplayName,
        Participants.ChappyId => IsBusinessMode ? "ChatGPT" : Participants.Chappy.DisplayName,
        _ => Participants.Get(participantId).DisplayName,
    };

    /// <summary>簡易表示モード時の丸バッジに出す1〜2文字の頭文字を返す。</summary>
    public static string ResolveInitial(string participantId, string displayName)
    {
        var trimmed = displayName.Trim();
        if (participantId == Participants.UserId && trimmed == "あなた") return "U";
        if (participantId == Participants.ClaudeId && (trimmed == "クローディ" || trimmed.Equals("Claude", StringComparison.OrdinalIgnoreCase))) return "C";
        if (participantId == Participants.GeminiId && (trimmed == "ジェミリー" || trimmed.Equals("Gemini", StringComparison.OrdinalIgnoreCase))) return "G";
        // ChatGPT の頭文字「C」は Claude と重なるので、「GP」にする
        if (participantId == Participants.ChappyId && trimmed.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "GP";
        return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
    }

    /// <summary>簡易表示モード時はフルパスを末尾の名前だけに省略して返す。</summary>
    public string FormatWorkDir(string workDir)
    {
        if (!IsBusinessMode || string.IsNullOrWhiteSpace(workDir)) return workDir;
        var trimmed = workDir.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? workDir : name;
    }

    /// <summary>簡易表示モード時はログのパスを「(1つ上のフォルダ名)/(ファイル名)」だけに省略して返す。</summary>
    public string FormatLogPath(string logPath)
    {
        if (!IsBusinessMode || string.IsNullOrWhiteSpace(logPath)) return logPath;
        var fileName = Path.GetFileName(logPath);
        var parentDir = Path.GetDirectoryName(logPath);
        var folderName = string.IsNullOrEmpty(parentDir) ? "" : Path.GetFileName(parentDir.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(folderName) ? fileName : $"{folderName}/{fileName}";
    }
}
