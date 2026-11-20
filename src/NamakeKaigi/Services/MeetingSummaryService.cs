using System.Text;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>軽量モデルを使って会議の発言一覧から引き継ぎ・アーカイブ用の要約(summary.md)を作成する。</summary>
public sealed class MeetingSummaryService
{
    private const int MaxTranscriptCharsForSummary = 32000;

    private readonly Func<SummarySettings, IChatAgent?> _agentFactory;

    /// <summary>要約サービスを作る。</summary>
    /// <param name="agentFactory">要約設定に応じた単発・読み取り専用のAIを作る関数</param>
    public MeetingSummaryService(Func<SummarySettings, IChatAgent?> agentFactory)
    {
        _agentFactory = agentFactory;
    }

    private static readonly string[] AutoCandidateOrder =
    {
        Participants.ClaudeId,
        Participants.GeminiId,
        Participants.ChappyId,
    };

    /// <summary>会議の発言一覧から要約テキストを生成する。</summary>
    /// <param name="entries">会議の発言一覧</param>
    /// <param name="settings">要約設定</param>
    /// <param name="title">会議のタイトル(あれば)</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>生成された要約(Markdown)</returns>
    public async Task<string> SummarizeAsync(
        IReadOnlyList<ChatEntry> entries,
        SummarySettings settings,
        string? title = null,
        CancellationToken ct = default)
    {
        if (!settings.Enabled)
            throw new InvalidOperationException("会議の要約機能が無効になっています。設定画面から有効にしてください。");
        if (entries.Count == 0)
            throw new InvalidOperationException("要約対象の発言がありません。");

        var prompt = BuildSummaryPrompt(entries, title);
        var isAuto = string.IsNullOrWhiteSpace(settings.AgentId);

        if (!isAuto)
        {
            var agent = _agentFactory(settings)
                ?? throw new InvalidOperationException("要約に使えるAIが見つかりません。設定画面で要約に使うAIを確認してください。");
            var reply = await agent.AskAsync(prompt, conversationId: null, onUpdate: null, ct);
            var text = reply.Text?.Trim() ?? "";
            if (text.Length == 0)
                throw new InvalidOperationException("AIから空の要約が返されました。");
            return text;
        }

        var hadAgent = false;
        Exception? lastError = null;
        for (var i = 0; i < AutoCandidateOrder.Length; i++)
        {
            var candidateId = AutoCandidateOrder[i];
            var candidateSettings = settings with
            {
                AgentId = candidateId,
                Model = i == 0 ? settings.Model : "",
            };
            var agent = _agentFactory(candidateSettings);
            if (agent is null) continue;

            hadAgent = true;
            try
            {
                var reply = await agent.AskAsync(prompt, conversationId: null, onUpdate: null, ct);
                var text = reply.Text?.Trim() ?? "";
                if (text.Length > 0) return text;
                lastError = new InvalidOperationException("AIから空の要約が返されました。");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (!hadAgent)
            throw new InvalidOperationException("要約に使えるAIが見つかりません。設定画面で要約に使うAIを確認してください。");
        throw new InvalidOperationException($"利用可能なすべてのAIで要約に失敗しました: {lastError?.Message}", lastError);
    }

    /// <summary>設定とインストール済みAIから、要約に使う単発・読み取り専用のエージェントを生成する。</summary>
    /// <param name="appSettings">アプリ設定</param>
    /// <param name="runner">プロセス実行</param>
    /// <param name="installedAis">インストール済みAIの識別名一覧(nullならすべて利用可能とみなす)</param>
    /// <param name="summarySettings">要約設定</param>
    /// <returns>単発実行用のエージェント(使えるものがなければnull)</returns>
    public static IChatAgent? CreateSummaryAgent(
        AppSettings appSettings,
        IProcessRunner runner,
        IReadOnlyCollection<string>? installedAis,
        SummarySettings summarySettings)
    {
        var chosenId = ResolveAgentId(summarySettings.AgentId, installedAis);
        if (chosenId is null) return null;

        var timeout = TimeSpan.FromSeconds(Math.Min(Math.Max(appSettings.TimeoutSec, 60), 180));
        var workDir = string.IsNullOrWhiteSpace(appSettings.WorkDir) ? Environment.CurrentDirectory : appSettings.WorkDir;
        var model = summarySettings.Model?.Trim() ?? "";

        if (chosenId == Participants.ClaudeId)
        {
            // 要約専用: ツール不要・読み取り専用・セッション非永続
            var claude = appSettings.Claude with
            {
                Model = model.Length > 0 ? model : "haiku",
                ExtraArgs = new[] { "--disable-slash-commands", "--strict-mcp-config", "--tools", "" },
                ReadOnlyNote = "",
                Effort = "low",
            };
            return new ClaudeAgent(claude, runner, workDir, timeout) { KeepAlive = false };
        }

        if (chosenId == Participants.GeminiId)
        {
            var agy = appSettings.Agy with
            {
                Model = model.Length > 0 ? model : "gemini-3.8-flash-low",
                ExtraArgs = new[] { "--mode", "plan" },
                NoToolNote = "ツールは使わず会話だけで答えてください。\n\n",
            };
            return new AgyAgent(agy, runner, workDir, timeout) { KeepAlive = false };
        }

        if (chosenId == Participants.ChappyId)
        {
            var codex = (appSettings.Codex ?? new CodexSettings()) with
            {
                Model = model.Length > 0 ? model : null,
                Note = "",
                Effort = "low",
            };
            return new CodexAgent(codex, runner, workDir, timeout);
        }

        return null;
    }

    /// <summary>設定されたAI、または利用可能な最優先AIの識別名を返す。</summary>
    public static string? ResolveAgentId(string? preferredAgentId, IReadOnlyCollection<string>? installedAis)
    {
        bool IsAvailable(string id) => installedAis is null || installedAis.Contains(id);
        var pref = preferredAgentId?.Trim().ToLowerInvariant() ?? "";
        if (pref.Length > 0 && IsAvailable(pref)) return pref;

        // 自動選択の優先順位: Claude(Haiku) → Gemini(Flash) → Chappy(Codex)
        if (IsAvailable(Participants.ClaudeId)) return Participants.ClaudeId;
        if (IsAvailable(Participants.GeminiId)) return Participants.GeminiId;
        if (IsAvailable(Participants.ChappyId)) return Participants.ChappyId;
        return null;
    }

    /// <summary>会議ログを要約させるプロンプトを組み立てる。</summary>
    public static string BuildSummaryPrompt(IReadOnlyList<ChatEntry> entries, string? title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("以下の会議ログを読んで、次の新しい会議へそのまま引き継げる簡潔な要約をMarkdownで作成してください。");
        sb.AppendLine("ツールは使わず、会話ログの内容だけを元に回答してください。");
        sb.AppendLine("以下の見出し構成で整理してください（該当がなければ「なし」と記載）：");
        sb.AppendLine("- ## 議題・背景");
        sb.AppendLine("- ## 決定事項");
        sb.AppendLine("- ## 前提・制約・技術情報");
        sb.AppendLine("- ## 未解決事項・次のアクション");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.AppendLine($"会議タイトル: {title.Trim()}");
            sb.AppendLine();
        }
        sb.AppendLine("=== 会議ログ ===");

        // 長すぎる場合は先頭（背景）と後半（直近の決定・文脈）を残す
        var formatted = entries.Select(e =>
        {
            var who = Participants.Get(e.SpeakerId).LogName;
            return $"[{who}] {e.Text.Trim()}";
        }).ToList();

        var total = formatted.Sum(s => s.Length + 2);
        if (total <= MaxTranscriptCharsForSummary)
        {
            foreach (var line in formatted) sb.AppendLine(line).AppendLine();
        }
        else
        {
            // 先頭数件と末尾を優先して予算内に収める
            var head = new List<string>();
            var tail = new List<string>();
            var budget = MaxTranscriptCharsForSummary - 60;
            var headBudget = budget / 4;
            var usedHead = 0;
            var i = 0;
            for (; i < formatted.Count && usedHead + formatted[i].Length <= headBudget; i++)
            {
                head.Add(formatted[i]);
                usedHead += formatted[i].Length + 2;
            }
            var tailBudget = budget - usedHead;
            var usedTail = 0;
            for (var j = formatted.Count - 1; j >= i && usedTail + formatted[j].Length <= tailBudget; j--)
            {
                tail.Add(formatted[j]);
                usedTail += formatted[j].Length + 2;
            }
            tail.Reverse();
            foreach (var line in head) sb.AppendLine(line).AppendLine();
            if (head.Count + tail.Count < formatted.Count)
                sb.AppendLine("... (中略) ...\n");
            foreach (var line in tail) sb.AppendLine(line).AppendLine();
        }

        return sb.ToString();
    }
}
