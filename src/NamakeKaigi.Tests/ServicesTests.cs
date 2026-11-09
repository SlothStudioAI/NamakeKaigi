using System.Diagnostics;
using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

public class MeetingTranscriptTests
{
    private static ChatEntry E(string who, string text) => new(who, text, new DateTime(2026, 10, 24, 12, 0, 0));

    [Fact]
    public void 自分の発言は添えず_既読にするまで同じ未読が返る()
    {
        var t = new MeetingTranscript();
        t.Add(E(Participants.UserId, "こんにちは"));
        t.Add(E(Participants.ClaudeId, "やあ"));

        var (text, upTo) = t.PeekUnseen(Participants.ClaudeId, 1000);
        Assert.Equal("【ユーザー】こんにちは", text);
        Assert.Equal(2, upTo);
        Assert.Equal(text, t.PeekUnseen(Participants.ClaudeId, 1000).Text); // まだ既読にしていない

        t.MarkSeen(Participants.ClaudeId, upTo);
        Assert.Equal("", t.PeekUnseen(Participants.ClaudeId, 1000).Text);
    }

    [Fact]
    public void 上限を超えた長い発言は_先頭を残して省略の印を付ける()
    {
        var t = new MeetingTranscript();
        t.Add(E(Participants.UserId, "[添付ファイル: C:/x/a.cs]\n" + new string('あ', 6000) + "おわり"));
        var (text, _) = t.PeekUnseen(Participants.GeminiId, 4000);
        Assert.True(text.Length <= 4000);
        Assert.Contains("[添付ファイル: C:/x/a.cs]", text); // 先頭(パスの行)が残る
        Assert.EndsWith("(長いため省略)", text);
        Assert.DoesNotContain("おわり", text);
    }

    [Fact]
    public void 順番で2人目に渡すとき_1人目の長い返答があっても_ユーザーの質問は消えない()
    {
        var t = new MeetingTranscript();
        t.Add(E(Participants.UserId, "この設計をどう思う?"));
        t.Add(E(Participants.ClaudeId, new string('い', 6000)));
        var (text, _) = t.PeekUnseen(Participants.GeminiId, 4000);
        Assert.True(text.Length <= 4000);
        Assert.Contains("この設計をどう思う?", text);
        Assert.Contains("(長いため省略)", text);
    }

    [Fact]
    public void 発言が多すぎるときは_古い発言から外す()
    {
        var t = new MeetingTranscript();
        for (var i = 0; i < 100; i++) t.Add(E(Participants.UserId, $"発言{i:000}" + new string('う', 200)));
        var (text, _) = t.PeekUnseen(Participants.GeminiId, 4000);
        Assert.True(text.Length <= 4000);
        Assert.StartsWith("(古い発言は省略)", text);
        Assert.Contains("発言099", text);
        Assert.DoesNotContain("発言000", text);
    }

    [Fact]
    public void 省略の位置がサロゲートペアの途中でも_壊れた文字を残さない()
    {
        var t = new MeetingTranscript();
        t.Add(E(Participants.UserId, string.Concat(Enumerable.Repeat("😀", 3000))));
        var (text, _) = t.PeekUnseen(Participants.GeminiId, 1001);
        Assert.DoesNotContain('\uFFFD', text);
        Assert.Equal(text.Count(char.IsHighSurrogate), text.Count(char.IsLowSurrogate));
    }

    [Theory]
    [InlineData(typeof(TimeoutException), "conv", false, false, false)]
    [InlineData(typeof(InvalidOperationException), "conv", true, false, false)]
    [InlineData(typeof(InvalidOperationException), "conv", false, true, false)]
    [InlineData(typeof(InvalidOperationException), null, false, false, false)]
    [InlineData(typeof(InvalidOperationException), "conv", false, false, true)]
    public void 新しい会話でやり直すのは_会話が消えていた失敗だけ(Type exType, string? convId, bool workStarted, bool cancelled, bool expected)
    {
        using var cts = new CancellationTokenSource();
        if (cancelled) cts.Cancel();
        var ex = (Exception)Activator.CreateInstance(exType, "失敗")!;
        Assert.Equal(expected, ChatSession.ShouldRetryWithNewConversation(ex, convId, workStarted, cts.Token));
    }
}

public class ClaudeAgentTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static ClaudeAgent Make(FakeRunner r) => new(
        new ClaudeSettings("claude", "sonnet", new[] { "--tools", "Read" }), r, "C:/work", Timeout);

    private static void AddNormalStream(FakeRunner r)
    {
        r.Lines.Add("{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-sonnet-5-5\",\"session_id\":\"s1\"}");
        r.Lines.Add("{\"type\":\"stream_event\",\"event\":{\"type\":\"message_start\"}}");
        r.Lines.Add("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"2 \"}}}");
        r.Lines.Add("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"です。\"}}}");
        r.Lines.Add("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"2 です。\",\"session_id\":\"s1\"}");
    }

    [Fact]
    public void 引数は_流れてくる形式で_モデルと追加引数が入り_会話IDがあればresumeが付く()
    {
        var agent = Make(new FakeRunner());
        Assert.Equal(
            new[]
            {
                "-p", "本文", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
                "--model", "sonnet", "--tools", "Read",
            },
            agent.BuildArgs("本文", null));
        var args = agent.BuildArgs("本文", "abc");
        Assert.Equal("--resume", args[^2]);
        Assert.Equal("abc", args[^1]);

        agent.SetExtraDirs(new[] { "C:/ext1", "C:/ext2" });
        agent.SetEffort("high");
        var withDirs = agent.BuildArgs("本文", null);
        Assert.Contains("--add-dir", withDirs);
        Assert.Contains("C:/ext1", withDirs);
        Assert.Contains("C:/ext2", withDirs);
        Assert.Contains("--effort", withDirs);
        Assert.Contains("high", withDirs);
    }

    [Fact]
    public async Task 流れてきた文字とモデル名を知らせ_完成した返答とセッションIDを返す()
    {
        var runner = new FakeRunner();
        AddNormalStream(runner);
        var updates = new List<AgentUpdate>();

        var reply = await Make(runner).AskAsync("1+1は?", null, updates.Add, CancellationToken.None);

        Assert.Equal(new AgentReply("2 です。", "s1", "claude-sonnet-5-5"), reply);
        Assert.Equal(new[]
        {
            new AgentUpdate(AgentUpdateKind.Model, "claude-sonnet-5-5"),
            new AgentUpdate(AgentUpdateKind.Delta, "2 "),
            new AgentUpdate(AgentUpdateKind.Delta, "です。"),
        }, updates);
        Assert.Equal("claude", runner.LastFile);
    }

    [Fact]
    public async Task 結果の行まで届いていれば_終了コードの失敗は無視する()
    {
        var runner = new FakeRunner { ThrowAfter = new InvalidOperationException("終了コード 1") };
        AddNormalStream(runner);

        var reply = await Make(runner).AskAsync("x", null, null, CancellationToken.None);

        Assert.Equal("2 です。", reply.Text);
    }

    [Fact]
    public async Task エラーの結果や_結果が届かないときは例外にする()
    {
        var errorRunner = new FakeRunner();
        errorRunner.Lines.Add("{\"type\":\"result\",\"is_error\":true,\"result\":\"失敗\",\"session_id\":\"s\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Make(errorRunner).AskAsync("x", null, null, CancellationToken.None));

        var noResult = new FakeRunner();
        noResult.Lines.Add("{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"m\",\"session_id\":\"s\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Make(noResult).AskAsync("x", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task JSONでない行は無視する()
    {
        var runner = new FakeRunner();
        runner.Lines.Add("警告: なにかの文章");
        runner.Lines.Add("{壊れたJSON");
        AddNormalStream(runner);

        var reply = await Make(runner).AskAsync("x", null, null, CancellationToken.None);

        Assert.Equal("2 です。", reply.Text);
    }
}

public class AgyAgentTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static AgyAgent Make(FakeRunner r, string? model = null) => new(
        new AgySettings("agy", new[] { "--mode", "plan" }, "ツール禁止。", model), r, "C:/work", Timeout);

    private static void AddNormalStream(FakeRunner r)
    {
        r.Lines.Add("{\"event\":\"init\",\"conversation_id\":\"c1\",\"init\":{\"cwd\":\"C:/work\"}}");
        r.Lines.Add("{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"user_input\",\"state\":\"DONE\"}}");
        r.Lines.Add("{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"text_delta\":\"水耕栽培には\"}}");
        r.Lines.Add("{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"text_delta\":\"バジルです。\"}}");
        r.Lines.Add("{\"event\":\"result\",\"result\":{\"conversation_id\":\"c1\",\"status\":\"SUCCESS\",\"response\":\"水耕栽培にはバジルです。\"}}");
    }

    [Fact]
    public void 引数は_本文の先頭に添え書きが付き_planモードで_会話IDは_conversationになる()
    {
        var agent = Make(new FakeRunner());
        Assert.Equal(new[] { "-p", "ツール禁止。本文", "--output-format", "stream-json", "--mode", "plan" },
            agent.BuildArgs("本文", null));
        var args = agent.BuildArgs("本文", "c9");
        Assert.Equal("--conversation", args[^2]);
        Assert.Equal("c9", args[^1]);

        agent.SetExtraDirs(new[] { "C:/extA" });
        agent.SetEffort("xhigh");
        var withDirs = agent.BuildArgs("本文", null);
        Assert.Contains("--add-dir", withDirs);
        Assert.Contains("C:/extA", withDirs);
        Assert.DoesNotContain("--effort", withDirs); // agyはモデル名に労力が含まれるので、労力は渡さない
        Assert.False(agent.SupportsEffort);
    }

    [Fact]
    public void モデルを指定したときだけ_modelが付き_表示名も変わる()
    {
        Assert.DoesNotContain("--model", Make(new FakeRunner()).BuildArgs("本文", null));
        Assert.Equal(AgyAgent.UnspecifiedModelLabel, Make(new FakeRunner()).ModelLabel);
        Assert.Equal(AgyAgent.UnspecifiedModelLabel, Make(new FakeRunner(), "  ").ModelLabel);

        var agent = Make(new FakeRunner(), "gemini-3.8-flash-high");
        var args = agent.BuildArgs("本文", null).ToList();
        Assert.Equal("gemini-3.8-flash-high", args[args.IndexOf("--model") + 1]);
        Assert.Equal("gemini-3.8-flash-high", agent.ModelLabel);
    }

    [Fact]
    public async Task 流れてきた文字を知らせ_完成した返答と会話IDを返す()
    {
        var runner = new FakeRunner();
        AddNormalStream(runner);
        var updates = new List<AgentUpdate>();

        var reply = await Make(runner).AskAsync("x", null, updates.Add, CancellationToken.None);

        Assert.Equal(new AgentReply("水耕栽培にはバジルです。", "c1"), reply);
        Assert.Equal(new[]
        {
            new AgentUpdate(AgentUpdateKind.Delta, "水耕栽培には"),
            new AgentUpdate(AgentUpdateKind.Delta, "バジルです。"),
        }, updates); // user_input の行は流さない
    }

    [Fact]
    public async Task 返答が空_エラー_結果なしは例外にする()
    {
        var empty = new FakeRunner();
        empty.Lines.Add("{\"event\":\"result\",\"result\":{\"conversation_id\":\"c\",\"status\":\"SUCCESS\",\"response\":\"\"}}");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Make(empty).AskAsync("x", null, null, CancellationToken.None));

        var error = new FakeRunner();
        error.Lines.Add("{\"event\":\"result\",\"result\":{\"status\":\"ERROR\",\"response\":\"\",\"error\":\"壊れた\"}}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Make(error).AskAsync("x", null, null, CancellationToken.None));
        Assert.Contains("壊れた", ex.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Make(new FakeRunner()).AskAsync("x", null, null, CancellationToken.None));
    }
}

public class SettingsAndLogTests
{
    [Fact]
    public void 同梱のsettings_jsonを読める()
    {
        var s = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        Assert.Equal("sonnet", s.Claude.Model);
        Assert.Contains("plan", s.Agy.ExtraArgs);
        Assert.Equal("gemini-4-argon-medium", s.Agy.Model); // agyの実際の既定を、明示してある
        Assert.Equal(new[] { "sonnet", "opus", "haiku" }, s.Claude.Models);
        Assert.NotEmpty(s.Agy.Models!);
        Assert.True(s.MaxInjectChars > 0);
        Assert.True(s.TimeoutSec > 0);
    }

    [Fact]
    public void モデルの欄がない古い設定でも読める()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path,
            "{\"claude\":{\"executable\":\"c\",\"model\":\"m\",\"extraArgs\":[]},\"agy\":{\"executable\":\"a\",\"extraArgs\":[],\"noToolNote\":\"n\"},"
            + "\"workDir\":\"w\",\"meetingsDir\":\"d\",\"maxInjectChars\":1,\"timeoutSec\":1}");
        Assert.Null(SettingsLoader.Load(path).Agy.Model);
    }

    [Fact]
    public void ログは_起動した時刻のフォルダの_会議名のフォルダのlog_mdに追記される()
    {
        using var dir = new TempDir();
        var session = MarkdownLogWriter.SessionDirFor(dir.Path, new DateTime(2026, 10, 24, 12, 30, 0));
        var log = new MarkdownLogWriter(session, new DateTime(2026, 10, 24, 12, 30, 0), "会議室1");
        log.Append(new ChatEntry(Participants.UserId, "  こんにちは  ", new DateTime(2026, 10, 24, 12, 30, 5)));
        log.Append(new ChatEntry(Participants.ClaudeId, "やあ", new DateTime(2026, 10, 24, 12, 30, 9)));

        Assert.Equal(Path.Combine(dir.Path, "20261024_123000", "会議室1", "log.md"), log.Path);
        var text = File.ReadAllText(log.Path);
        Assert.Contains("## ユーザー (12:30:05)\n\nこんにちは\n", text);
        Assert.Contains("## Claude (12:30:09)\n\nやあ\n", text);
    }

    [Fact]
    public void ログは_ユーザーの発言に宛先を添える()
    {
        using var dir = new TempDir();
        var log = new MarkdownLogWriter(dir.Path, new DateTime(2026, 10, 24, 12, 30, 0));
        log.Append(new ChatEntry(Participants.UserId, "どう?", new DateTime(2026, 10, 24, 12, 31, 0),
            new[] { Participants.GeminiId }));

        var expected = "## ユーザー → " + Participants.Get(Participants.GeminiId).LogName + " (12:31:00)";
        Assert.Contains(expected, File.ReadAllText(log.Path));
    }
}

public class ProcessRunnerTests
{
    private static async Task<List<string>> Run(string[] args, TimeSpan timeout)
    {
        var lines = new List<string>();
        await new ProcessRunner().RunLinesAsync("cmd.exe", args, Environment.CurrentDirectory, timeout,
            lines.Add, CancellationToken.None);
        return lines;
    }

    [Fact]
    public async Task 実際にコマンドを実行して_標準出力を1行ずつ受け取る()
    {
        var lines = await Run(new[] { "/c", "echo hello & echo world" }, TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "hello ", "world" }, lines);
    }

    [Fact]
    public async Task 出力は終了を待たず_出た順に届く()
    {
        var arrivals = new List<(string Line, TimeSpan At)>();
        var sw = Stopwatch.StartNew();
        await new ProcessRunner().RunLinesAsync("cmd.exe",
            new[] { "/c", "echo first & ping -n 3 127.0.0.1 > nul & echo second" },
            Environment.CurrentDirectory, TimeSpan.FromSeconds(10),
            line => arrivals.Add((line.Trim(), sw.Elapsed)), CancellationToken.None);

        Assert.Equal(new[] { "first", "second" }, arrivals.Select(a => a.Line));
        Assert.True(arrivals[1].At - arrivals[0].At > TimeSpan.FromMilliseconds(500)); // 先の行は、終わりを待たずに届いた
    }

    [Fact]
    public async Task 終了コードが0以外なら例外にする()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Run(new[] { "/c", "exit", "3" }, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task タイムアウトしたら止めて例外にする()
    {
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(
            () => Run(new[] { "/c", "ping -n 20 127.0.0.1 > nul" }, TimeSpan.FromMilliseconds(500)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void 絶対パスはそのまま_名前だけならPATHから探す()
    {
        Assert.Equal(@"C:\x\agy.exe", ProcessRunner.ResolveExecutable(@"C:\x\agy.exe"));
        Assert.EndsWith("cmd.exe", ProcessRunner.ResolveExecutable("cmd"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("存在しない道具", ProcessRunner.ResolveExecutable("存在しない道具"));
    }
}

public class ChatSessionTests
{
    private static readonly string[] Everyone = { Participants.ClaudeId, Participants.GeminiId };

    [Fact]
    public void 制限時間を変えると_両方のAIに効き_保存の処理が呼ばれる()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("a", "c"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("b", "g"));
        var saved = new List<int>();
        var session = new ChatSession(new[] { claude, gemini }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000,
            timeoutSec: 600, onTimeoutChanged: saved.Add);
        Assert.Equal(600, session.TimeoutSec);

        session.SetTimeout(1200);
        Assert.Equal(1200, session.TimeoutSec);
        Assert.Equal(TimeSpan.FromSeconds(1200), claude.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1200), gemini.Timeout);
        Assert.Equal(new[] { 1200 }, saved);

        session.SetTimeout(1200); // 同じ値や0以下では、何もしない
        session.SetTimeout(0);
        Assert.Equal(new[] { 1200 }, saved);
    }

    private static (ChatSession Session, FakeAgent Claude, FakeAgent Gemini, TempDir Dir) Make()
    {
        var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude",
            (_, _, n) => new AgentReply($"Claudeの{n}回目", "claude-conv"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini",
            (_, _, n) => new AgentReply($"Geminiの{n}回目", "gemini-conv"));
        var log = new MarkdownLogWriter(dir.Path, DateTime.Now);
        return (new ChatSession(new[] { claude, gemini }, log, 4000), claude, gemini, dir);
    }

    private static List<ChatProgress> Finals(IEnumerable<ChatProgress> all) =>
        all.Where(p => p.Kind is ChatProgressKind.Done or ChatProgressKind.Error).ToList();

    [Fact]
    public async Task 全員宛てなら両方に送り_完成が2件届き_ログに残る()
    {
        var (session, claude, gemini, dir) = Make();
        using var _ = dir;
        var progress = new List<ChatProgress>();

        await session.SendAsync("案をください", Everyone, progress.Add);

        var finals = Finals(progress);
        Assert.Equal(2, finals.Count);
        Assert.All(finals, r => Assert.Equal(ChatProgressKind.Done, r.Kind));
        Assert.Contains("【ユーザー】案をください", claude.Calls[0].Prompt);
        Assert.Contains("あなたはGemini", gemini.Calls[0].Prompt);
        var log = File.ReadAllText(session.LogPath);
        Assert.Contains("案をください", log);
        Assert.Contains("Claudeの1回目", log);
        Assert.Contains("Geminiの1回目", log);
    }

    [Fact]
    public async Task 流れてきた文字とモデル名が_そのまま通知される()
    {
        var dir = new TempDir();
        using var _ = dir;
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("完成", "c", "claude-sonnet-5-5"))
        {
            Updates = new[]
            {
                new AgentUpdate(AgentUpdateKind.Model, "claude-sonnet-5-5"),
                new AgentUpdate(AgentUpdateKind.Delta, "完"),
                new AgentUpdate(AgentUpdateKind.Delta, "成"),
            },
        };
        var session = new ChatSession(new[] { claude }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000);
        var progress = new List<ChatProgress>();

        await session.SendAsync("どうぞ", new[] { Participants.ClaudeId }, progress.Add);

        Assert.Equal(new[]
        {
            new ChatProgress(Participants.ClaudeId, ChatProgressKind.Started, ""), // 聞き始めた知らせ(順番の2人目の吹き出しを出すため)
            new ChatProgress(Participants.ClaudeId, ChatProgressKind.Model, "claude-sonnet-5-5"),
            new ChatProgress(Participants.ClaudeId, ChatProgressKind.Delta, "完"),
            new ChatProgress(Participants.ClaudeId, ChatProgressKind.Delta, "成"),
            new ChatProgress(Participants.ClaudeId, ChatProgressKind.Model, "claude-sonnet-5-5"),
            new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "完成"),
        }, progress);
    }

    [Fact]
    public void モデル名の初期表示は_AIごとの設定から取る()
    {
        var (session, _, _, dir) = Make();
        using var _ = dir;
        Assert.Equal("fake-model", session.ModelLabels[Participants.ClaudeId]);
        Assert.Equal("fake-model", session.ModelLabels[Participants.GeminiId]);
    }

    [Fact]
    public async Task 二回目は会話IDを引き継ぎ_相手の返答は添えるが_自分の返答は添えない()
    {
        var (session, claude, gemini, dir) = Make();
        using var _ = dir;

        await session.SendAsync("1つ目", Everyone, _ => { });
        await session.SendAsync("2つ目", Everyone, _ => { });

        Assert.Null(claude.Calls[0].ConversationId);
        Assert.Equal("claude-conv", claude.Calls[1].ConversationId);
        Assert.Equal("gemini-conv", gemini.Calls[1].ConversationId);
        Assert.Contains("【Gemini】Geminiの1回目", claude.Calls[1].Prompt);
        Assert.DoesNotContain("Claudeの1回目", claude.Calls[1].Prompt);
        // Geminiは、Claudeの返答を(1回目か2回目の)どちらかで1度だけ見ている
        Assert.Equal(1, gemini.Calls.Count(c => c.Prompt.Contains("【Claude】Claudeの1回目")));
        Assert.DoesNotContain("【ユーザー】1つ目", claude.Calls[1].Prompt); // 1つ目は前回に見せ済み
    }

    [Fact]
    public async Task 片方だけ宛てなら_もう片方には送らない()
    {
        var (session, claude, gemini, dir) = Make();
        using var _ = dir;

        await session.SendAsync("クローディだけに", new[] { Participants.ClaudeId }, _ => { });

        Assert.Single(claude.Calls);
        Assert.Empty(gemini.Calls);
    }

    [Fact]
    public async Task 失敗したら_エラーとして通知し_未読は次回にまた添える()
    {
        var dir = new TempDir();
        using var _ = dir;
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, n) =>
            n == 1 ? throw new InvalidOperationException("通信できません") : new AgentReply("復活", "c"));
        var session = new ChatSession(new[] { claude }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000);
        var progress = new List<ChatProgress>();

        await session.SendAsync("1つ目", new[] { Participants.ClaudeId }, progress.Add);
        await session.SendAsync("2つ目", new[] { Participants.ClaudeId }, progress.Add);

        var finals = Finals(progress);
        Assert.Equal(ChatProgressKind.Error, finals[0].Kind);
        Assert.Contains("通信できません", finals[0].Text);
        Assert.Equal(ChatProgressKind.Done, finals[1].Kind);
        Assert.Contains("【ユーザー】1つ目", claude.Calls[1].Prompt); // 失敗した分も添え直す
        Assert.Contains("【ユーザー】2つ目", claude.Calls[1].Prompt);
        Assert.DoesNotContain("通信できません", File.ReadAllText(session.LogPath)); // エラー文はログに残さない
    }

    [Fact]
    public async Task 完成は届いた順に通知される()
    {
        var dir = new TempDir();
        using var _ = dir;
        var slow = new SlowAgent(Participants.ClaudeId, "Claude", 300);
        var fast = new SlowAgent(Participants.GeminiId, "Gemini", 0);
        var session = new ChatSession(new IChatAgent[] { slow, fast }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000);
        var order = new List<string>();

        await session.SendAsync("どうぞ", Everyone, p =>
        {
            if (p.Kind == ChatProgressKind.Done) order.Add(p.AgentId);
        });

        Assert.Equal(new[] { Participants.GeminiId, Participants.ClaudeId }, order);
    }

    [Fact]
    public async Task 添付の追加フォルダは後の発言で上書きされず発言ごとに保たれる()
    {
        var dir = new TempDir();
        using var _ = dir;
        var tcs0 = new TaskCompletionSource();
        var recordingAgent = new GateControlledAgent(Participants.ClaudeId, "Claude", tcs0.Task);
        var session = new ChatSession(new IChatAgent[] { recordingAgent }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000);

        // 0つ目の発言: 実行中にして gate を塞いでおく
        var task0 = session.SendAsync("0つ目", new[] { Participants.ClaudeId }, _ => { }, extraDirs: Array.Empty<string>());

        // 1つ目の発言: extraDirs=["C:/dir1"] を指定して送信(gate待ちになる)
        var task1 = session.SendAsync("1つ目", new[] { Participants.ClaudeId }, _ => { }, extraDirs: new[] { "C:/dir1" });

        // 2つ目の発言: 1つ目がgate待ちの間に、extraDirsなし(空)で送信
        var task2 = session.SendAsync("2つ目", new[] { Participants.ClaudeId }, _ => { }, extraDirs: Array.Empty<string>());

        // 0つ目を完了させて、1つ目を動かす
        tcs0.SetResult();
        await Task.WhenAll(task0, task1, task2);

        // 0つ目は空、1つ目は["C:/dir1"]、2つ目は空であるべき
        Assert.Equal(3, recordingAgent.RecordedDirsOnAsk.Count);
        Assert.Empty(recordingAgent.RecordedDirsOnAsk[0]);
        Assert.Equal(new[] { "C:/dir1" }, recordingAgent.RecordedDirsOnAsk[1]);
        Assert.Empty(recordingAgent.RecordedDirsOnAsk[2]);
    }

    private sealed class GateControlledAgent : IChatAgent
    {
        private readonly Task _firstBlock;
        private int _callCount;
        private IReadOnlyList<string> _currentDirs = Array.Empty<string>();

        public GateControlledAgent(string id, string name, Task firstBlock)
        {
            Id = id;
            Name = name;
            _firstBlock = firstBlock;
        }

        public string Id { get; }
        public string Name { get; }
        public string ModelId => "mock";
        public string ModelLabel => "mock";
        public IReadOnlyList<ModelOption> ModelOptions => Array.Empty<ModelOption>();
        public List<IReadOnlyList<string>> RecordedDirsOnAsk { get; } = new();

        public void SetModel(string modelId) { }
        public void SetExtraDirs(IReadOnlyList<string> dirs) => _currentDirs = dirs;
        public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct) => Task.FromResult(ModelOptions);

        public async Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                await _firstBlock;
            }
            RecordedDirsOnAsk.Add(_currentDirs.ToList());
            return new AgentReply("返答", Id);
        }
    }

    /// <summary>決めた時間だけ待ってから答える偽のAI。</summary>
    private sealed class SlowAgent : IChatAgent
    {
        private readonly int _delayMs;

        public SlowAgent(string id, string name, int delayMs)
        {
            Id = id;
            Name = name;
            _delayMs = delayMs;
        }

        public string Id { get; }
        public string Name { get; }
        public string ModelId => "slow";
        public string ModelLabel => "slow";
        public IReadOnlyList<ModelOption> ModelOptions => Array.Empty<ModelOption>();

        public void SetModel(string modelId)
        {
        }

        public void SetExtraDirs(IReadOnlyList<string> dirs)
        {
        }

        public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct) =>
            Task.FromResult(ModelOptions);

        public async Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate,
            CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new AgentReply(Name + "の返答", Id);
        }
    }
}

public class ActivityTextTests
{
    [Theory]
    [InlineData("Read", "C:/Work/memory/AI_MEMORY.md", "AI_MEMORY.md を読んでいます")]
    [InlineData("read_file", @"C:\Work\CLAUDE.md", "CLAUDE.md を読んでいます")]
    [InlineData("Read", null, "ファイルを読んでいます")]
    [InlineData("Edit", "C:/x/a.cs", "a.cs を書き換えています")]
    [InlineData("grep_search", "水耕栽培", "水耕栽培 を探しています")]
    [InlineData("PowerShell", "rm -rf /", "コマンドを実行しています")]
    [InlineData("WebSomething", null, "WebSomething を使っています")]
    public void 道具の名前と対象から_いまの作業の文を作る(string tool, string? target, string expected)
    {
        Assert.Equal(expected, ActivityText.Describe(tool, target));
    }

    [Fact]
    public void 長い対象は切り_コマンドの中身は出さない()
    {
        var text = ActivityText.Describe("grep", new string('あ', 50));
        Assert.StartsWith(new string('あ', 30) + "…", text);
        Assert.DoesNotContain("rm", ActivityText.Describe("Bash", "rm -rf /"));
    }

    [Theory]
    [InlineData("AI_MEMORY.md を読んでいます", "AI_MEMORY.md を読みました")]
    [InlineData("ファイルを読んでいます", "ファイルを読みました")]
    [InlineData("a.cs を書き換えています", "a.cs を書き換えました")]
    [InlineData("水耕栽培 を探しています", "水耕栽培 を探しました")]
    [InlineData("探しています", "探しました")]
    [InlineData("コマンドを実行しています", "コマンドを実行しました")]
    [InlineData("Foo を使っています", "Foo を使いました")]
    [InlineData("", "")]
    public void 作業の文を_終わったあとの過去形にする(string present, string expected)
    {
        Assert.Equal(expected, ActivityText.ToPast(present));
    }

    [Fact]
    public void 引数のJSONから_対象になりそうな文字列を取り出す()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"limit\":1,\"file_path\":\"C:/a/b.md\"}");
        Assert.Equal("C:/a/b.md", ActivityText.FindTarget(doc.RootElement));
        using var none = System.Text.Json.JsonDocument.Parse("{\"limit\":1}");
        Assert.Null(ActivityText.FindTarget(none.RootElement));
    }
}

public class ActivityStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Claudeがファイルを読むと_作業の知らせが出て_結果が戻ると消える()
    {
        var runner = new FakeRunner();
        runner.Lines.Add("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{\"file_path\":\"C:/Work/memory/AI_MEMORY.md\",\"limit\":1}}]}}");
        runner.Lines.Add("{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t\",\"content\":\"...\"}]}}");
        runner.Lines.Add("{\"type\":\"result\",\"is_error\":false,\"result\":\"読みました\",\"session_id\":\"s\"}");
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", Array.Empty<string>()), runner, "w", Timeout);
        var updates = new List<AgentUpdate>();

        await agent.AskAsync("x", null, updates.Add, CancellationToken.None);

        Assert.Equal(new[]
        {
            new AgentUpdate(AgentUpdateKind.Activity, "AI_MEMORY.md を読んでいます"),
            new AgentUpdate(AgentUpdateKind.Activity, "AI_MEMORY.md を読みました"),
        }, updates);
    }

    [Fact]
    public async Task agyがファイルを読むと_作業の知らせが出て_終わると消える()
    {
        var runner = new FakeRunner();
        runner.Lines.Add("{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"ACTIVE\",\"tool_name\":\"read_file\",\"tool_info\":{\"name\":\"read_file\",\"parameters\":{\"Path\":\"C:/Work/memory/AI_MEMORY.md\"}}}}");
        runner.Lines.Add("{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"DONE\",\"tool_name\":\"read_file\",\"tool_info\":{\"output\":\"102 lines\"}}}");
        runner.Lines.Add("{\"event\":\"result\",\"result\":{\"conversation_id\":\"c\",\"status\":\"SUCCESS\",\"response\":\"読みました\"}}");
        var agent = new AgyAgent(new AgySettings("agy", Array.Empty<string>(), ""), runner, "w", Timeout);
        var updates = new List<AgentUpdate>();

        await agent.AskAsync("x", null, updates.Add, CancellationToken.None);

        Assert.Equal(new[]
        {
            new AgentUpdate(AgentUpdateKind.Activity, "AI_MEMORY.md を読んでいます"),
            new AgentUpdate(AgentUpdateKind.Activity, "AI_MEMORY.md を読みました"),
        }, updates);
    }
}

public class ModelSelectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static ClaudeAgent Claude(ClaudeSettings? settings = null) => new(
        settings ?? new ClaudeSettings("claude", "sonnet", Array.Empty<string>()), new FakeRunner(), "w", Timeout);

    [Fact]
    public async Task agyの一覧が取れたら残し_次からは起動直後も取れないときも_表示名つきで使う()
    {
        var path = Path.Combine(Path.GetTempPath(), "namake_models_" + Guid.NewGuid().ToString("N"), "agy-models.json");
        try
        {
            var runner = new FakeRunner();
            runner.Lines.Add("gemini-4-argon-medium\tGemini 4 Argon (Medium)");
            runner.Lines.Add("gemini-3.8-flash-low\tGemini 3.8 Flash (Low)");
            var first = new AgyAgent(new AgySettings("agy", Array.Empty<string>(), "", "gemini-4-argon-medium",
                new[] { "gemini-4-argon-medium" }), runner, "w", Timeout) { ModelCachePath = path };
            Assert.Equal(new[] { "gemini-4-argon-medium" }, first.ModelOptions.Select(o => o.Label)); // まだ残していない
            await first.LoadModelOptionsAsync(CancellationToken.None);

            var failing = new FakeRunner { ThrowAfter = new InvalidOperationException("回線なし") };
            var next = new AgyAgent(new AgySettings("agy", Array.Empty<string>(), "", "z", new[] { "gemini-4-argon-medium" }),
                failing, "w", Timeout) { ModelCachePath = path };
            var expected = new[] { "Gemini 4 Argon (Medium)", "Gemini 3.8 Flash (Low)", "z" };
            Assert.Equal(expected, next.ModelOptions.Select(o => o.Label)); // 起動直後
            Assert.Equal(expected, (await next.LoadModelOptionsAsync(CancellationToken.None)).Select(o => o.Label)); // 取れないとき
        }
        finally
        {
            var dir = Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 残した一覧が壊れていたら_設定にある一覧を使う()
    {
        var path = Path.Combine(Path.GetTempPath(), "namake_models_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{壊れた");
            var agent = new AgyAgent(new AgySettings("agy", Array.Empty<string>(), "", "a", new[] { "a", "b" }),
                new FakeRunner(), "w", Timeout) { ModelCachePath = path };
            Assert.Equal(new[] { "a", "b" }, agent.ModelOptions.Select(o => o.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AgyAgent Agy(FakeRunner runner, string? model = null, IReadOnlyList<string>? models = null) => new(
        new AgySettings("agy", Array.Empty<string>(), "", model, models), runner, "w", Timeout);

    [Fact]
    public void Claudeはモデルを切り替えると_次の引数に反映される_選択肢の既定はsonnet_opus_haiku()
    {
        var agent = Claude();
        Assert.Equal(new[] { "sonnet", "opus", "haiku" }, agent.ModelOptions.Select(o => o.Id));

        agent.SetModel("opus");

        var args = agent.BuildArgs("本文", null).ToList();
        Assert.Equal("opus", args[args.IndexOf("--model") + 1]);
        Assert.Equal("opus", agent.ModelId);
        Assert.Equal("opus", agent.ModelLabel);
    }

    [Fact]
    public void Claudeの選択肢は_設定の一覧と現在のモデルを含み_重複しない()
    {
        var agent = Claude(new ClaudeSettings("claude", "claude-opus-5-5", Array.Empty<string>(), new[] { "sonnet", "sonnet" }));
        Assert.Equal(new[] { "sonnet", "claude-opus-5-5" }, agent.ModelOptions.Select(o => o.Id));
    }

    [Fact]
    public void agyはモデルを切り替えると_次の引数に反映される()
    {
        var agent = Agy(new FakeRunner());
        Assert.DoesNotContain("--model", agent.BuildArgs("本文", null));

        agent.SetModel("gemini-3.8-flash-low");

        var args = agent.BuildArgs("本文", null).ToList();
        Assert.Equal("gemini-3.8-flash-low", args[args.IndexOf("--model") + 1]);
        Assert.Equal("gemini-3.8-flash-low", agent.ModelLabel);
    }

    [Fact]
    public async Task agyの選択肢は_agy_modelsの出力から作る()
    {
        var runner = new FakeRunner();
        runner.Lines.Add("Fetching available models...");
        runner.Lines.Add("gemini-4-argon-medium\tGemini 4 Argon (Medium)");
        runner.Lines.Add("claude-sonnet-5-5-low\tClaude Sonnet 5.5 (Low)");
        var agent = Agy(runner, "gemini-4-argon-medium");

        var options = await agent.LoadModelOptionsAsync(CancellationToken.None);

        Assert.Equal(new[]
        {
            new ModelOption("gemini-4-argon-medium", "Gemini 4 Argon (Medium)"),
            new ModelOption("claude-sonnet-5-5-low", "Claude Sonnet 5.5 (Low)"),
        }, options);
        Assert.Equal(new[] { "models" }, runner.LastArgs);
    }

    [Fact]
    public async Task agyの一覧が取れなければ_設定の一覧を使い_取れた一覧に現在のモデルがなければ足す()
    {
        var failing = new FakeRunner { ThrowAfter = new InvalidOperationException("取れません") };
        var fallback = await Agy(failing, "gemini-4-argon-medium", new[] { "gemini-3.8-flash-low" })
            .LoadModelOptionsAsync(CancellationToken.None);
        Assert.Equal(new[] { "gemini-3.8-flash-low", "gemini-4-argon-medium" }, fallback.Select(o => o.Id));

        var runner = new FakeRunner();
        runner.Lines.Add("a\tA");
        var added = await Agy(runner, "z").LoadModelOptionsAsync(CancellationToken.None);
        Assert.Equal(new[] { "a", "z" }, added.Select(o => o.Id));
    }

    [Fact]
    public void 会議は_AIごとのモデルの切り替えを伝える()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("x", "c"));
        var session = new ChatSession(new[] { claude }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000);

        session.SetModel(Participants.ClaudeId, "opus");

        Assert.Equal("opus", session.ModelIdOf(Participants.ClaudeId));
        Assert.Equal("opus", claude.ModelId);
    }
}

public class AttachmentComposerTests
{
    [Fact]
    public void 添付ファイルとフォルダから_絶対パスつきの表示文とプロンプト用テキストを作る()
    {
        using var dir = new TempDir();
        var filePath = Path.Combine(dir.Path, "sample.txt");
        File.WriteAllText(filePath, "1行目\n2行目");
        var subDir = Path.Combine(dir.Path, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "inside.md"), "# 見出し");

        var items = new[]
        {
            new AttachmentItem(filePath),
            new AttachmentItem(subDir),
        };

        var display = AttachmentComposer.FormatForDisplayAndLog("これを見て", items);
        Assert.Contains("これを見て", display);
        Assert.Contains("[添付ファイル:", display);
        Assert.Contains(items[0].FullPath, display);
        Assert.Contains(items[1].FullPath, display);

        var prompt = AttachmentComposer.FormatForPrompt("これを見て", items);
        Assert.Contains("1行目", prompt);
        Assert.Contains("inside.md", prompt);
    }

    [Fact]
    public void 作業フォルダの外にある添付だけ_追加許可フォルダとして抽出する()
    {
        using var work = new TempDir();
        using var ext = new TempDir();
        var insideFile = Path.Combine(work.Path, "in.txt");
        File.WriteAllText(insideFile, "in");
        var outsideFile = Path.Combine(ext.Path, "out.txt");
        File.WriteAllText(outsideFile, "out");

        var items = new[]
        {
            new AttachmentItem(insideFile),
            new AttachmentItem(outsideFile),
        };

        var dirs = AttachmentComposer.ResolveExternalDirectories(work.Path, items);
        var expectedDir = Path.GetFullPath(ext.Path).Replace('\\', '/').TrimEnd('/');
        Assert.Equal(new[] { expectedDir }, dirs);
    }

    [Fact]
    public void 返答の先頭に出た担当行だけを取り除き_本文の途中は残す()
    {
        Assert.Equal("こんにちは", ReplyTextCleaner.StripLeadingRoleHeader("担当: ツクル（実装担当）\n\nこんにちは"));
        Assert.Equal("こんにちは", ReplyTextCleaner.StripLeadingRoleHeader("担当：ジェミリー\r\nこんにちは"));
        Assert.Equal("", ReplyTextCleaner.StripLeadingRoleHeader("担当: まだ1行目の途中"));
        Assert.Equal("本文です\n担当: ここは残る", ReplyTextCleaner.StripLeadingRoleHeader("本文です\n担当: ここは残る"));
    }
}

public class UsageAndMeetingStoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Claudeのrate_limit_eventから_5時間枠と7日枠の割合とリセット時刻を読み取る()
    {
        var runner = new FakeRunner();
        runner.Lines.Add("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.03,\"resetsAt\":1792893600},\"seven_day\":{\"utilization\":0.82,\"resetsAt\":1792990800}}}}");
        runner.Lines.Add("{\"type\":\"result\",\"is_error\":false,\"result\":\"はい\",\"session_id\":\"s1\"}");
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", Array.Empty<string>()), runner, "w", Timeout);
        var updates = new List<AgentUpdate>();

        await agent.AskAsync("x", null, updates.Add, CancellationToken.None);

        var usageUpdate = Assert.Single(updates, u => u.Kind == AgentUpdateKind.Usage);
        var (shortText, isHigh, tooltip) = UsageQuota.Decode(usageUpdate.Text);
        Assert.Equal("枠: 5時間 3% · 7日 82%", shortText);
        Assert.True(isHigh); // 82% なので80%以上の強調対象
        Assert.Contains("5時間枠: 3%", tooltip);
        Assert.Contains("7日枠: 82%", tooltip);
    }

    [Fact]
    public async Task 会議の発言と会話IDがmeeting_jsonに保存され_開き直すと続きのIDで話せる()
    {
        using var dir = new TempDir();
        var settings = new AppSettings(
            new ClaudeSettings("claude", "sonnet", Array.Empty<string>()),
            new AgySettings("agy", Array.Empty<string>(), ""),
            dir.Path,
            dir.Path,
            4000,
            5);

        var claude1 = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("最初の返答", "conv-123"));
        var gemini1 = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("Gの返答", "g-456"));
        var log1 = new MarkdownLogWriter(dir.Path, new DateTime(2026, 10, 25, 6, 0, 0));
        var session1 = new ChatSession(new[] { claude1, gemini1 }, log1, 4000, workDir: dir.Path);

        await session1.SendAsync("最初の質問です", new[] { Participants.ClaudeId }, _ => { });

        var list = MeetingStore.ListMeetings(dir.Path);
        var summary = Assert.Single(list);
        Assert.True(summary.HasConversationId);
        Assert.Equal("ID引き継ぎ", summary.ModeBadge);
        Assert.Contains("最初の質問です", summary.Title);

        // 開き直す
        var restoredState = MeetingStore.LoadState(summary.FolderPath);
        Assert.Equal(2, restoredState.Entries.Count);
        Assert.Equal("conv-123", restoredState.ConversationIds[Participants.ClaudeId]);

        var claude2 = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("続きの返答", "conv-123"));
        var log2 = new MarkdownLogWriter(summary.FolderPath, restoredState.StartedAt, reopen: true);
        var session2 = new ChatSession(new[] { claude2 }, log2, 4000, restoredState: restoredState);

        await session2.SendAsync("続きの質問です", new[] { Participants.ClaudeId }, _ => { });

        Assert.Equal("conv-123", claude2.Calls[0].ConversationId);
        Assert.DoesNotContain("最初の質問です", claude2.Calls[0].Prompt); // 既読位置も引き継がれている
        Assert.Contains("続きの質問です", claude2.Calls[0].Prompt);
    }

    [Fact]
    public void meeting_jsonがない既存のlog_mdだけでも_発言一覧を復元して一覧に出せる()
    {
        using var dir = new TempDir();
        var log = new MarkdownLogWriter(dir.Path, new DateTime(2026, 10, 24, 15, 30, 0));
        log.Append(new ChatEntry(Participants.UserId, "古い会議の質問", new DateTime(2026, 10, 24, 15, 30, 5), new[] { Participants.GeminiId }));
        log.Append(new ChatEntry(Participants.GeminiId, "古い会議の答え", new DateTime(2026, 10, 24, 15, 30, 12)));

        var list = MeetingStore.ListMeetings(dir.Path);
        var summary = Assert.Single(list);
        Assert.False(summary.HasConversationId);
        Assert.Equal("ログから再開", summary.ModeBadge);
        Assert.Equal(2, summary.MessageCount);

        var restored = MeetingStore.LoadState(summary.FolderPath);
        Assert.Equal(2, restored.Entries.Count);
        Assert.Equal(Participants.UserId, restored.Entries[0].SpeakerId);
        Assert.Equal(new[] { Participants.GeminiId }, restored.Entries[0].TargetIds);
        Assert.Equal(Participants.GeminiId, restored.Entries[1].SpeakerId);
        Assert.Equal("古い会議の答え", restored.Entries[1].Text);
    }

    [Fact]
    public async Task 保存されていた会話IDがCLI側で消えていて失敗したら_過去のログを添えて新しい会話で自動再開する()
    {
        using var dir = new TempDir();
        var restored = new MeetingState
        {
            StartedAt = new DateTime(2026, 10, 24, 12, 0, 0),
            ConversationIds = new Dictionary<string, string> { [Participants.ClaudeId] = "expired-id" },
            SeenPositions = new Dictionary<string, int> { [Participants.ClaudeId] = 2 },
            Entries = new List<ChatEntry>
            {
                new(Participants.UserId, "昨日の大事な前提", new DateTime(2026, 10, 24, 12, 0, 1)),
                new(Participants.ClaudeId, "承知しました", new DateTime(2026, 10, 24, 12, 0, 5)),
            },
        };

        // 1回目(expired-id あり)は失敗し、2回目(conversationId == null)は成功する偽AI
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, convId, _) =>
        {
            if (convId == "expired-id") throw new InvalidOperationException("Session not found");
            return new AgentReply("ログを読んで再開しました", "new-id");
        });
        var log = new MarkdownLogWriter(dir.Path, restored.StartedAt, reopen: true);
        var session = new ChatSession(new[] { claude }, log, 4000, restoredState: restored);
        var progress = new List<ChatProgress>();

        await session.SendAsync("今日の質問", new[] { Participants.ClaudeId }, progress.Add);

        Assert.Equal(2, claude.Calls.Count);
        Assert.Equal("expired-id", claude.Calls[0].ConversationId);
        Assert.Null(claude.Calls[1].ConversationId); // 2回目は新しい会話
        Assert.Contains("昨日の大事な前提", claude.Calls[1].Prompt); // 既読位置がリセットされ、過去のログが添えられた
        Assert.Contains("今日の質問", claude.Calls[1].Prompt);
        Assert.Contains(progress, p => p.Kind == ChatProgressKind.Done && p.Text == "ログを読んで再開しました");
    }

    [Fact]
    public void クリップボードの画像を会議フォルダのattachmentsにPNG保存できる()
    {
        using var dir = new TempDir();
        var logPath = Path.Combine(dir.Path, "log.md");
        var pixels = new byte[] { 40, 120, 60, 255 };
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 4);

        var saved = ClipboardImageHelper.SavePngToMeetingFolder(bitmap, logPath, new DateTime(2026, 10, 25, 8, 15, 0));

        Assert.True(File.Exists(saved));
        Assert.Contains("attachments", saved);
        Assert.EndsWith(".png", saved);
    }

    [Fact]
    public void モデルと労力をsettings_jsonに保存できる()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{\"claude\":{\"executable\":\"claude\",\"model\":\"sonnet\",\"extraArgs\":[]},\"agy\":{\"executable\":\"agy\",\"extraArgs\":[],\"noToolNote\":\"\"},\"workDir\":\"w\",\"meetingsDir\":\"m\",\"maxInjectChars\":4000,\"timeoutSec\":60}");

        SettingsLoader.SaveModelAndEffort(path, "claude", "opus", "high");
        SettingsLoader.SaveModelAndEffort(path, "agy", "gemini-4-argon-medium", "low");

        var loaded = SettingsLoader.Load(path);
        Assert.Equal("opus", loaded.Claude.Model);
        Assert.Equal("high", loaded.Claude.Effort);
        Assert.Equal("gemini-4-argon-medium", loaded.Agy.Model);
        Assert.Equal("low", loaded.Agy.Effort);

        SettingsLoader.SaveTimeout(path, 1200);
        Assert.Equal(1200, SettingsLoader.Load(path).TimeoutSec);
        Assert.Equal("opus", SettingsLoader.Load(path).Claude.Model); // ほかの設定は残る

        SettingsLoader.SaveTheme(path, "dark-forest");
        var loadedWithTheme = SettingsLoader.Load(path);
        Assert.Equal("dark-forest", loadedWithTheme.Theme);

        SettingsLoader.SaveAppearance(path, new AppearanceSettings(true, "山田", "設計AI", "査読AI"));
        var loadedWithAppearance = SettingsLoader.Load(path);
        Assert.NotNull(loadedWithAppearance.Appearance);
        Assert.True(loadedWithAppearance.Appearance!.IsBusinessMode);
        Assert.Equal("山田", loadedWithAppearance.Appearance.UserName);
        Assert.Equal("設計AI", loadedWithAppearance.Appearance.ClaudeName);
        Assert.Equal("査読AI", loadedWithAppearance.Appearance.GeminiName);
    }
}
