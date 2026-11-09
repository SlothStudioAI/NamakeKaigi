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
    private void SwitchSession(IChatSession nextSession, bool reopened)
    {
        if (!ReferenceEquals(nextSession, _session)) _session.Shutdown(); // 前の会議の、起動したままのCLIを終わらせる
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
        Raise(nameof(WorkDir));
        Raise(nameof(WorkDirDisplay));
        Raise(nameof(WorkDirName));
        ApplyMeetingTitle(reopened);
    }

    /// <summary>
    /// 会議を切り替えたあと、会議の名前とタブの名前をそろえる。
    /// 過去の会議を開き直したなら、会議ログに書いてある名前をタブに戻す(書いてなければ、タブもログも変えない)。
    /// 新しい会議なら、いまのタブの名前を、新しい会議の名前(会議フォルダの名前)にする。
    /// </summary>
    /// <param name="reopened">過去の会議を開き直したならtrue</param>
    private void ApplyMeetingTitle(bool reopened)
    {
        if (reopened)
        {
            if (string.IsNullOrWhiteSpace(_session.MeetingTitle)) return;
            Set(ref _roomTitle, _session.MeetingTitle.Trim(), nameof(RoomTitle)); // 開き直した会議の名前は、書き直さない
        }
        else
        {
            _session.SetMeetingTitle(_roomTitle);
            RaiseLogPathChanged();
        }
    }
}
