using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>書く担当の指名と、作業フォルダの切り替えの試験。</summary>
public class WriterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly string[] ReadOnly = { "--tools", "Read" };
    private static readonly string[] Writable = { "--tools", "Read,Edit,Write", "--permission-mode", "acceptEdits" };

    private static ClaudeAgent Claude(IReadOnlyList<string>? writerArgs) => new(
        new ClaudeSettings("claude", "sonnet", ReadOnly, null, writerArgs), new FakeRunner(), "w", Timeout);

    [Fact]
    public void 書く担当にすると_書き込みを許す引数に切り替わる()
    {
        var agent = Claude(Writable);
        Assert.True(agent.CanWrite);

        var before = agent.BuildArgs("本文", null).ToList();
        Assert.DoesNotContain("acceptEdits", before);

        agent.WriteMode = true;
        var after = agent.BuildArgs("本文", null).ToList();
        Assert.Contains("acceptEdits", after);
        Assert.DoesNotContain("--dangerously-skip-permissions", after);

        agent.WriteMode = false;
        Assert.DoesNotContain("acceptEdits", agent.BuildArgs("本文", null));
    }

    [Fact]
    public void 段階ごとに_本文の先頭に状態の添え書きが付く()
    {
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", ReadOnly, null, Writable, "[状態]読み取り専用"),
            new FakeRunner(), "w", Timeout);

        Assert.Equal("[状態]読み取り専用\n\n本文", agent.BuildArgs("本文", null)[1]);

        // 一部できるは、設定の道具から文を組み立てる(コマンドの持ち物がないので「コマンドは使えません」)
        agent.WriteMode = true;
        var partial = agent.BuildArgs("本文", null)[1];
        Assert.StartsWith("[状態] いまは「読み書き」です。使える道具は Read, Edit, Write だけです。コマンドは使えません。", partial);
        Assert.EndsWith("\n\n本文", partial);
    }

    private static readonly string[] Full = { "--permission-mode", "auto" };
    private static readonly string[] Cmd = { "--tools", "Read,Edit,Write,PowerShell", "--permission-mode", "acceptEdits",
        "--allowedTools", "PowerShell(dotnet build*)", "PowerShell(dotnet test*)" };

    private static ClaudeAgent FullCapable(FakeRunner? runner = null) => new(
        new ClaudeSettings("claude", "sonnet", ReadOnly, null, Writable, "[状態]読むだけ", Cmd, null, Full, "[状態]何でもできる"),
        runner ?? new FakeRunner(), "w", Timeout);

    // 連携を1つもONにしていないときの、何でもできるの添え書き
    private static readonly string FullNoteWithoutConnectors = "[状態]何でもできる" + HarnessInfo.DescribeConnectors(null);

    [Fact]
    public void 何でもできるは_書く担当のときだけ有効で_道具を絞らず自動判定の引数になる()
    {
        var agent = FullCapable();
        Assert.True(agent.CanFull);

        // 書く担当でないうちは、何でもできるにしても効かない
        agent.FullMode = true;
        Assert.False(agent.FullMode);

        agent.WriteMode = true;
        agent.FullMode = true;
        var args = agent.BuildArgs("本文", null).ToList();
        Assert.Contains("auto", args);
        Assert.DoesNotContain("--tools", args); // 道具を絞らない
        Assert.DoesNotContain("--dangerously-skip-permissions", args);
        Assert.Equal(FullNoteWithoutConnectors + "\n\n本文", args[1]);
        Assert.Contains("--strict-mcp-config", args); // 連携をONにしていなければ、連携は読み込まない

        // 書く担当をやめると、何でもできるも外れる
        agent.WriteMode = false;
        Assert.False(agent.FullMode);
        Assert.DoesNotContain("auto", agent.BuildArgs("本文", null));
    }

    [Fact]
    public void 何でもできる用の引数が設定になければ_何でもできるにはできない()
    {
        var agent = Claude(Writable);
        agent.WriteMode = true;
        agent.FullMode = true;
        Assert.False(agent.CanFull);
        Assert.False(agent.FullMode);
    }

    [Fact]
    public void 一部できるでコマンドを使うときは_許可したコマンドを添え書きで伝える()
    {
        var agent = FullCapable();
        agent.WriteMode = true;
        agent.CommandMode = true;
        var note = agent.BuildArgs("本文", null)[1];
        Assert.Contains("使える道具は Read, Edit, Write, PowerShell だけです。", note);
        Assert.Contains("コマンドは dotnet build*、dotnet test* なら、確認なしで通ります。それ以外のコマンド(git など)は、実行の前に、画面でユーザーに承認を求めます(拒否されたら実行されません)。", note);
    }

    [Fact]
    public async Task 添え書きは_新しい会話の最初と_段階が変わった直後だけ付く()
    {
        var runner = new FakeRunner();
        runner.Lines.Add("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"はい\",\"session_id\":\"s1\"}");
        var agent = FullCapable(runner);

        // 新しい会話の最初は付く
        await agent.AskAsync("1回目", null, null, CancellationToken.None);
        Assert.Equal("[状態]読むだけ\n\n1回目", runner.LastArgs[1]);

        // 同じ段階のまま続けると、付かない
        await agent.AskAsync("2回目", "s1", null, CancellationToken.None);
        Assert.Equal("2回目", runner.LastArgs[1]);

        // 段階が変わった直後は、付く
        agent.WriteMode = true;
        agent.FullMode = true;
        await agent.AskAsync("3回目", "s1", null, CancellationToken.None);
        Assert.Equal(FullNoteWithoutConnectors + "\n\n3回目", runner.LastArgs[1]);

        await agent.AskAsync("4回目", "s1", null, CancellationToken.None);
        Assert.Equal("4回目", runner.LastArgs[1]);

        // 会話が新しくなったら(会話IDなし)、また付く
        await agent.AskAsync("5回目", null, null, CancellationToken.None);
        Assert.Equal(FullNoteWithoutConnectors + "\n\n5回目", runner.LastArgs[1]);
    }

    [Fact]
    public void カードの段階の選択で_書く担当と何でもできるが切り替わる()
    {
        var writer = new List<(string, bool)>();
        var full = new List<(string, bool)>();
        var member = new MemberViewModel(Participants.All.First(p => p.Id == Participants.ClaudeId),
            onWriterChanged: (id, v) => writer.Add((id, v)), onFullChanged: (id, v) => full.Add((id, v)))
        {
            CanWrite = true,
            CanFull = true,
            CanRunCommands = true,
        };
        Assert.Equal(new[] { "read", "partial", "full" }, member.LevelOptions.Select(o => o.Id));
        Assert.Equal("read", member.SelectedLevelId);

        member.SelectedLevelId = "full";
        Assert.True(member.IsWriter);
        Assert.True(member.IsFull);
        Assert.False(member.ShowCommandChoice); // 何でもできるのときは「コマンドも使う」を出さない

        member.SelectedLevelId = "partial";
        Assert.True(member.IsWriter);
        Assert.False(member.IsFull);
        Assert.True(member.ShowCommandChoice);

        member.SelectedLevelId = "read";
        Assert.False(member.IsWriter);
        Assert.Equal(new[] { (Participants.ClaudeId, true), (Participants.ClaudeId, false) }, writer);
        Assert.Equal(new[] { (Participants.ClaudeId, true), (Participants.ClaudeId, false) }, full);

        // 何でもできるにできないAI(ジェミリー)は、2段階
        Assert.Equal(new[] { "read", "partial" }, PermissionLevelOption.For(false).Select(o => o.Id));
    }

    [Fact]
    public void コマンドを使う持ち物は_書く担当のときだけ有効で_許可リストの引数に切り替わる()
    {
        var cmd = new[] { "--tools", "Read,Edit,Write,PowerShell", "--permission-mode", "acceptEdits",
            "--allowedTools", "PowerShell(dotnet build*)", "PowerShell(dotnet test*)" };
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", ReadOnly, null, Writable, null, cmd),
            new FakeRunner(), "w", Timeout);
        Assert.True(agent.CanRunCommands);

        // 書く担当でないうちは、コマンドをオンにしても効かない
        agent.CommandMode = true;
        Assert.DoesNotContain("PowerShell", string.Join(" ", agent.BuildArgs("本文", null)));

        agent.WriteMode = true;
        agent.CommandMode = true;
        var args = agent.BuildArgs("本文", null).ToList();
        Assert.Contains("PowerShell(dotnet build*)", args);
        Assert.DoesNotContain("PowerShell(dotnet *)", args);
        Assert.DoesNotContain("--dangerously-skip-permissions", args);

        // 書く担当をやめると、コマンドも使えなくなる
        agent.WriteMode = false;
        Assert.False(agent.CommandMode);
        Assert.DoesNotContain("PowerShell", string.Join(" ", agent.BuildArgs("本文", null)));
    }

    [Fact]
    public void コマンド用の引数が設定になければ_コマンドは使えない()
    {
        var agent = Claude(Writable);
        agent.WriteMode = true;
        agent.CommandMode = true;
        Assert.False(agent.CanRunCommands);
        Assert.False(agent.CommandMode);
    }

    [Fact]
    public void 画面のコマンド選択は_書く担当のときだけ見え_書く担当をやめるとオフに戻る()
    {
        var session = new WritableFakeSession();
        var vm = new MainViewModel(() => session);
        Assert.True(vm.ClaudeMember.CanRunCommands);
        Assert.False(vm.ClaudeMember.ShowCommandChoice);

        vm.ClaudeMember.IsWriter = true;
        Assert.True(vm.ClaudeMember.ShowCommandChoice);
        vm.ClaudeMember.CommandsOn = true;
        Assert.Contains(Participants.ClaudeId, session.Commanders);

        vm.ClaudeMember.IsWriter = false;
        Assert.False(vm.ClaudeMember.CommandsOn);
        Assert.DoesNotContain(Participants.ClaudeId, session.Commanders);
        Assert.False(vm.ClaudeMember.ShowCommandChoice);
    }

    [Fact]
    public void 書き込み用の引数が設定にないAIは_書く担当になれない()
    {
        var agent = Claude(null);
        agent.WriteMode = true;
        Assert.False(agent.CanWrite);
        Assert.False(agent.WriteMode);
    }

    [Fact]
    public void agyは書き込み用の引数が設定にあれば書く担当になれ_本文の添え書きと引数が切り替わる()
    {
        var settings = new AgySettings("agy", new[] { "--mode", "plan" }, "[読まない]", null, null,
            new[] { "--mode", "accept-edits" }, "[書く]", "外にも書ける");
        var agy = new AgyAgent(settings, new FakeRunner(), "w", Timeout);
        Assert.True(agy.CanWrite);

        var before = agy.BuildArgs("本文", null).ToList();
        Assert.Equal("[読まない]本文", before[1]);
        Assert.Contains("plan", before);

        agy.WriteMode = true;
        var after = agy.BuildArgs("本文", null).ToList();
        Assert.Equal("[書く]本文", after[1]);
        Assert.Contains("accept-edits", after);
        Assert.DoesNotContain("plan", after);
        Assert.DoesNotContain("--dangerously-skip-permissions", after);
        Assert.Equal("外にも書ける", agy.WriterWarning);
    }

    [Fact]
    public void agyの何でもできるは_書く担当のときだけ有効で_引数と添え書きが切り替わる()
    {
        var settings = new AgySettings("agy", new[] { "--mode", "plan" }, "[読まない]", null, null,
            new[] { "--mode", "accept-edits" }, "[書く]", "外にも書ける", null,
            new[] { "--dangerously-skip-permissions" }, "[何でも]", "止まらない");
        var agy = new AgyAgent(settings, new FakeRunner(), "w", Timeout);
        Assert.True(agy.CanFull);

        agy.FullMode = true; // 書く担当でないうちは、有効にならない
        Assert.False(agy.FullMode);

        agy.WriteMode = true;
        agy.FullMode = true;
        Assert.True(agy.FullMode);
        var args = agy.BuildArgs("本文", null).ToList();
        Assert.Equal("[何でも]本文", args[1]);
        Assert.Contains("--dangerously-skip-permissions", args);
        Assert.DoesNotContain("accept-edits", args);
        Assert.DoesNotContain("plan", args);
        Assert.Equal("止まらない", agy.FullWarning);
        Assert.Contains("【いまの段階: 全部", agy.HarnessSummary);
        Assert.DoesNotContain("--dangerously-skip-permissions", agy.HarnessSummary); // 起動引数は画面に出さない

        agy.WriteMode = false; // 読むだけに戻すと、何でもできるも外れる
        Assert.False(agy.FullMode);
        Assert.DoesNotContain("--dangerously-skip-permissions", agy.BuildArgs("本文", null));
    }

    [Fact]
    public void agyは何でもできる用の引数が設定になければ_何でもできるにならない()
    {
        var settings = new AgySettings("agy", new[] { "--mode", "plan" }, "", null, null,
            new[] { "--mode", "accept-edits" });
        var agy = new AgyAgent(settings, new FakeRunner(), "w", Timeout);
        agy.WriteMode = true;
        agy.FullMode = true;
        Assert.False(agy.CanFull);
        Assert.False(agy.FullMode);
        Assert.DoesNotContain("--dangerously-skip-permissions", agy.BuildArgs("本文", null));
    }

    [Fact]
    public void ジェミリーの段階の選択肢は_何でもできるがあるとき_真ん中を書き換えできると呼ぶ()
    {
        Assert.Equal(new[] { "読む", "読み書き", "全部" },
            PermissionLevelOption.For(true, true).Select(o => o.Label));
        Assert.Equal(new[] { "読む", "読み書き", "全部" },
            PermissionLevelOption.For(true).Select(o => o.Label));
    }

    [Fact]
    public void agyは書き込み用の引数が設定になければ_書く担当になれない()
    {
        IChatAgent agy = new AgyAgent(new AgySettings("agy", new[] { "--mode", "plan" }, ""), new FakeRunner(), "w", Timeout);
        agy.WriteMode = true;
        Assert.False(agy.CanWrite);
        Assert.False(agy.WriteMode);
    }

    [Fact]
    public async Task 作業フォルダを切り替えると_次の呼び出しから新しいフォルダで動く()
    {
        var runner = new DirRunner();
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", ReadOnly), runner, "C:/old", Timeout);
        agent.SetWorkDir("C:/new");
        runner.Lines.Add("{\"type\":\"result\",\"result\":\"ok\",\"session_id\":\"s\",\"is_error\":false}");

        await agent.AskAsync("本文", null, null, CancellationToken.None);

        Assert.Equal("C:/new", runner.LastWorkDir);
    }

    [Fact]
    public void 会議は_書く担当を1人ずつ切り替えられ_両方にもでき_作業フォルダを全員に伝える()
    {
        using var dir = new TempDir();
        var claude = new WritableFakeAgent(Participants.ClaudeId, "Claude");
        var other = new WritableFakeAgent(Participants.GeminiId, "Gemini");
        string? saved = null;
        var session = new ChatSession(new IChatAgent[] { claude, other },
            new MarkdownLogWriter(dir.Path, DateTime.Now), 1000, onWorkDirChanged: d => saved = d);

        session.SetWriter(Participants.ClaudeId, true);
        Assert.True(claude.WriteMode);
        Assert.False(other.WriteMode);

        // 作業が別々なら、両方を書く担当にしてよい
        session.SetWriter(Participants.GeminiId, true);
        Assert.True(claude.WriteMode);
        Assert.True(other.WriteMode);

        session.SetWriter(Participants.ClaudeId, false);
        Assert.False(claude.WriteMode);
        Assert.True(other.WriteMode);

        session.SetWorkDir("C:/proj");
        Assert.Equal("C:/proj", claude.WorkDir);
        Assert.Equal("C:/proj", other.WorkDir);
        Assert.Equal("C:/proj", session.WorkDir);
        Assert.Equal("C:/proj", saved);
    }

    [Fact]
    public void 設定ファイルの作業フォルダだけを書き換え_他の設定は残す()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{\"workDir\":\"C:/old\",\"timeoutSec\":42,\"memo\":\"日本語\"}");

        SettingsLoader.SaveWorkDir(path, @"C:\IT開発\proj");

        var text = File.ReadAllText(path);
        Assert.Contains("\"workDir\": \"C:/IT開発/proj\"", text);
        Assert.Contains("\"timeoutSec\": 42", text);
        Assert.Contains("日本語", text);
    }

    [Fact]
    public void 画面で書く担当を選ぶと会議に伝わり_両方を同時に選べて_注意はジェミリーだけに出る()
    {
        var session = new WritableFakeSession();
        var vm = new MainViewModel(() => session);
        Assert.True(vm.ClaudeMember.CanWrite);
        Assert.True(vm.GeminiMember.CanWrite);
        Assert.False(vm.GeminiMember.ShowWriterWarning); // 選ぶまでは出さない

        vm.ClaudeMember.IsWriter = true;
        vm.GeminiMember.IsWriter = true;
        Assert.Equal(new[] { Participants.ClaudeId, Participants.GeminiId }, session.Writers.OrderBy(x => x));
        Assert.True(vm.ClaudeMember.IsWriter);
        Assert.True(vm.GeminiMember.ShowWriterWarning);
        Assert.False(vm.ClaudeMember.ShowWriterWarning);

        vm.ClaudeMember.IsWriter = false;
        Assert.Equal(new[] { Participants.GeminiId }, session.Writers);
    }

    [Fact]
    public void 新しい会議は_全員が読み取り専用から始まる_作業フォルダは引き継ぐ()
    {
        var sessions = new Queue<WritableFakeSession>(new[] { new WritableFakeSession(), new WritableFakeSession() });
        var vm = new MainViewModel(() => sessions.Dequeue());
        vm.ClaudeMember.IsWriter = true;
        vm.ChangeWorkDir("C:/proj");
        Assert.Equal("C:/proj", vm.WorkDir);

        vm.StartNewMeeting();

        Assert.False(vm.ClaudeMember.IsWriter);
    }

    [Fact]
    public void ハーネスのツールチップは_読むだけ_一部できる_コマンドON_何でもできるの切り替えに連動する()
    {
        using var dir = new TempDir();
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"))
            with { MeetingsDir = dir.Path };
        var session = new ChatSessionFactory(settings, new FakeRunner()).Create();
        var vm = new MainViewModel(() => session);

        // 初期状態: 読むだけ
        Assert.Equal("read", vm.ClaudeMember.SelectedLevelId);
        Assert.Contains("読む", vm.ClaudeMember.HarnessTooltip);
        Assert.Contains("📄 ファイル読み込み (Read)", vm.ClaudeMember.HarnessTooltip);
        Assert.DoesNotContain("許可コマンド", vm.ClaudeMember.HarnessTooltip);
        Assert.Contains("【いまの段階: 読む】", vm.GeminiMember.HarnessTooltip);
        Assert.DoesNotContain("--", vm.GeminiMember.HarnessTooltip); // 起動引数は出さない

        // 読み書き: 編集が含まれる(サブエージェント呼び出しは、道具の固定の組み合わせに入れない)
        vm.ClaudeMember.SelectedLevelId = "partial";
        vm.GeminiMember.SelectedLevelId = "partial";
        Assert.Contains("読み書き", vm.ClaudeMember.HarnessTooltip);
        Assert.Contains("(Edit)", vm.ClaudeMember.HarnessTooltip);
        Assert.DoesNotContain("(Agent)", vm.ClaudeMember.HarnessTooltip);
        Assert.Contains("【いまの段階: 読み書き】", vm.GeminiMember.HarnessTooltip);
        Assert.DoesNotContain("--", vm.GeminiMember.HarnessTooltip);
        Assert.Contains("コマンドは使えません", vm.GeminiMember.HarnessTooltip);

        // 既定の設定では「コマンドも使う」がない(読み書きの道具は固定。コマンドは「全部」で使う)
        Assert.False(vm.ClaudeMember.ShowCommandChoice);
        Assert.DoesNotContain("許可コマンド", vm.ClaudeMember.HarnessTooltip);

        // 何でもできる: クローディは道具はすべて・許可は自動判定、ジェミリーは許可の確認をすべて飛ばす(Turbo)
        Assert.True(vm.ClaudeMember.CanFull);
        Assert.True(vm.GeminiMember.CanFull);
        vm.ClaudeMember.SelectedLevelId = "full";
        Assert.Contains("全部", vm.ClaudeMember.HarnessTooltip);
        Assert.DoesNotContain("起動引数", vm.ClaudeMember.HarnessTooltip);
        Assert.DoesNotContain("--permission-mode", vm.ClaudeMember.HarnessTooltip);
        vm.GeminiMember.SelectedLevelId = "full";
        Assert.Contains("全部", vm.GeminiMember.HarnessTooltip);
        Assert.DoesNotContain("--dangerously-skip-permissions", vm.GeminiMember.HarnessTooltip);
        Assert.True(vm.GeminiMember.ShowWriterWarning);
        Assert.Contains("判定がありません", vm.GeminiMember.DisplayWarning);
        vm.GeminiMember.SelectedLevelId = "partial"; // 書き換えできるに戻すと、元の注意に戻る
        Assert.Contains("外にも書けて", vm.GeminiMember.DisplayWarning);
    }

    [Fact]
    public void ハーネス設定を組み立てて保存すると_settings_jsonと会議のAIに反映される()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "settings.json"), path);
        var loaded = SettingsLoader.Load(path) with { MeetingsDir = dir.Path };

        var factory = new ChatSessionFactory(
            loaded,
            new FakeRunner(),
            saveClaude: c => SettingsLoader.SaveClaudeHarness(path, c),
            saveAgy: a => SettingsLoader.SaveAgyHarness(path, a));
        var vm = new MainViewModel(factory.Create);

        var current = vm.GetClaudeSettings()!;
        var updated = HarnessInfo.BuildClaudeSettings(
            current,
            new[] { "Read", "Glob" },
            new[] { "Read", "Edit", "Write", "Agent", "WebSearch" },
            new[] { "dotnet build*", "dotnet test*", "git status*" });

        vm.UpdateClaudeHarness(updated);

        // 読み取り専用時のツールチップに Glob が増えている
        Assert.Contains("🔍 ファイル検索 (Glob)", vm.ClaudeMember.HarnessTooltip);

        // 書く担当＋コマンドONにすると WebSearch と git status* が反映されている
        vm.ClaudeMember.IsWriter = true;
        vm.ClaudeMember.CommandsOn = true;
        Assert.Contains("🔭 Web検索 (WebSearch)", vm.ClaudeMember.HarnessTooltip);
        Assert.Contains("git status*", vm.ClaudeMember.HarnessTooltip);

        // ファイルにも保存されている
        var reloaded = SettingsLoader.Load(path);
        Assert.Contains("WebSearch", HarnessInfo.ParseTools(reloaded.Claude.WriterExtraArgs));
        Assert.Contains("git status*", HarnessInfo.ParseAllowedCommands(reloaded.Claude.CommandExtraArgs));
    }

    /// <summary>作業フォルダを記録する偽のプロセス実行。</summary>
    private sealed class DirRunner : IProcessRunner
    {
        public List<string> Lines { get; } = new();
        public string? LastWorkDir { get; private set; }

        public Task RunLinesAsync(string fileName, IReadOnlyList<string> args, string workDir,
            TimeSpan timeout, Action<string> onLine, CancellationToken ct)
        {
            LastWorkDir = workDir;
            foreach (var l in Lines) onLine(l);
            return Task.CompletedTask;
        }
    }

    /// <summary>書く担当になれる偽のAI。</summary>
    private sealed class WritableFakeAgent : IChatAgent
    {
        public WritableFakeAgent(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
        public string ModelId => "";
        public string ModelLabel => "";
        public bool CanWrite => true;
        public bool WriteMode { get; set; }
        public string WorkDir { get; private set; } = "";
        public void SetWorkDir(string workDir) => WorkDir = workDir;
        public void SetModel(string modelId) { }
        public IReadOnlyList<ModelOption> ModelOptions => Array.Empty<ModelOption>();
        public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct) =>
            Task.FromResult(ModelOptions);
        public Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
            CancellationToken ct) => Task.FromResult(new AgentReply("", ""));
    }

    /// <summary>両方が書く担当になれる偽の会議(ジェミリーには注意つき)。</summary>
    private sealed class WritableFakeSession : IChatSession
    {
        public HashSet<string> Writers { get; } = new();
        public HashSet<string> Commanders { get; } = new();
        public string WorkDir { get; private set; } = "C:/start";
        public string LogPath => "fake.md";
        public IReadOnlyDictionary<string, string> ModelLabels { get; } = new Dictionary<string, string>
        {
            [Participants.ClaudeId] = "sonnet",
            [Participants.GeminiId] = "",
        };
        public string ModelIdOf(string agentId) => "";
        public void SetModel(string agentId, string modelId) { }
        public IReadOnlyList<ModelOption> ModelOptionsFor(string agentId) => Array.Empty<ModelOption>();
        public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(string agentId) =>
            Task.FromResult<IReadOnlyList<ModelOption>>(Array.Empty<ModelOption>());
        public Task SendAsync(string text, IReadOnlyList<string> targetIds, Action<ChatProgress> onProgress) =>
            Task.CompletedTask;
        public bool CanWrite(string agentId) => true;
        public bool CanRunCommands(string agentId) => agentId == Participants.ClaudeId;
        public void SetCommands(string agentId, bool enabled)
        {
            if (enabled) Commanders.Add(agentId);
            else Commanders.Remove(agentId);
        }
        public string WriterWarning(string agentId) => agentId == Participants.GeminiId ? "外にも書ける" : "";
        public void SetWriter(string agentId, bool isWriter)
        {
            if (isWriter) Writers.Add(agentId);
            else Writers.Remove(agentId);
        }
        public void SetWorkDir(string workDir) => WorkDir = workDir;
    }
}
