using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 参加者の出欠と、宛先・順番の選択。</summary>
public sealed partial class MainViewModel
{
    private ChatTarget _target = ChatTarget.Everyone;

    private string _selectedAllOrder = AllOrders.Parallel;

    private IReadOnlyList<string> _defaultParticipants;
    private readonly Action<IReadOnlyList<string>>? _onDefaultParticipantsChanged;

    /// <summary>1つの会議室に同時に参加できるAIの上限人数。</summary>
    public const int MaxMeetingAis = 6;

    /// <summary>左サイドバーに実際に表示するメンバー一覧(IsInRoom が true のもの)。</summary>
    public ObservableCollection<MemberViewModel> SidebarMembers { get; } = new();

    private void SyncSidebarMembers()
    {
        var desired = Members.Where(m => m.Participant.Id != Participants.UserId && m.IsInstalled && m.IsInRoom).ToList();
        if (SidebarMembers.Count == desired.Count && SidebarMembers.SequenceEqual(desired)) return;
        SidebarMembers.Clear();
        foreach (var m in desired) SidebarMembers.Add(m);
    }

    // 参加の状態(宛先のボタンの出し分けに使う)
    public bool ClaudePresent => ClaudeMember.IsInRoom && ClaudeMember.IsPresent;

    public bool GeminiPresent => GeminiMember.IsInRoom && GeminiMember.IsPresent;

    public bool ChappyPresent => ChappyMember.IsInRoom && ChappyMember.IsPresent;

    public bool AgyClaudePresent => AgyClaudeMember.IsInRoom && AgyClaudeMember.IsPresent;

    public bool AgyChappyPresent => AgyChappyMember.IsInRoom && AgyChappyMember.IsPresent;

    /// <summary>「全員」のボタンを出すか(AIが2人以上いるとき。1on1では、相手1人がそのまま宛先になる)。</summary>
    public bool ShowEveryoneTarget => PresentAiIds().Count >= 2;

    /// <summary>順番の選択欄を出すか(宛先が「全員」で、AIが2人以上いるとき。チャッピーを含む2人でも出す)。</summary>
    public bool ShowAllOrderChoice => TargetEveryone && PresentAiIds().Count >= 2;

    /// <summary>参加するAIの識別名を、知っているものだけにそろえる(空・知らない名前だけなら、標準AI全員)。</summary>
    /// <param name="ids">設定などで指定された識別名</param>
    /// <param name="allowAgyProxies">agy経由メンバーも含めるならtrue</param>
    /// <returns>そろえた識別名(画面の並び順)</returns>
    private static IReadOnlyList<string> NormalizeParticipants(IEnumerable<string>? ids, bool allowAgyProxies = false)
    {
        var pool = allowAgyProxies ? Participants.All : Participants.StandardAis;
        var known = pool.Where(p => p.Id != Participants.UserId).Select(p => p.Id).ToList();
        var wanted = ids?.Select(x => x?.Trim() ?? "").ToList() ?? new List<string>();
        var chosen = known.Where(id => wanted.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (chosen.Count > 0) return chosen;
        return Participants.StandardAis.Select(p => p.Id).ToList();
    }

    /// <summary>いま会議に参加しているAIの識別名(画面の並び順)。</summary>
    private List<string> PresentAiIds() =>
        Members.Where(m => m.CanChoosePresence && m.IsInRoom && m.IsPresent).Select(m => m.Participant.Id).ToList();

    /// <summary>その識別名の人が、会議に参加しているか(ユーザー本人・知らない名前はtrue)。</summary>
    private bool IsPresent(string id) =>
        Members.FirstOrDefault(m => m.Participant.Id == id) is { } m ? m.IsInRoom && m.IsPresent : true;

    /// <summary>この会議にまだ追加されておらず、左の「＋ メンバー追加」から選べるメンバー。</summary>
    public IReadOnlyList<MemberViewModel> AddableMembers =>
        Members.Where(m => m.CanChoosePresence && m.IsInstalled && !m.IsInRoom).ToList();

    /// <summary>「＋ メンバー追加」ボタンを有効にするか(会議開始前かつ追加候補があり、上限未満のとき)。</summary>
    public bool CanAddMember => Messages.Count == 0 && AddableMembers.Count > 0 && PresentAiIds().Count < MaxMeetingAis;

    /// <summary>「＋ メンバー追加」から選ばれたメンバーを現在の会議室に加える。</summary>
    /// <param name="id">追加するAIの識別名</param>
    public void AddMemberToRoom(string id)
    {
        if (Messages.Count > 0 || PresentAiIds().Count >= MaxMeetingAis) return;
        var member = Members.FirstOrDefault(m => m.Participant.Id == id);
        if (member is null || !member.IsInstalled || member.IsInRoom) return;
        member.IsInRoom = true;
        member.SetPresentSilently(true);
        AfterPresenceChanged();
    }

    // 設定画面「初期メンバー」用プロパティ(標準AIのみ対象。最低1人は残す)
    public bool DefaultClaudeInstalled => ClaudeMember.IsInstalled;
    public bool DefaultGeminiInstalled => GeminiMember.IsInstalled;
    public bool DefaultChappyInstalled => ChappyMember.IsInstalled;

    public bool DefaultClaude
    {
        get => _defaultParticipants.Contains(Participants.ClaudeId);
        set => SetDefaultParticipant(Participants.ClaudeId, value, nameof(DefaultClaude));
    }

    public bool DefaultGemini
    {
        get => _defaultParticipants.Contains(Participants.GeminiId);
        set => SetDefaultParticipant(Participants.GeminiId, value, nameof(DefaultGemini));
    }

    public bool DefaultChappy
    {
        get => _defaultParticipants.Contains(Participants.ChappyId);
        set => SetDefaultParticipant(Participants.ChappyId, value, nameof(DefaultChappy));
    }

    private void SetDefaultParticipant(string id, bool enabled, string propertyName)
    {
        var current = _defaultParticipants.ToList();
        if (enabled)
        {
            if (current.Contains(id)) return;
            current.Add(id);
        }
        else
        {
            if (!current.Contains(id)) return;
            if (current.Count <= 1)
            {
                // 最後の1人は外せない(チェックを元に戻す通知だけ飛ばす)
                Raise(propertyName);
                return;
            }
            current.Remove(id);
        }
        _defaultParticipants = NormalizeParticipants(current, allowAgyProxies: false);
        Raise(nameof(DefaultClaude));
        Raise(nameof(DefaultGemini));
        Raise(nameof(DefaultChappy));
        _onDefaultParticipantsChanged?.Invoke(_defaultParticipants);
        if (Messages.Count == 0) ApplyPresence(_defaultParticipants);
    }

    /// <summary>参加者を一度にそろえる(新しい会議の始め・過去の会議の開き直し)。会議への知らせは出さない。</summary>
    /// <param name="ids">参加するAIの識別名</param>
    private void ApplyPresence(IEnumerable<string> ids)
    {
        var installed = Members.Where(m => m.CanChoosePresence && m.IsInstalled).Select(m => m.Participant.Id).ToList();
        var present = NormalizeParticipants(ids, allowAgyProxies: true).Where(installed.Contains).ToList();
        if (present.Count == 0)
        {
            present = Participants.StandardAis.Select(p => p.Id).Where(installed.Contains).ToList();
            if (present.Count == 0) present = installed;
        }
        foreach (var m in Members.Where(m => m.CanChoosePresence))
        {
            var inRoom = present.Contains(m.Participant.Id);
            m.IsInRoom = inRoom;
            m.SetPresentSilently(inRoom);
        }
        AfterPresenceChanged();
    }

    /// <summary>画面で「参加する」が変えられたとき。最後の1人と、会議が始まったあとは外せない(元に戻す)。</summary>
    private void OnPresenceChosen(string id, bool present)
    {
        if (!present && (PresentAiIds().Count == 0 || Messages.Count > 0))
        {
            Member(id).SetPresentSilently(true);
            return;
        }
        if (present && Messages.Count > 0)
        {
            Member(id).SetPresentSilently(false);
            return;
        }
        AfterPresenceChanged();
    }

    /// <summary>参加者が変わったあとの後始末(宛先・意見を聞く相手・順番の選択肢・ボタンの出し分け・説明文)。</summary>
    private void AfterPresenceChanged()
    {
        SyncSidebarMembers();
        NormalizeTarget();
        foreach (var message in Messages) message.ApplyPresence(IsPresent);
        RefreshPresenceLock();
        RefreshAllOrderChoice();
        Raise(nameof(ClaudePresent));
        Raise(nameof(GeminiPresent));
        Raise(nameof(ChappyPresent));
        Raise(nameof(AgyClaudePresent));
        Raise(nameof(AgyChappyPresent));
        Raise(nameof(ShowEveryoneTarget));
        Raise(nameof(ShowAllOrderChoice));
    }

    /// <summary>
    /// 順番の選択肢を作り直し、選択欄に今の値を出し直す(作り直すと選択欄が空白になるため)。
    /// 先に答える人が参加していなければ、「同時」に戻して settings.json にも保存する。
    /// </summary>
    private void RefreshAllOrderChoice()
    {
        Raise(nameof(AllOrderOptions));
        var firstId = AllOrders.FirstOf(_selectedAllOrder);
        if (firstId != "" && !IsPresent(firstId)) SelectedAllOrder = AllOrders.Parallel;
        Raise(nameof(SelectedAllOrder));
        Raise(nameof(AllOrderHint));
    }

    /// <summary>宛先を、参加している人に合わせる(AIが1人なら、その人。宛先の人が抜けたなら、「全員」)。</summary>
    private void NormalizeTarget()
    {
        var present = PresentAiIds();
        if (present.Count == 1) Target = ToChatTarget(present[0]);
        else if (Target != ChatTarget.Everyone && !IsPresent(ToId(Target))) Target = ChatTarget.Everyone;
    }

    /// <summary>「参加する」の欄を、変えてよいか(会議が始まったあと・最後の1人は変えられない)と、その説明に合わせる。</summary>
    private void RefreshPresenceLock()
    {
        var started = Messages.Count > 0;
        var presentCount = PresentAiIds().Count;
        foreach (var m in Members.Where(m => m.CanChoosePresence))
        {
            var last = m.IsPresent && presentCount <= 1;
            m.CanChangePresence = !started && !last;
            m.PresenceTooltip = started ? "会議が始まったので、参加者は変えられません。「＋ 新しい会議」で選び直せます"
                : last ? "会議には、AIが最低1人は必要です(最後の1人は外せません)"
                : MemberViewModel.DefaultPresenceTooltip;
        }
        Raise(nameof(AddableMembers));
        Raise(nameof(CanAddMember));
    }

    /// <summary>宛先の選択肢に対応する識別名(「全員」は空)。</summary>
    private static string ToId(ChatTarget target) => target switch
    {
        ChatTarget.Claude => Participants.ClaudeId,
        ChatTarget.Gemini => Participants.GeminiId,
        ChatTarget.Chappy => Participants.ChappyId,
        ChatTarget.AgyClaude => Participants.AgyClaudeId,
        ChatTarget.AgyChappy => Participants.AgyChappyId,
        _ => "",
    };

    /// <summary>識別名に対応する宛先の選択肢(知らない名前は「全員」)。</summary>
    private static ChatTarget ToChatTarget(string id) => id switch
    {
        Participants.ClaudeId => ChatTarget.Claude,
        Participants.GeminiId => ChatTarget.Gemini,
        Participants.ChappyId => ChatTarget.Chappy,
        Participants.AgyClaudeId => ChatTarget.AgyClaude,
        Participants.AgyChappyId => ChatTarget.AgyChappy,
        _ => ChatTarget.Everyone,
    };

    /// <summary>
    /// 「全員」宛ての答える順番の選択肢(名前は、いまの表示名で出す)。「同時」と、参加している人ごとの「〇〇が先」。
    /// 〇〇が先: 〇〇が答えてから、残りの人がその答えを読んで、同時にそれぞれの意見を言う(会議風)。
    /// </summary>
    public IReadOnlyList<AllOrderOption> AllOrderOptions =>
        new[] { new AllOrderOption(AllOrders.Parallel, "同時",
                "全員が同時に答えます。互いの今回の答えは見ないので、独立した意見になります") }
            .Concat(PresentAiIds().Select(id => new AllOrderOption(AllOrders.OrderFor(id), $"{Member(id).DisplayName}が先",
                $"{Member(id).DisplayName}が先に答え、残りの人がその答えを読んで、同時にそれぞれの意見を言います(1往復で止まります)")))
            .ToList();

    /// <summary>いま選ばれている「全員」宛ての答える順番。選ぶと次の発言から効き、settings.json にも保存される。</summary>
    public string SelectedAllOrder
    {
        get => _selectedAllOrder;
        set
        {
            // 選択肢を作り直したときに、選択欄から空が届くことがあるので、空は無視する
            if (string.IsNullOrEmpty(value)) return;
            if (!Set(ref _selectedAllOrder, AllOrders.Normalize(value))) return;
            _session.SetAllOrder(_selectedAllOrder);
            Raise(nameof(AllOrderHint));
        }
    }

    /// <summary>
    /// いまの順番と段階で、何が起きるかの一言(宛先「全員」の横に出す)。
    /// あとに答える人が2人なら、「同時」と同じ形の一言(先の人の答えを読んで、残りの2人が同時に答える)。
    /// 1人なら、1人目が書けて2人目が読むだけなら「作ってから確認」、逆なら「考えてから作る」、
    /// 2人とも読むだけなら「案と意見」、2人とも書けるなら「作業のリレー」。
    /// </summary>
    public string AllOrderHint
    {
        get
        {
            var present = PresentAiIds();
            // AIが0人のとき、名前が空のまま「が同時に〜」と出ないようにする
            if (present.Count == 0) return "参加しているAIがいません。左の「この会議に参加する」にチェックを入れてください";
            if (present.Count == 1) return $"{Member(present[0]).DisplayName}だけが参加しています(1on1)";
            var names = string.Join("・", present.Select(id => Member(id).DisplayName));
            if (_selectedAllOrder == AllOrders.Parallel)
            {
                return $"{names}が同時に、別々に答えます(互いの今回の答えは見ない＝独立した意見)";
            }
            var firstId = AllOrders.FirstOf(_selectedAllOrder);
            if (!present.Contains(firstId))
            {
                return $"{Member(firstId).DisplayName}が参加していないので、{names}が同時に答えます";
            }
            var ordered = AllOrders.Arrange(_selectedAllOrder, present);
            var first = Member(ordered[0]);
            var f = $"{first.DisplayName}({first.LevelLabel})";
            if (ordered.Count > 2)
            {
                var rest = string.Join("・", ordered.Skip(1).Select(id => Member(id).DisplayName));
                return $"{first.DisplayName}が先に答え、{rest}がそれを読んで、同時に答えます(あとの人は作業はせず、意見だけ)";
            }
            var second = Member(ordered[1]);
            var s = $"{second.DisplayName}({second.LevelLabel})";
            return (first.IsWriter, second.IsWriter) switch
            {
                (true, false) => $"▶ 作ってから確認: {f}が作業し → {s}がその結果を見て、意見を言います",
                (false, true) => $"▶ 考えてから作る: {f}が調べて案を出し → {s}がそれに沿って作業します",
                (false, false) => $"▶ 案と意見: {f}が答え → {s}がそれを読んで、どう思うかを言います",
                _ => $"▶ 作業のリレー: {f}が作業し → {s}が続きの作業をします(同じファイルは触らず、コミットは{first.DisplayName}だけ)",
            };
        }
    }

    /// <summary>選ばれている宛先。</summary>
    public ChatTarget Target
    {
        get => _target;
        set
        {
            if (!Set(ref _target, value)) return;
            Raise(nameof(TargetClaude));
            Raise(nameof(TargetGemini));
            Raise(nameof(TargetChappy));
            Raise(nameof(TargetAgyClaude));
            Raise(nameof(TargetAgyChappy));
            Raise(nameof(TargetEveryone));
            Raise(nameof(ShowAllOrderChoice));
        }
    }

    // ラジオボタン(宛先の丸いボタン)と結びつける真偽値
    public bool TargetClaude { get => Target == ChatTarget.Claude; set { if (value) Target = ChatTarget.Claude; } }

    public bool TargetGemini { get => Target == ChatTarget.Gemini; set { if (value) Target = ChatTarget.Gemini; } }

    public bool TargetChappy { get => Target == ChatTarget.Chappy; set { if (value) Target = ChatTarget.Chappy; } }

    public bool TargetAgyClaude { get => Target == ChatTarget.AgyClaude; set { if (value) Target = ChatTarget.AgyClaude; } }

    public bool TargetAgyChappy { get => Target == ChatTarget.AgyChappy; set { if (value) Target = ChatTarget.AgyChappy; } }

    public bool TargetEveryone { get => Target == ChatTarget.Everyone; set { if (value) Target = ChatTarget.Everyone; } }

    /// <summary>宛先の識別名の並びにする。参加していない人は除く(全員が除かれたら、参加している全員に送る)。</summary>
    private IReadOnlyList<string> TargetIds()
    {
        if (Target == ChatTarget.Everyone) return PresentAiIds();
        var id = ToId(Target);
        if (id.Length > 0 && IsPresent(id)) return new[] { id };
        return PresentAiIds();
    }
}
