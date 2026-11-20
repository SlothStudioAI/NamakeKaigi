using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 新しい会議・過去の会議の開き直し。</summary>
public sealed partial class MainViewModel
{
    /// <summary>新しい会議を始める(画面を空にして、新しいログを作る)。</summary>
    public void StartNewMeeting()
    {
        _readAloud.Stop();
        SwitchSession(_sessionFactory(), reopened: false);
    }

    /// <summary>保存されている過去の会議一覧(新しい順)を取り出す。</summary>
    public IReadOnlyList<SavedMeetingSummary> ListPastMeetings() => _session.ListPastMeetings();

    /// <summary>過去の会議を開き直して、発言と会話IDを復元する。</summary>
    /// <param name="folderPath">会議フォルダのパス</param>
    public void OpenPastMeeting(string folderPath)
    {
        var reopened = _session.OpenPastMeeting(folderPath);
        if (reopened is null) return;
        SwitchSession(reopened, reopened: true);
    }

    /// <summary>過去の会議で参加していたAI(話した人と、宛先になった人。既定の参加者は足さない)。</summary>
    /// <param name="entries">復元した発言</param>
    /// <returns>参加していたAIの識別名</returns>
    private static IReadOnlyList<string> ParticipantsIn(IReadOnlyList<ChatEntry> entries) => entries
        .SelectMany(e => (e.TargetIds ?? Array.Empty<string>()).Append(e.SpeakerId))
        .Where(id => id != Participants.UserId)
        .Distinct()
        .ToList();

    /// <summary>会議を切り替える。</summary>
    /// <param name="nextSession">次の会議</param>
    /// <param name="reopened">過去の会議を開き直したならtrue(参加者はその会議に合わせる)。新しい会議ならfalse(既定の参加者)</param>
    /// <param name="nextRoomTitle">新しい会議に引き継ぐ部屋名(省略時は現在の部屋名を維持)</param>
    /// <param name="inheritedLevels">引き継ぐ各AIの権限段階(省略時は全員「読む」で初期化)</param>
    private void SwitchSession(
        IChatSession nextSession,
        bool reopened,
        string? nextRoomTitle = null,
        IReadOnlyDictionary<string, string>? inheritedLevels = null)
    {
        if (!ReferenceEquals(nextSession, _session))
        {
            var prevSession = _session;
            var prevFolder = System.IO.Path.GetDirectoryName(prevSession.LogPath);
            var backfill = _backfillService;
            var summarySettings = _summarySettings;
            _ = Task.Run(async () =>
            {
                prevSession.Shutdown(); // 前の会議の、起動したままのCLIをバックグラウンドで終わらせる
                if (backfill is not null && summarySettings.Enabled
                    && !string.IsNullOrWhiteSpace(prevFolder) && System.IO.Directory.Exists(prevFolder))
                {
                    await backfill.EnsureSummaryAsync(prevFolder, summarySettings).ConfigureAwait(false);
                }
            });
        }
        _session = nextSession;
        _lists.Clear();
        Attachments.Clear();
        _streaming.Clear();
        _rawStreamingText.Clear();
        foreach (var m in Members)
        {
            m.PendingCount = 0;
            m.IsAnswering = false;
            m.ActivityText = "";
        }
        NotifyBusyStateChanged();
        _selectedAllOrder = _session.AllOrder; // 参加者をそろえる前に読む(先の人が抜けていたら「同時」に戻すため)
        ApplyPresence(reopened ? ParticipantsIn(_session.RestoredEntries) : _defaultParticipants);
        foreach (var entry in _session.RestoredEntries)
        {
            var speaker = Participants.Get(entry.SpeakerId);
            AddMessage(new MessageViewModel(speaker, entry.Text, entry.Time, targetIds: entry.TargetIds));
        }
        ApplyModelLabels();
        ApplyWritePermissions();
        ApplyInheritedLevels(reopened ? _session.RestoredLevels : inheritedLevels);
        ApplyAppearanceToAll();
        _ = RefreshModelOptionsAsync();
        _selectedThemeId = ThemeCatalog.GetOrDefault(_session.ThemeId).Id;
        Raise(nameof(SelectedThemeId));
        _selectedTimeoutSec = _session.TimeoutSec;
        Raise(nameof(TimeoutOptions));
        Raise(nameof(SelectedTimeoutSec));
        Raise(nameof(SelectedAllOrder));
        Raise(nameof(AllOrderHint));
        Raise(nameof(LogPath));
        Raise(nameof(LogPathDisplay));
        RaiseWorkDirChanged();
        ApplyMeetingTitle(reopened, nextRoomTitle);
        RefreshContextMetrics();
    }

    /// <summary>
    /// 会議を切り替えたあと、会議の名前とタブの名前をそろえる。
    /// 過去の会議を開き直したなら、会議ログに書いてある名前をタブに戻す(書いてなければ、タブもログも変えない)。
    /// 新しい会議なら、引き継ぎ名(指定時)またはいまのタブの名前を、新しい会議の名前(会議フォルダの名前)にする。
    /// </summary>
    /// <param name="reopened">過去の会議を開き直したならtrue</param>
    /// <param name="nextRoomTitle">新しい会議に引き継ぐ部屋名(省略時は現在の部屋名を維持)</param>
    private void ApplyMeetingTitle(bool reopened, string? nextRoomTitle = null)
    {
        if (reopened)
        {
            if (string.IsNullOrWhiteSpace(_session.MeetingTitle)) return;
            Set(ref _roomTitle, _session.MeetingTitle.Trim(), nameof(RoomTitle)); // 開き直した会議の名前は、書き直さない
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(nextRoomTitle))
                Set(ref _roomTitle, nextRoomTitle.Trim(), nameof(RoomTitle));
            _session.SetMeetingTitle(_roomTitle);
            RaiseLogPathChanged();
        }
    }

    private void ApplyInheritedLevels(IReadOnlyDictionary<string, string>? inheritedLevels)
    {
        if (inheritedLevels is null || inheritedLevels.Count == 0) return;
        foreach (var m in Members.Where(m => m.Participant.Id != Participants.UserId))
        {
            if (!inheritedLevels.TryGetValue(m.Participant.Id, out var levelId)) continue;
            if (string.IsNullOrWhiteSpace(levelId) || levelId == PermissionLevelOption.ReadId) continue;
            if (!m.CanWrite) continue;
            m.SelectedLevelId = levelId == PermissionLevelOption.FullId && !m.CanFull
                ? PermissionLevelOption.PartialId
                : levelId;
        }
    }
}
