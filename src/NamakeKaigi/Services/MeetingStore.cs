using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// 会議の状態(meeting.json)の保存・読み込みと、log.md からの復元を行う。
/// 置き場所は「保存先/起動した時刻/会議名/」(中に log.md・meeting.json・attachments/)。
/// 古い形式「保存先/日時/」(会議フォルダが保存先の直下にある)も、そのまま読める。
/// </summary>
public static class MeetingStore
{
    /// <summary>会議フォルダの中の状態ファイル名。</summary>
    public const string StateFileName = "meeting.json";

    /// <summary>会議フォルダの中のログのファイル名。</summary>
    public const string LogFileName = "log.md";

    private const string TimestampFormat = "yyyyMMdd_HHmmss";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // "## ユーザー → ジェミリー (12:31:00)" または "## Claude (12:30:09)"
    private static readonly Regex HeadingRegex = new(
        @"^##\s+(?<who>[^\s(→]+)(?:\s*→\s*(?<targets>[^\s(]+))?\s*\((?<time>\d{2}:\d{2}:\d{2})\)\s*$",
        RegexOptions.Compiled);

    /// <summary>会議の状態を、状態ファイルに保存する(会議フォルダがなければ作る)。</summary>
    /// <param name="statePath">状態ファイルのパス(MarkdownLogWriter.StatePath)</param>
    /// <param name="state">保存する会議状態</param>
    public static void SaveState(string statePath, MeetingState state)
    {
        var dir = Path.GetDirectoryName(statePath);
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(statePath, json, Utf8);
    }

    /// <summary>名前が日時(yyyyMMdd_HHmmss)の形ならtrue。</summary>
    /// <param name="text">フォルダ名など</param>
    public static bool IsStamp(string? text) =>
        text is not null &&
        DateTime.TryParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>そのフォルダが会議フォルダ(log.md か meeting.json がある)ならtrue。</summary>
    /// <param name="dir">フォルダのパス</param>
    public static bool IsMeetingFolder(string dir) =>
        File.Exists(Path.Combine(dir, LogFileName)) || File.Exists(Path.Combine(dir, StateFileName));

    /// <summary>会議フォルダから状態を復元する。meeting.json がなければ log.md から発言を読み取る。</summary>
    /// <param name="folderPath">会議フォルダのパス(新しい形式は 保存先/起動時刻/会議名、古い形式は 保存先/日時)</param>
    /// <returns>復元した会議状態</returns>
    public static MeetingState LoadState(string folderPath)
    {
        var statePath = Path.Combine(folderPath, StateFileName);
        if (File.Exists(statePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<MeetingState>(File.ReadAllText(statePath), JsonOptions);
                if (loaded is not null) return loaded;
            }
            catch (Exception)
            {
                // 壊れていた場合は下の log.md からの復元にフォールバックする
            }
        }

        var startedAt = FolderTime(folderPath);
        var logPath = Path.Combine(folderPath, LogFileName);
        var entries = File.Exists(logPath) ? ParseMarkdownLog(logPath, startedAt) : new List<ChatEntry>();
        return new MeetingState
        {
            StartedAt = startedAt,
            Entries = entries,
        };
    }

    /// <summary>
    /// 保存先フォルダから、過去の会議一覧(発言が1件以上あるもの、新しい順)を返す。
    /// 新しい形式(起動時刻のフォルダの中の会議フォルダ)と、古い形式(保存先の直下の会議フォルダ)の両方を拾う。
    /// </summary>
    /// <param name="meetingsDir">meetings フォルダのパス</param>
    public static IReadOnlyList<SavedMeetingSummary> ListMeetings(string meetingsDir)
    {
        if (string.IsNullOrWhiteSpace(meetingsDir) || !Directory.Exists(meetingsDir))
            return Array.Empty<SavedMeetingSummary>();

        var candidates = SafeSubDirs(meetingsDir)
            .SelectMany(dir => IsMeetingFolder(dir) ? new[] { dir } : SafeSubDirs(dir).Where(IsMeetingFolder));

        var result = new List<SavedMeetingSummary>();
        foreach (var dir in candidates)
        {
            var state = LoadState(dir);
            if (state.Entries.Count == 0) continue; // 空の会議は一覧に出さない

            var firstUser = state.Entries.FirstOrDefault(e => e.SpeakerId == Participants.UserId)?.Text
                            ?? state.Entries[0].Text;
            var oneLine = firstUser.Replace("\r", " ").Replace("\n", " ").Trim();
            if (oneLine.Length > 42) oneLine = oneLine[..42] + "…";

            // 例: 20261031_173243/名簿づくり(古い形式は 20261025_013000)
            var folderName = Path.GetRelativePath(meetingsDir, dir).Replace('\\', '/');
            var hasConvId = state.ConversationIds.Values.Any(v => !string.IsNullOrWhiteSpace(v));
            result.Add(new SavedMeetingSummary(
                folderName,
                dir,
                Path.Combine(dir, LogFileName),
                state.StartedAt,
                oneLine,
                state.Entries.Count,
                hasConvId));
        }

        return result.OrderByDescending(m => m.StartedAt).ThenByDescending(m => m.FolderName).ToList();
    }

    /// <summary>会議フォルダの開始時刻。フォルダ名が日時ならそれ、そうでなければ1つ上(起動した時刻)のフォルダ名。どちらでもなければ現在時刻。</summary>
    private static DateTime FolderTime(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return IsStamp(name) ? ParseFolderTime(name) : ParseFolderTime(Path.GetFileName(Path.GetDirectoryName(trimmed) ?? ""));
    }

    /// <summary>直下のフォルダの一覧(読めなければ空)。</summary>
    private static IEnumerable<string> SafeSubDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>log.md の見出しと本文から発言一覧を復元する(meeting.json がない既存の会議用)。</summary>
    public static List<ChatEntry> ParseMarkdownLog(string logPath, DateTime baseDate)
    {
        var entries = new List<ChatEntry>();
        if (!File.Exists(logPath)) return entries;

        var lines = File.ReadAllLines(logPath);
        string? currentSpeaker = null;
        IReadOnlyList<string>? currentTargets = null;
        DateTime currentTime = baseDate;
        var body = new StringBuilder();

        void Flush()
        {
            if (currentSpeaker is null) return;
            var text = body.ToString().Trim();
            if (text.Length > 0)
            {
                entries.Add(new ChatEntry(currentSpeaker, text, currentTime, currentTargets));
            }
            currentSpeaker = null;
            currentTargets = null;
            body.Clear();
        }

        foreach (var line in lines)
        {
            var m = HeadingRegex.Match(line);
            if (m.Success)
            {
                Flush();
                currentSpeaker = ResolveSpeakerId(m.Groups["who"].Value);
                currentTargets = ResolveTargets(m.Groups["targets"].Value, currentSpeaker);
                if (TimeSpan.TryParseExact(m.Groups["time"].Value, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var ts))
                {
                    currentTime = baseDate.Date + ts;
                }
                continue;
            }

            if (currentSpeaker is not null)
            {
                body.AppendLine(line);
            }
        }
        Flush();
        return entries;
    }

    private static string ResolveSpeakerId(string logName) => logName switch
    {
        "Claude" or "クローディ" => Participants.ClaudeId,
        "Gemini" or "ジェミリー" => Participants.GeminiId,
        "ChatGPT" or "チャッピー" => Participants.ChappyId,
        _ => Participants.UserId,
    };

    private static IReadOnlyList<string>? ResolveTargets(string rawTargets, string speakerId)
    {
        if (speakerId != Participants.UserId) return null;
        if (string.IsNullOrWhiteSpace(rawTargets))
            return new[] { Participants.ClaudeId, Participants.GeminiId };

        var list = new List<string>();
        foreach (var part in rawTargets.Split('・', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var id = ResolveSpeakerId(part);
            if (id != Participants.UserId && !list.Contains(id)) list.Add(id);
        }
        return list.Count > 0 ? list : new[] { Participants.ClaudeId, Participants.GeminiId };
    }

    private static DateTime ParseFolderTime(string stamp)
    {
        if (DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            return dt;
        }
        return DateTime.Now;
    }
}
