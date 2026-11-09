using System.IO;
using System.Windows;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>メンバー名簿(定義ファイルの拾い上げ・一覧・窓)の試験。</summary>
public class RosterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "namake_roster_" + Guid.NewGuid().ToString("N"));

    public RosterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Put(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private const string ClaudeDef = "---\nname: software-engineer\ndisplayName: 実装担当 (ツクル)\ndescription: \"設計に沿って実装する\"\nmodel: sonnet\ntools:\n  - Read\n  - Edit\n---\n\nあなたは実装エンジニアです。\n規約を守ってください。\n";
    private const string GeminiDef = "---\nname: script-reviewer\ndescription: >-\n  台本案の弱点を指摘し、\n  一本に統合\ndisplayName: 査読担当 (タタキ)\n---\n\n# 査読担当 (タタキ)\n\n査読します。\n";

    [Fact]
    public void 定義の見出しと本文を読み取る()
    {
        var m = RosterScanner.ParseDefinition(ClaudeDef, "claude", "IT開発", "p.md", "fallback");

        Assert.Equal("software-engineer", m.Name);
        Assert.Equal("実装担当 (ツクル)", m.DisplayName);
        Assert.Equal("設計に沿って実装する", m.Description);
        Assert.Equal("sonnet", m.Model);
        Assert.Equal("あなたは実装エンジニアです。\n規約を守ってください。", m.Rules);
    }

    [Fact]
    public void 説明が複数行の書き方でも_1つの文章につなぐ()
    {
        var m = RosterScanner.ParseDefinition(GeminiDef, "gemini", "youtube_project", "p.md", "fallback");

        Assert.Equal("台本案の弱点を指摘し、 一本に統合", m.Description);
        Assert.Equal("査読担当 (タタキ)", m.DisplayName);
        Assert.Null(m.Model);
        Assert.StartsWith("# 査読担当 (タタキ)", m.Rules);
    }

    [Fact]
    public void 見出しがない定義は_ファイル名を識別名にして_全文を行動ルールにする()
    {
        var m = RosterScanner.ParseDefinition("ただの文章です\n", "claude", "共通", "p.md", "only-body");

        Assert.Equal("only-body", m.Name);
        Assert.Equal("only-body", m.DisplayName); // 表示名がなければ識別名
        Assert.Equal("ただの文章です", m.Rules);
    }

    [Fact]
    public void クロとジェミの置き場所から拾い_ゴミ箱と_archiveは拾わない()
    {
        Put(".claude/agents/pm-progress.md", "---\nname: pm-progress\ndisplayName: 進行管理担当 (ダンドリ)\n---\n本文");
        Put("IT開発/.claude/agents/software-engineer.md", ClaudeDef);
        Put("IT開発/.claude/agents/_archive/old.md", "---\nname: old\n---\n");
        Put("youtube_project/.agents/skills/script-reviewer/SKILL.md", GeminiDef);
        Put("_ゴミ箱/20261013/マーケ/.claude/agents/copywriter.md", "---\nname: copywriter\n---\n");

        var members = RosterScanner.Scan(new[] { _root }, new RosterSettings());

        Assert.Equal(new[] { "software-engineer", "pm-progress" },
            members.Where(m => m.Group == "claude").Select(m => m.Name));
        Assert.Equal(new[] { "IT開発", "共通" }, members.Where(m => m.Group == "claude").Select(m => m.Team));
        Assert.Equal(new[] { "script-reviewer" }, members.Where(m => m.Group == "gemini").Select(m => m.Name));
        Assert.DoesNotContain(members, m => m.Name is "copywriter" or "old");
    }

    [Fact]
    public void 同じ識別名のコピーは_いちばん浅い場所の1件にまとめる()
    {
        Put("IT開発/.claude/agents/software-engineer.md", ClaudeDef);
        Put("IT開発/projects/公開用/App/.claude/agents/software-engineer.md", ClaudeDef);

        var members = RosterScanner.Scan(new[] { _root }, new RosterSettings());

        var only = Assert.Single(members);
        Assert.Equal("IT開発", only.Team);
    }

    [Fact]
    public void 探す深さの上限より深い所は_拾わない()
    {
        Put("a/b/c/.claude/agents/deep.md", "---\nname: deep\n---\n");

        Assert.Empty(RosterScanner.Scan(new[] { _root }, new RosterSettings(MaxDepth: 2)));
        Assert.Single(RosterScanner.Scan(new[] { _root }, new RosterSettings(MaxDepth: 4)));
    }

    [Fact]
    public void 起点が設定になければ_作業フォルダから探す()
    {
        Put("T/.claude/agents/x.md", "---\nname: x\n---\n");

        var members = RosterScanner.Scan(new RosterSettings(), _root);

        Assert.Single(members);
    }

    // 本物の .codex/agents/*.toml と同じ形(Claude の定義から取り込まれたもの。本文の改行が \r\ で残っている)
    private const string CodexDef =
        "name = \"sounding-board\"\n" +
        "description = \"壁打ち相手。話を言い直して1つだけ質問する\"\n" +
        "developer_instructions = \"\"\"\n" +
        "あなたはサモエド犬の「ホッコリ」です。\\r\n" +
        "\\r\n" +
        "【やること】\\r\n" +
        "- 言い直す（\"つまり\"）\"\"\"\n";

    [Fact]
    public void チャッピーの定義_TOMLの見出しと行動ルールを読み取り_表示名は識別名にする()
    {
        var m = RosterScanner.ParseCodexDefinition(CodexDef, "共通", @"C:\x\.codex\agents\sounding-board.toml", "fallback");

        Assert.Equal("codex", m.Group);
        Assert.Equal("sounding-board", m.Name);
        Assert.Equal("sounding-board", m.DisplayName);
        Assert.Equal("壁打ち相手。話を言い直して1つだけ質問する", m.Description);
        Assert.Null(m.Model);
        Assert.Equal("あなたはサモエド犬の「ホッコリ」です。\n\n【やること】\n- 言い直す（\"つまり\"）", m.Rules);
    }

    [Fact]
    public void TOMLの読み取り_4つの書き方と_表より後ろは読まない()
    {
        var fields = TomlStrings.Read(
            "# コメント\n" +
            "a = \"x\\ty\\u3042\"\n" +
            "b = 'C:\\path'\n" +
            "c = '''\n生の\\n文字'''\n" +
            "d = \"\"\"\n1行目 \\\n    続き\"\"\"\n" +
            "n = 3\n" +
            "e = \"閉じていない\n" +
            "f = \"次の行も読む\"\n" +
            "[table]\n" +
            "g = \"読まない\"\n");

        Assert.Equal("x\tyあ", fields["a"]);
        Assert.Equal("C:\\path", fields["b"]);
        Assert.Equal("生の\\n文字", fields["c"]);
        Assert.Equal("1行目 続き", fields["d"]);
        Assert.False(fields.ContainsKey("n"));
        Assert.Equal("次の行も読む", fields["f"]);
        Assert.False(fields.ContainsKey("g"));
    }

    [Fact]
    public void チャッピーの置き場所から拾い_置き場所の階層もつく()
    {
        Put(".codex/agents/sounding-board.toml", CodexDef);
        Put("IT開発/.codex/agents/code-reviewer.toml", "name = \"code-reviewer\"\ndeveloper_instructions = \"査読する\"\n");
        Put("IT開発/.codex/agents/_archive/old.toml", "name = \"old\"\n");
        Put("IT開発/.codex/config.toml", "model = \"x\"\n");

        var members = RosterScanner.Scan(new[] { _root }, new RosterSettings());

        Assert.Equal(new[] { ("IT開発", "code-reviewer"), ("共通", "sounding-board") },
            members.Where(m => m.Group == "codex").Select(m => (m.Team, m.Name)));
    }

    [Fact]
    public void 個人用の3か所も拾い_作業フォルダに同じ識別名があれば作業フォルダを出す()
    {
        var work = Path.Combine(_root, "work");
        var home = Path.Combine(_root, "home");
        Put("work/.claude/agents/same.md", "---\nname: same\ndisplayName: 作業フォルダの\n---\n");
        Put("home/.claude/agents/same.md", "---\nname: same\ndisplayName: 個人用の\n---\n");
        Put("home/.claude/agents/mine.md", "---\nname: mine\n---\n");
        Put("home/.agents/skills/gemi/SKILL.md", "---\nname: gemi\n---\n");
        Put("home/.codex/agents/chappy.toml", "name = \"chappy\"\n");
        Put("home/深い/.claude/agents/deep.md", "---\nname: deep\n---\n"); // ホームの下は、決まった場所しか見ない

        var members = RosterScanner.Scan(new RosterSettings(), work, home);

        Assert.Equal("作業フォルダの", members.Single(m => m.Name == "same").DisplayName);
        Assert.Equal(new[] { "mine", "gemi", "chappy" }.Order(),
            members.Where(m => m.Team == RosterScanner.UserTeamName).Select(m => m.Name).Order());
        Assert.DoesNotContain(members, m => m.Name == "deep");
    }

    [Fact]
    public void 個人用を探さない設定なら_ホームは見ない()
    {
        Put("home/.claude/agents/mine.md", "---\nname: mine\n---\n");

        var members = RosterScanner.Scan(new RosterSettings(IncludeUserDirs: false),
            Path.Combine(_root, "work"), Path.Combine(_root, "home"));

        Assert.Empty(members);
    }

    [Fact]
    public async Task 名簿は_クロ_ジェミ_チャッピーの順に並び_0人のグループは説明が出る()
    {
        Put("T/.claude/agents/x.md", "---\nname: x\ndisplayName: エックス\n---\n本文X");
        Put("T/.agents/skills/y/SKILL.md", "---\nname: y\ndisplayName: ワイ\n---\n本文Y");
        var vm = new RosterViewModel(() => RosterScanner.Scan(new[] { _root }, new RosterSettings()));

        await vm.LoadAsync();

        Assert.Equal(new[] { "claude", "gemini", "codex" }, vm.Groups.Select(g => g.Id));
        Assert.Equal("全2人", vm.Status);
        Assert.Equal("クローディ (Claude) — 1人", vm.Groups[0].Header);

        Assert.Equal(new[] { "T" }, vm.Groups[0].Teams.Select(t => t.Name)); // 置き場所の階層
        Assert.Equal("T — 1人", vm.Groups[0].Teams[0].Header);
        vm.Select(vm.Groups[0].Teams[0]);
        Assert.Equal("置き場所: T", vm.DetailMeta);

        vm.Select(vm.Groups[0].Members[0]);
        Assert.Equal("エックス", vm.DetailTitle);
        Assert.Equal("本文X", vm.DetailRules);
        Assert.Contains("識別名: x", vm.DetailMeta);

        vm.Select(vm.Groups[2]);
        Assert.Contains("定義が見つかりませんでした(.codex/agents)", vm.DetailMeta);
    }

    [Fact]
    public async Task 探すのに失敗したら_状況に理由を出す()
    {
        var vm = new RosterViewModel(() => throw new InvalidOperationException("読めません"));

        await vm.LoadAsync();

        Assert.Equal("探せませんでした: 読めません", vm.Status);
    }

    [Fact]
    public async Task 簡易表示では_名簿の見出しにキャラ名を出さない()
    {
        var vm = new RosterViewModel(() => Array.Empty<RosterMember>(), new AppearanceSettings(IsBusinessMode: true));

        await vm.LoadAsync();

        Assert.Equal(new[] { "Claude — 0人", "Gemini — 0人", "ChatGPT (Codex) — 0人" }, vm.Groups.Select(g => g.Header));
    }

    [Fact]
    public async Task 名簿の窓が読み込めて_画像に描ける()
    {
        Put("T/.claude/agents/x.md", "---\nname: x\ndisplayName: 実装担当 (ツクル)\ndescription: 実装する\nmodel: sonnet\n---\nあなたは実装エンジニアです。");
        Put("T/.claude/agents/z.md", "---\nname: z\ndisplayName: QA担当 (イジワル)\n---\nあら探しをします。");
        Put(".claude/agents/p.md", "---\nname: p\ndisplayName: 進行管理担当 (ダンドリ)\n---\n段取りします。");
        Put("U/.agents/skills/y/SKILL.md", "---\nname: y\ndisplayName: 査読担当 (タタキ)\n---\n査読します。");
        var vm = new RosterViewModel(() => RosterScanner.Scan(new[] { _root }, new RosterSettings()));
        await vm.LoadAsync();
        vm.Select(vm.Groups[0].Members[0]);

        var png = MainWindowRenderTests.RenderWindow(() => new RosterWindow(vm), new Size(920, 640), "roster_window.png");

        Assert.True(File.Exists(png));
    }

    [Fact]
    public void 実際のワークスペースを拾える()
    {
        var real = Environment.GetEnvironmentVariable("NAMAKE_ROSTER_ROOT");
        if (string.IsNullOrEmpty(real)) return; // 環境変数があるときだけ動く試験(本物のフォルダでの確認用)

        var members = RosterScanner.Scan(new[] { real }, new RosterSettings());

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "roster_real.txt"),
            members.Select(m => $"{m.Group}\t{m.Team}\t{m.Name}\t{m.DisplayName}\t{m.Model}\t{m.Rules.Length}"));
        Assert.NotEmpty(members);
    }
}
