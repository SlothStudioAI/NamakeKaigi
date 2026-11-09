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

    private readonly IReadOnlyList<string> _defaultParticipants;

    // 参加の状態(宛先のボタンの出し分けに使う)
    public bool ClaudePresent => ClaudeMember.IsPresent;

    public bool GeminiPresent => GeminiMember.IsPresent;

    public bool ChappyPresent => ChappyMember.IsPresent;

    /// <summary>「全員」のボタンを出すか(AIが2人以上いるとき。1on1では、相手1人がそのまま宛先になる)。</summary>
    public bool ShowEveryoneTarget => PresentAiIds().Count >= 2;

    /// <summary>順番の選択欄を出すか(宛先が「全員」で、AIが2人以上いるとき。チャッピーを含む2人でも出す)。</summary>
    public bool ShowAllOrderChoice => TargetEveryone && PresentAiIds().Count >= 2;

    /// <summary>参加するAIの識別名を、知っているものだけにそろえる(空・知らない名前だけなら、AI全員)。</summary>
    /// <param name="ids">設定などで指定された識別名</param>
    /// <returns>そろえた識別名(画面の並び順)</returns>
    private static IReadOnlyList<string> NormalizeParticipants(IEnumerable<string>? ids)
    {
        var known = Participants.All.Where(p => p.Id != Participants.UserId).Select(p => p.Id).ToList();
        var wanted = ids?.Select(x => x?.Trim() ?? "").ToList() ?? new List<string>();
        var chosen = known.Where(id => wanted.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        return chosen.Count > 0 ? chosen : known;
    }

    /// <summary>いま会議に参加しているAIの識別名(画面の並び順)。</summary>
    private List<string> PresentAiIds() =>
        Members.Where(m => m.CanChoosePresence && m.IsPresent).Select(m => m.Participant.Id).ToList();

    /// <summary>その識別名の人が、会議に参加しているか(ユーザー本人・知らない名前はtrue)。</summary>
    private bool IsPresent(string id) => Members.FirstOrDefault(m => m.Participant.Id == id)?.IsPresent ?? true;

    /// <summary>参加者を一度にそろえる(新しい会議の始め・過去の会議の開き直し)。会議への知らせは出さない。</summary>
    /// <param name="ids">参加するAIの識別名</param>
    private void ApplyPresence(IEnumerable<string> ids)
    {
        var installed = Members.Where(m => m.CanChoosePresence && m.IsInstalled).Select(m => m.Participant.Id).ToList();
        var present = NormalizeParticipants(ids).Where(installed.Contains).ToList();
        if (present.Count == 0) present = installed; // 選ばれていた人が全員入っていなければ、入っている人で始める
        foreach (var m in Members.Where(m => m.CanChoosePresence)) m.SetPresentSilently(present.Contains(m.Participant.Id));
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
        NormalizeTarget();
        foreach (var message in Messages) message.ApplyPresence(IsPresent);
        RefreshPresenceLock();
        RefreshAllOrderChoice();
        Raise(nameof(ClaudePresent));
        Raise(nameof(GeminiPresent));
        Raise(nameof(ChappyPresent));
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
    }

    /// <summary>宛先の選択肢に対応する識別名(「全員」は空)。</summary>
    private static string ToId(ChatTarget target) => target switch
    {
        ChatTarget.Claude => Participants.ClaudeId,
        ChatTarget.Gemini => Participants.GeminiId,
        ChatTarget.Chappy => Participants.ChappyId,
        _ => "",
    };

    /// <summary>識別名に対応する宛先の選択肢(知らない名前は「全員」)。</summary>
    private static ChatTarget ToChatTarget(string id) => id switch
    {
        Participants.ClaudeId => ChatTarget.Claude,
        Participants.GeminiId => ChatTarget.Gemini,
        Participants.ChappyId => ChatTarget.Chappy,
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
            Raise(nameof(TargetEveryone));
            Raise(nameof(ShowAllOrderChoice));
        }
    }

    // ラジオボタン(宛先の丸いボタン)と結びつける3つの真偽値
    public bool TargetClaude { get => Target == ChatTarget.Claude; set { if (value) Target = ChatTarget.Claude; } }

    public bool TargetGemini { get => Target == ChatTarget.Gemini; set { if (value) Target = ChatTarget.Gemini; } }

    public bool TargetChappy { get => Target == ChatTarget.Chappy; set { if (value) Target = ChatTarget.Chappy; } }

    public bool TargetEveryone { get => Target == ChatTarget.Everyone; set { if (value) Target = ChatTarget.Everyone; } }

    /// <summary>宛先の識別名の並びにする。参加していない人は除く(全員が除かれたら、参加している全員に送る)。</summary>
    private IReadOnlyList<string> TargetIds()
    {
        var ids = Target switch
        {
            ChatTarget.Claude => new[] { Participants.ClaudeId },
            ChatTarget.Gemini => new[] { Participants.GeminiId },
            ChatTarget.Chappy => new[] { Participants.ChappyId },
            // 全員: 同時でも「〇〇が先」でも、参加している全員(チャッピーも入れる)
            _ => new[] { Participants.ClaudeId, Participants.GeminiId, Participants.ChappyId },
        };
        var present = ids.Where(IsPresent).ToList();
        return present.Count > 0 ? present : PresentAiIds();
    }
}
