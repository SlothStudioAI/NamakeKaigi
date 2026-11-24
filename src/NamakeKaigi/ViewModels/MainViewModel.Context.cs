using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>設定画面の「要約に使うAI」の選択肢。</summary>
/// <param name="Id">AIの識別名(空なら自動)</param>
/// <param name="Label">画面に出す名前</param>
public sealed record SummaryAgentOption(string Id, string Label);

/// <summary>メイン画面の状態とコマンド(partial): コンテキスト計測・アラート・要約引き継ぎ。</summary>
public sealed partial class MainViewModel
{
    private readonly Func<string?, IChatSession>? _summarySessionFactory;
    private readonly MeetingSummaryService? _summaryService;
    private readonly MeetingSummaryBackfillService? _backfillService;
    private readonly Action<SummarySettings>? _onSummarySettingsChanged;
    private SummarySettings _summarySettings = new();
    private ContextMetrics _contextMetrics = ContextMetrics.Empty;
    private bool _isSummarizing;

    /// <summary>「要約して新しい会議へ」コマンド。</summary>
    public ICommand SummarizeAndNewMeetingCommand { get; private set; } = null!;

    /// <summary>要約設定が他の部屋で変わったときに発火するイベント。</summary>
    public event Action<MainViewModel, SummarySettings>? SummaryUpdated;

    /// <summary>入力欄下の1行コンテキスト表示（例: 42発言 · 18,500字 · 62,400 tok）。</summary>
    public string ContextMetricsText => ContextMetricsService.FormatLine(_contextMetrics);

    /// <summary>コンテキスト表示のツールチップ（発言者別内訳・トークン内訳・しきい値）。</summary>
    public string ContextMetricsTooltip => ContextMetricsService.FormatTooltip(_contextMetrics, _summarySettings, _session.Appearance);

    /// <summary>区切り目安のしきい値を超えているか。</summary>
    public bool IsContextAlert => ContextMetricsService.IsOverThreshold(_contextMetrics, _summarySettings);

    /// <summary>「要約して新しい会議へ」ボタンを表示するか（1発言以上あればいつでも押せる かつ 要約機能が有効）。</summary>
    public bool ShowSummarizeButton => (_contextMetrics.MessageCount > 0 || _isSummarizing) && _summarySettings.Enabled;

    /// <summary>要約を作成中ならtrue。</summary>
    public bool IsSummarizing
    {
        get => _isSummarizing;
        private set
        {
            if (!Set(ref _isSummarizing, value)) return;
            Raise(nameof(ShowSummarizeButton));
            Raise(nameof(SummarizeButtonText));
            ((RelayCommand)SummarizeAndNewMeetingCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>要約ボタンの表示文言。</summary>
    public string SummarizeButtonText => IsSummarizing ? "要約を作成中…" : "要約して新しい会議へ";

    /// <summary>いま要約して新しい会議へ移れるか。</summary>
    public bool CanSummarize =>
        _summarySettings.Enabled
        && !IsSummarizing
        && !IsBusy
        && _summaryService is not null
        && _summarySessionFactory is not null
        && _contextMetrics.MessageCount > 0;

    /// <summary>コンテキスト計測を再計算して画面に通知する。</summary>
    public void RefreshContextMetrics()
    {
        _contextMetrics = _session.GetContextMetrics();
        Raise(nameof(ContextMetricsText));
        Raise(nameof(ContextMetricsTooltip));
        Raise(nameof(IsContextAlert));
        Raise(nameof(ShowSummarizeButton));
        ((RelayCommand)SummarizeAndNewMeetingCommand).RaiseCanExecuteChanged();
    }

    /// <summary>現在の会議を軽量モデルで要約し、現在のタブを新しい会議（引き継ぎ付き）へ切り替える。</summary>
    public async Task SummarizeAndNewMeetingAsync()
    {
        if (!CanSummarize) return;
        var entries = _session.GetEntriesSnapshot();
        if (entries.Count == 0) return;

        IsSummarizing = true;
        try
        {
            var currentLevels = CaptureMemberLevels();
            var oldFolder = Path.GetDirectoryName(_session.LogPath);
            var summary = await _summaryService!.SummarizeAsync(entries, _summarySettings, RoomTitle);
            var nextTitle = await NextHandoverRoomTitleAsync(RoomTitle);
            _readAloud.Stop();
            var nextSession = _summarySessionFactory!(summary);
            SwitchSession(nextSession, reopened: false, nextRoomTitle: nextTitle, inheritedLevels: currentLevels);
            MeetingStore.MarkHandedOver(oldFolder);
        }
        catch (Exception ex)
        {
            AddMessage(new MessageViewModel(Participants.Claude, $"要約の作成に失敗しました: {ex.Message}", _clock(), isError: true));
        }
        finally
        {
            IsSummarizing = false;
        }
    }

    /// <summary>現在の会議フォルダの summary.md をバックグラウンドで自動生成・更新する(タブ閉じ・切替時用)。</summary>
    internal void TriggerSummarySave(string? folderPath = null)
    {
        if (_backfillService is null || !_summarySettings.Enabled) return;
        var targetFolder = folderPath ?? Path.GetDirectoryName(_session.LogPath);
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder)) return;
        var settings = _summarySettings;
        _ = Task.Run(() => _backfillService.EnsureSummaryAsync(targetFolder, settings));
    }

    /// <summary>過去の会議の要約(summary.md がなければその場で生成)を引き継いで新しい会議として開く。</summary>
    /// <param name="selected">選ばれた過去の会議</param>
    public async Task OpenPastMeetingWithSummaryAsync(SavedMeetingSummary selected)
    {
        if (_summarySessionFactory is null) return;

        var (summary, state) = await Task.Run(() =>
            (MeetingStore.ReadSummary(selected.FolderPath), MeetingStore.LoadState(selected.FolderPath)));

        if (string.IsNullOrWhiteSpace(summary))
        {
            if (_summaryService is null || !_summarySettings.Enabled)
                throw new InvalidOperationException("要約機能が無効のため、要約を新しく作成できません。");

            var title = string.IsNullOrWhiteSpace(selected.MeetingName) ? null : selected.MeetingName;
            summary = await _summaryService.SummarizeAsync(state.Entries, _summarySettings, title);
            var summaryToSave = summary;
            await Task.Run(() => MeetingStore.SaveSummary(selected.FolderPath, summaryToSave));
        }

        var nextTitle = await NextHandoverRoomTitleAsync(string.IsNullOrWhiteSpace(selected.MeetingName) ? RoomTitle : selected.MeetingName);
        _readAloud.Stop();
        var nextSession = _summarySessionFactory(summary);
        SwitchSession(nextSession, reopened: false, nextRoomTitle: nextTitle, inheritedLevels: state.Levels);
        MeetingStore.MarkHandedOver(selected.FolderPath);
    }

    private Dictionary<string, string> CaptureMemberLevels() =>
        Members.Where(m => m.Participant.Id != Participants.UserId)
            .ToDictionary(m => m.Participant.Id, m => m.SelectedLevelId);

    private async Task<string> NextHandoverRoomTitleAsync(string? sourceTitle)
    {
        var baseSource = string.IsNullOrWhiteSpace(sourceTitle) ? RoomTitle : sourceTitle;
        var existing = new List<string>();
        if (Rooms is not null)
            existing.AddRange(Rooms.Items.Select(r => r.RoomTitle));
        try
        {
            var pastNames = await Task.Run(() => _session.ListPastMeetings().Select(m => m.MeetingName).ToList());
            existing.AddRange(pastNames);
        }
        catch
        {
            // 過去会議一覧が読めない環境では、開いているタブ名のみで重複判定する
        }
        return BuildHandoverRoomTitle(baseSource, _clock(), existing, _roomTitleMaxChars);
    }

    /// <summary>
    /// 引き継ぎ先の新会議用の部屋名を組み立てる（例: 部屋名_20261114、同日重複があれば _2, _3 ...）。
    /// 末尾に既存の引き継ぎ日付サフィックス（_yyyyMMdd または _yyyyMMdd_N）が付いていれば剥がしてから付与する。
    /// </summary>
    public static string BuildHandoverRoomTitle(string? sourceTitle, DateTime today, IEnumerable<string> existingTitles, int maxChars)
    {
        var clean = string.Join(" ", (sourceTitle ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) clean = "会議室";
        var baseName = Regex.Replace(clean, @"_\d{8}(?:_\d+)?$", "");
        if (baseName.Length == 0) baseName = clean;

        var used = new HashSet<string>(
            existingTitles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var datePart = today.ToString("yyyyMMdd");
        var limit = Math.Max(1, maxChars);

        string FormatWithSuffix(string suffix)
        {
            if (limit <= suffix.Length)
                return suffix[^limit..];
            var maxBase = limit - suffix.Length;
            var trimmedBase = baseName.Length > maxBase ? baseName[..maxBase].TrimEnd() : baseName;
            if (trimmedBase.Length == 0) trimmedBase = baseName[..Math.Min(baseName.Length, maxBase)];
            return trimmedBase + suffix;
        }

        var first = FormatWithSuffix($"_{datePart}");
        if (!used.Contains(first)) return first;

        for (var n = 2; ; n++)
        {
            var candidate = FormatWithSuffix($"_{datePart}_{n}");
            if (!used.Contains(candidate)) return candidate;
        }
    }

    // ===== 設定画面用：要約の有効/無効・AI・モデル選択 =====

    /// <summary>会議の要約機能を有効にするか。</summary>
    public bool SummaryEnabled
    {
        get => _summarySettings.Enabled;
        set
        {
            if (_summarySettings.Enabled == value) return;
            UpdateSummarySettings(_summarySettings with { Enabled = value });
        }
    }

    /// <summary>要約に使うAIの選択肢（表示名設定に左右されない固定表記）。</summary>
    public IReadOnlyList<SummaryAgentOption> SummaryAgentOptions
    {
        get
        {
            var list = new List<SummaryAgentOption>
            {
                new("", "自動（軽量モデル優先）"),
            };
            foreach (var m in Members.Where(m => m.Participant.Id != Participants.UserId && m.IsInstalled))
            {
                var label = m.Participant.Id switch
                {
                    Participants.ClaudeId => "Claude",
                    Participants.GeminiId => "Gemini",
                    Participants.ChappyId => "ChatGPT",
                    _ => m.Participant.DisplayName,
                };
                list.Add(new SummaryAgentOption(m.Participant.Id, label));
            }
            return list;
        }
    }

    /// <summary>選ばれている要約用AIの識別名（空なら自動）。</summary>
    public string SelectedSummaryAgentId
    {
        get => _summarySettings.AgentId ?? "";
        set
        {
            if (value is null) return;
            var next = value;
            if (_summarySettings.AgentId == next) return;
            // AIを変えたらモデル指定は自動に戻す
            UpdateSummarySettings(_summarySettings with { AgentId = next, Model = "" });
            Raise(nameof(SummaryModelOptions));
            Raise(nameof(SelectedSummaryModelId));
            Raise(nameof(CanChooseSummaryModel));
        }
    }

    /// <summary>特定のAIが選ばれているときだけモデル選択を有効にする。</summary>
    public bool CanChooseSummaryModel => !string.IsNullOrWhiteSpace(_summarySettings.AgentId);

    /// <summary>選ばれている要約用AIで選択可能なモデル一覧。</summary>
    public IReadOnlyList<ModelOption> SummaryModelOptions
    {
        get
        {
            var list = new List<ModelOption> { new("", "自動（最軽量モデル）") };
            var agentId = _summarySettings.AgentId;
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                try
                {
                    list.AddRange(_session.ModelOptionsFor(agentId));
                }
                catch (KeyNotFoundException)
                {
                    // 該当AIが無効な場合は自動のみ
                }
            }
            var current = _summarySettings.Model ?? "";
            if (current.Length > 0 && list.All(o => o.Id != current))
                list.Add(new ModelOption(current, current));
            return list;
        }
    }

    /// <summary>選ばれている要約用モデルID（空なら自動）。</summary>
    public string SelectedSummaryModelId
    {
        get => _summarySettings.Model ?? "";
        set
        {
            var next = value ?? "";
            if (_summarySettings.Model == next) return;
            UpdateSummarySettings(_summarySettings with { Model = next });
        }
    }

    private void UpdateSummarySettings(SummarySettings next)
    {
        _summarySettings = next;
        _onSummarySettingsChanged?.Invoke(next);
        NotifySummaryPropertiesChanged();
        SummaryUpdated?.Invoke(this, next);
    }

    internal void SyncSummarySettings(SummarySettings next)
    {
        _summarySettings = next;
        NotifySummaryPropertiesChanged();
    }

    private void NotifySummaryPropertiesChanged()
    {
        Raise(nameof(SummaryEnabled));
        Raise(nameof(SelectedSummaryAgentId));
        Raise(nameof(CanChooseSummaryModel));
        Raise(nameof(SummaryModelOptions));
        Raise(nameof(SelectedSummaryModelId));
        Raise(nameof(ContextMetricsTooltip));
        Raise(nameof(IsContextAlert));
        Raise(nameof(ShowSummarizeButton));
        ((RelayCommand)SummarizeAndNewMeetingCommand).RaiseCanExecuteChanged();
    }
}
