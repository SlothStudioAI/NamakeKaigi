using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>コンテキスト計測の警告段階。</summary>
public enum ContextAlertLevel
{
    /// <summary>通常。</summary>
    None = 0,
    /// <summary>警告(黄色)。</summary>
    Warning = 1,
    /// <summary>危険(赤色)。</summary>
    Danger = 2,
}

/// <summary>会議のコンテキスト使用量(発言数・累積文字数・各AIの実トークン数)の計測結果。</summary>
/// <param name="MessageCount">確定済みの発言数</param>
/// <param name="TotalChars">確定済みの累積文字数</param>
/// <param name="GeminiInputTokens">Gemini(transcript.jsonl)の最新 input_tokens(未取得時はnull)</param>
/// <param name="GeminiCacheReadTokens">Gemini(transcript.jsonl)の最新 cache_read_tokens(未取得時はnull)</param>
/// <param name="SpeakerStats">発言者IDごとの(発言数, 文字数)</param>
/// <param name="AgentTokens">AI識別名ごとの最新トークン数(未取得時はnullまたは空)</param>
public sealed record ContextMetrics(
    int MessageCount,
    int TotalChars,
    long? GeminiInputTokens,
    long? GeminiCacheReadTokens,
    IReadOnlyDictionary<string, (int Count, int Chars)> SpeakerStats,
    IReadOnlyDictionary<string, long>? AgentTokens = null)
{
    /// <summary>Geminiの実トークン合計(input_tokens + cache_read_tokens)。未取得時はnull。</summary>
    public long? GeminiTotalTokens =>
        GeminiInputTokens.HasValue ? GeminiInputTokens.Value + (GeminiCacheReadTokens ?? 0) : null;

    /// <summary>取得できたAIトークンのうち最大の値。どれも未取得ならnull。</summary>
    public long? PeakTokens
    {
        get
        {
            long? max = GeminiTotalTokens;
            if (AgentTokens is not null)
            {
                foreach (var v in AgentTokens.Values)
                {
                    if (!max.HasValue || v > max.Value) max = v;
                }
            }
            return max;
        }
    }

    /// <summary>空の計測結果。</summary>
    public static readonly ContextMetrics Empty = new(
        0, 0, null, null, new Dictionary<string, (int Count, int Chars)>());
}

/// <summary>現在の会議の発言数・累積文字数・各AIの実トークン数を計測する。</summary>
public static class ContextMetricsService
{
    private static readonly ConcurrentDictionary<string, string> SessionPathCache = new();

    /// <summary>発言一覧とGeminiの会話IDからコンテキスト計測結果を組み立てる。</summary>
    /// <param name="entries">確定済みの発言一覧</param>
    /// <param name="geminiConversationId">Gemini(agy)の会話ID(なければnull)</param>
    /// <param name="geminiBrainDir">Antigravity CLIのbrainフォルダ(テスト用差し替え可。null時は標準パス)</param>
    /// <returns>計測結果</returns>
    public static ContextMetrics Measure(
        IReadOnlyList<ChatEntry> entries,
        string? geminiConversationId,
        string? geminiBrainDir = null)
    {
        var ids = string.IsNullOrWhiteSpace(geminiConversationId)
            ? null
            : new Dictionary<string, string> { [Participants.GeminiId] = geminiConversationId };
        return Measure(entries, ids, geminiBrainDir);
    }

    /// <summary>発言一覧と各AIの会話ID一覧からコンテキスト計測結果を組み立てる。</summary>
    /// <param name="entries">確定済みの発言一覧</param>
    /// <param name="conversationIds">AI識別名ごとの会話ID</param>
    /// <param name="geminiBrainDir">Antigravity CLIのbrainフォルダ(省略時は標準パス)</param>
    /// <param name="claudeProjectsDir">Claude Codeのprojectsフォルダ(省略時は標準パス)</param>
    /// <param name="codexSessionsDir">Codex CLIのsessionsフォルダ(省略時は標準パス)</param>
    /// <returns>計測結果</returns>
    public static ContextMetrics Measure(
        IReadOnlyList<ChatEntry> entries,
        IReadOnlyDictionary<string, string>? conversationIds,
        string? geminiBrainDir = null,
        string? claudeProjectsDir = null,
        string? codexSessionsDir = null)
    {
        var totalChars = 0;
        var stats = new Dictionary<string, (int Count, int Chars)>();
        foreach (var e in entries)
        {
            var len = e.Text?.Length ?? 0;
            totalChars += len;
            var cur = stats.GetValueOrDefault(e.SpeakerId);
            stats[e.SpeakerId] = (cur.Count + 1, cur.Chars + len);
        }

        var agentTokens = new Dictionary<string, long>();
        long? inputTokens = null;
        long? cacheReadTokens = null;

        if (conversationIds is not null)
        {
            if (conversationIds.TryGetValue(Participants.GeminiId, out var geminiId))
            {
                (inputTokens, cacheReadTokens) = ReadGeminiTokens(geminiId, geminiBrainDir);
                if (inputTokens.HasValue)
                    agentTokens[Participants.GeminiId] = inputTokens.Value + (cacheReadTokens ?? 0);
            }
            if (conversationIds.TryGetValue(Participants.ClaudeId, out var claudeId))
            {
                var claudeTok = ReadClaudeTokens(claudeId, claudeProjectsDir);
                if (claudeTok.HasValue) agentTokens[Participants.ClaudeId] = claudeTok.Value;
            }
            if (conversationIds.TryGetValue(Participants.ChappyId, out var codexId))
            {
                var codexTok = ReadCodexTokens(codexId, codexSessionsDir);
                if (codexTok.HasValue) agentTokens[Participants.ChappyId] = codexTok.Value;
            }
        }

        return new ContextMetrics(entries.Count, totalChars, inputTokens, cacheReadTokens, stats, agentTokens);
    }

    /// <summary>transcript.jsonl から最新の input_tokens と cache_read_tokens を読み取る。</summary>
    /// <param name="conversationId">Geminiの会話ID</param>
    /// <param name="brainDir">brainフォルダのパス(省略時は %USERPROFILE%\.gemini\antigravity-cli\brain)</param>
    /// <returns>(input_tokens, cache_read_tokens)。読めない場合は (null, null)</returns>
    public static (long? InputTokens, long? CacheReadTokens) ReadGeminiTokens(
        string? conversationId,
        string? brainDir = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return (null, null);
        // 会話IDにパス区切り等が含まれる場合は無視する
        if (conversationId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || conversationId.Contains("..", StringComparison.Ordinal))
            return (null, null);

        var root = string.IsNullOrWhiteSpace(brainDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain")
            : brainDir;
        var transcriptPath = Path.Combine(root, conversationId.Trim(), ".system_generated", "logs", "transcript.jsonl");
        if (!File.Exists(transcriptPath)) return (null, null);

        long? lastInput = null;
        long? lastCache = null;
        try
        {
            using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.Contains("\"input_tokens\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (r.TryGetProperty("input_tokens", out var inp) && inp.TryGetInt64(out var inpVal))
                    {
                        lastInput = inpVal;
                        lastCache = r.TryGetProperty("cache_read_tokens", out var cr) && cr.TryGetInt64(out var crVal)
                            ? crVal
                            : 0;
                    }
                }
                catch (JsonException)
                {
                    // 書き込み途中の行などは読み飛ばす
                }
            }
        }
        catch (IOException)
        {
            return (null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }

        return (lastInput, lastCache);
    }

    private static bool IsSafeConversationId(string? conversationId) =>
        !string.IsNullOrWhiteSpace(conversationId)
        && conversationId.IndexOfAny(new[] { '/', '\\', ':' }) < 0
        && !conversationId.Contains("..", StringComparison.Ordinal);

    private static string? FindCachedSessionFile(string cacheKey, string root, string pattern, SearchOption option)
    {
        if (SessionPathCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
            return cached;
        if (!Directory.Exists(root)) return null;
        try
        {
            var found = Directory.EnumerateFiles(root, pattern, option).FirstOrDefault();
            if (found is not null) SessionPathCache[cacheKey] = found;
            return found;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Claude Codeのセッションjsonlから最新の入力トークン合計を読み取る。</summary>
    public static long? ReadClaudeTokens(string? conversationId, string? claudeProjectsDir = null)
    {
        if (!IsSafeConversationId(conversationId)) return null;
        var root = string.IsNullOrWhiteSpace(claudeProjectsDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects")
            : claudeProjectsDir;
        var id = conversationId!.Trim();
        var path = FindCachedSessionFile($"claude:{root}:{id}", root, $"{id}.jsonl", SearchOption.AllDirectories);
        if (path is null) return null;

        long? last = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (!((r.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out var u))
                          || r.TryGetProperty("usage", out u)))
                        continue;

                    long inp = u.TryGetProperty("input_tokens", out var p1) && p1.TryGetInt64(out var v1) ? v1 : 0;
                    long cr = u.TryGetProperty("cache_read_input_tokens", out var p2) && p2.TryGetInt64(out var v2) ? v2 : 0;
                    long cc = u.TryGetProperty("cache_creation_input_tokens", out var p3) && p3.TryGetInt64(out var v3) ? v3 : 0;
                    var total = inp + cr + cc;
                    if (total > 0) last = total;
                }
                catch (JsonException) { }
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        return last;
    }

    /// <summary>Codex CLIのセッションjsonlから最新の入力トークン数を読み取る。</summary>
    public static long? ReadCodexTokens(string? conversationId, string? codexSessionsDir = null)
    {
        if (!IsSafeConversationId(conversationId)) return null;
        var root = string.IsNullOrWhiteSpace(codexSessionsDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions")
            : codexSessionsDir;
        var id = conversationId!.Trim();
        var path = FindCachedSessionFile($"codex:{root}:{id}", root, $"*{id}.jsonl", SearchOption.AllDirectories);
        if (path is null) return null;

        long? last = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.Contains("\"token_count\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (r.TryGetProperty("payload", out var payload)
                        && payload.TryGetProperty("info", out var info)
                        && info.TryGetProperty("last_token_usage", out var usage)
                        && usage.TryGetProperty("input_tokens", out var inp)
                        && inp.TryGetInt64(out var val) && val > 0)
                    {
                        last = val;
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        return last;
    }

    /// <summary>いずれかの赤しきい値を超えているかを判定する。</summary>
    /// <param name="metrics">計測結果</param>
    /// <param name="thresholds">しきい値設定</param>
    /// <returns>1つ以上超えていればtrue</returns>
    public static bool IsOverThreshold(ContextMetrics metrics, SummarySettings thresholds) =>
        GetAlertLevel(metrics, thresholds) == ContextAlertLevel.Danger;

    /// <summary>コンテキスト警告の段階（通常 / 目安 / 2段階目）を判定する。</summary>
    public static ContextAlertLevel GetAlertLevel(ContextMetrics metrics, SummarySettings thresholds)
    {
        var peakTok = metrics.PeakTokens;
        if ((thresholds.MaxMessages > 0 && metrics.MessageCount >= thresholds.MaxMessages)
            || (thresholds.MaxChars > 0 && metrics.TotalChars >= thresholds.MaxChars)
            || (thresholds.MaxTokens > 0 && peakTok is long tokD && tokD >= thresholds.MaxTokens))
        {
            return ContextAlertLevel.Danger;
        }

        bool IsWarnHit(long current, int warn, int max) =>
            warn > 0 && (max <= 0 || warn <= max) && current >= warn;

        if (IsWarnHit(metrics.MessageCount, thresholds.WarnMessages, thresholds.MaxMessages)
            || IsWarnHit(metrics.TotalChars, thresholds.WarnChars, thresholds.MaxChars)
            || (peakTok is long tokW && IsWarnHit(tokW, thresholds.WarnTokens, thresholds.MaxTokens)))
        {
            return ContextAlertLevel.Warning;
        }

        return ContextAlertLevel.None;
    }

    /// <summary>入力欄下の1行表示テキストを作る。</summary>
    /// <param name="metrics">計測結果</param>
    /// <returns>例: "42発言 · 18,500字 · 62,400 tok"</returns>
    public static string FormatLine(ContextMetrics metrics)
    {
        var baseText = $"{metrics.MessageCount:N0}発言 · {metrics.TotalChars:N0}字";
        return metrics.PeakTokens is long tok
            ? $"{baseText} · {tok:N0} tok"
            : baseText;
    }

    /// <summary>ツールチップに出す詳細な内訳テキストを作る。</summary>
    /// <param name="metrics">計測結果</param>
    /// <param name="thresholds">しきい値設定</param>
    /// <param name="appearance">表示名設定(省略時は標準表示)</param>
    /// <returns>複数行の内訳テキスト</returns>
    public static string FormatTooltip(ContextMetrics metrics, SummarySettings thresholds, AppearanceSettings? appearance = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"現在の会議の長さ（区切りの目安: {thresholds.WarnMessages:N0}発言 · {thresholds.WarnChars:N0}字 · {thresholds.WarnTokens:N0} tok）");
        sb.AppendLine($"・合計: {metrics.MessageCount:N0}発言 / {metrics.TotalChars:N0}字");

        if (metrics.SpeakerStats.Count > 0)
        {
            var resolve = appearance ?? new AppearanceSettings();
            sb.AppendLine("・発言者別内訳:");
            foreach (var p in Participants.All)
            {
                if (metrics.SpeakerStats.TryGetValue(p.Id, out var st))
                {
                    long? tok = null;
                    if (metrics.AgentTokens is not null && metrics.AgentTokens.TryGetValue(p.Id, out var t))
                        tok = t;
                    else if (p.Id == Participants.GeminiId)
                        tok = metrics.GeminiTotalTokens;

                    var tokSuffix = tok.HasValue ? $" / 約{tok.Value:N0} tok" : "";
                    sb.AppendLine($"  - {resolve.ResolveParticipantName(p.Id)}: {st.Count:N0}発言 / {st.Chars:N0}字{tokSuffix}");
                }
            }
        }
        return sb.ToString().TrimEnd();
    }
}
