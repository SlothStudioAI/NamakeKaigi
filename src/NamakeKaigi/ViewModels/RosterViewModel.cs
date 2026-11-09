using System.Collections.ObjectModel;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>名簿の中のメンバー1人分(画面に出す形)。</summary>
public sealed class RosterMemberViewModel
{
    /// <summary>メンバーを包む。</summary>
    /// <param name="member">定義から読んだメンバー</param>
    public RosterMemberViewModel(RosterMember member) => Member = member;

    /// <summary>定義から読んだメンバー。</summary>
    public RosterMember Member { get; }

    /// <summary>一覧に出す名前(「実装担当 (ツクル)」)。</summary>
    public string DisplayName => Member.DisplayName;
}

/// <summary>名簿の置き場所(チーム)1つ分。グループの下に並ぶ。</summary>
public sealed class RosterTeamViewModel
{
    /// <summary>置き場所を作る。</summary>
    /// <param name="name">置き場所(探す起点からの相対パス。起点の直下なら「共通」)</param>
    /// <param name="members">その置き場所にいるメンバー</param>
    public RosterTeamViewModel(string name, IEnumerable<RosterMember> members)
    {
        Name = name;
        Members = new ObservableCollection<RosterMemberViewModel>(members.Select(m => new RosterMemberViewModel(m)));
    }

    /// <summary>置き場所の名前。</summary>
    public string Name { get; }

    /// <summary>その置き場所にいるメンバー。</summary>
    public ObservableCollection<RosterMemberViewModel> Members { get; }

    /// <summary>一覧に出す見出し(「IT開発 — 10人」)。</summary>
    public string Header => $"{Name} — {Members.Count}人";
}

/// <summary>名簿のグループ1つ分(クロ・ジェミ・チャッピー)。</summary>
public sealed class RosterGroupViewModel
{
    /// <summary>グループを作る。</summary>
    /// <param name="id">グループの識別名</param>
    /// <param name="title">見出し(AIの名前)</param>
    /// <param name="emptyNote">メンバーが0人のときに出す説明</param>
    /// <param name="members">所属するメンバー</param>
    public RosterGroupViewModel(string id, string title, string emptyNote, IEnumerable<RosterMember> members)
    {
        Id = id;
        Title = title;
        EmptyNote = emptyNote;
        var list = members.ToList();
        Members = new ObservableCollection<RosterMemberViewModel>(list.Select(m => new RosterMemberViewModel(m)));
        Teams = new ObservableCollection<RosterTeamViewModel>(
            list.GroupBy(m => m.Team).Select(g => new RosterTeamViewModel(g.Key, g)));
    }

    /// <summary>グループの識別名(claude / gemini / codex)。</summary>
    public string Id { get; }

    /// <summary>見出し(AIの名前)。</summary>
    public string Title { get; }

    /// <summary>メンバーが0人のときに出す説明。</summary>
    public string EmptyNote { get; }

    /// <summary>所属するメンバー(置き場所を分けない、全員の一覧)。</summary>
    public ObservableCollection<RosterMemberViewModel> Members { get; }

    /// <summary>置き場所(チーム)ごとのメンバー。一覧は、この単位で階層にする。</summary>
    public ObservableCollection<RosterTeamViewModel> Teams { get; }

    /// <summary>一覧に出す見出し(「クローディ (Claude) — 40人」)。</summary>
    public string Header => $"{Title} — {Members.Count}人";
}

/// <summary>メンバー名簿の窓の状態。定義ファイルを探して、グループごとに並べ、選んだメンバーの行動ルールを出す。</summary>
public sealed class RosterViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<RosterMember>> _scan;
    private readonly AppearanceSettings _appearance;
    private string _status = "メンバーを探しています…";
    private string _detailTitle = "";
    private string _detailMeta = "";
    private string _detailRules = "名前を選ぶと、ここに行動ルールが出ます。";

    /// <summary>名簿を作る(探すのは <see cref="LoadAsync"/> を呼んだとき)。</summary>
    /// <param name="scan">メンバーを拾い上げる処理(時間がかかるので、別のスレッドで呼ばれる)</param>
    /// <param name="appearance">表示名の設定(省略時は標準。簡易表示なら見出しにキャラ名を出さない)</param>
    public RosterViewModel(Func<IReadOnlyList<RosterMember>> scan, AppearanceSettings? appearance = null)
    {
        _scan = scan;
        _appearance = appearance ?? new AppearanceSettings();
    }

    /// <summary>グループの一覧(クロ・ジェミ・チャッピーの順)。</summary>
    public ObservableCollection<RosterGroupViewModel> Groups { get; } = new();

    /// <summary>上に出す状況(「探しています…」「全62人」)。</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>右に出す、選んだメンバー(またはグループ)の見出し。</summary>
    public string DetailTitle
    {
        get => _detailTitle;
        private set => Set(ref _detailTitle, value);
    }

    /// <summary>右に出す、識別名・チーム・モデル・役割の説明。</summary>
    public string DetailMeta
    {
        get => _detailMeta;
        private set => Set(ref _detailMeta, value);
    }

    /// <summary>右に出す、行動ルール(定義の本文)。</summary>
    public string DetailRules
    {
        get => _detailRules;
        private set => Set(ref _detailRules, value);
    }

    /// <summary>メンバーを探して、グループごとに並べる。</summary>
    /// <returns>探し終わったら完了するタスク</returns>
    public async Task LoadAsync()
    {
        Status = "メンバーを探しています…";
        IReadOnlyList<RosterMember> members;
        try
        {
            members = await Task.Run(_scan);
        }
        catch (Exception ex)
        {
            Status = $"探せませんでした: {ex.Message}";
            return;
        }

        Groups.Clear();
        Groups.Add(new RosterGroupViewModel(RosterScanner.ClaudeGroup, GroupTitle(Participants.ClaudeId, "Claude"),
            "定義が見つかりませんでした(.claude/agents)。", members.Where(m => m.Group == RosterScanner.ClaudeGroup)));
        Groups.Add(new RosterGroupViewModel(RosterScanner.GeminiGroup, GroupTitle(Participants.GeminiId, "Gemini"),
            "定義が見つかりませんでした(.agents/skills)。", members.Where(m => m.Group == RosterScanner.GeminiGroup)));
        Groups.Add(new RosterGroupViewModel(RosterScanner.CodexGroup, GroupTitle(Participants.ChappyId, "Codex"),
            "定義が見つかりませんでした(.codex/agents)。",
            members.Where(m => m.Group == RosterScanner.CodexGroup)));
        Status = $"全{members.Count}人";
    }

    /// <summary>グループの見出しを作る(「クローディ (Claude)」。簡易表示で名前とCLI名が同じなら「Claude」だけ)。</summary>
    /// <param name="participantId">参加者の識別名</param>
    /// <param name="cliName">定義の置き場所になっているCLIの名前</param>
    /// <returns>見出し</returns>
    private string GroupTitle(string participantId, string cliName)
    {
        var name = _appearance.ResolveParticipantName(participantId);
        return name == cliName ? name : $"{name} ({cliName})";
    }

    /// <summary>一覧で選ばれたものに合わせて、右の表示を切り替える。</summary>
    /// <param name="selected">選ばれたもの(メンバー、グループ、またはなし)</param>
    public void Select(object? selected)
    {
        switch (selected)
        {
            case RosterMemberViewModel item:
                var m = item.Member;
                DetailTitle = m.DisplayName;
                DetailMeta = string.Join(Environment.NewLine, new[]
                {
                    $"識別名: {m.Name}",
                    $"チーム: {m.Team}",
                    $"モデル: {m.Model ?? "(定義に指定なし)"}",
                    string.IsNullOrEmpty(m.Description) ? "" : $"役割: {m.Description}",
                    $"定義ファイル: {m.SourcePath}",
                }.Where(s => s.Length > 0));
                DetailRules = m.Rules.Length > 0 ? m.Rules : "(本文がありません)";
                break;
            case RosterTeamViewModel team:
                DetailTitle = team.Header;
                DetailMeta = $"置き場所: {team.Name}";
                DetailRules = "名前を選ぶと、ここに行動ルールが出ます。";
                break;
            case RosterGroupViewModel group:
                DetailTitle = group.Header;
                DetailMeta = group.Members.Count == 0 ? group.EmptyNote : "";
                DetailRules = group.Members.Count == 0 ? "" : "名前を選ぶと、ここに行動ルールが出ます。";
                break;
            default:
                DetailTitle = "";
                DetailMeta = "";
                DetailRules = "名前を選ぶと、ここに行動ルールが出ます。";
                break;
        }
    }
}
