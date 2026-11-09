using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>CLIを起動したままにする(11.2①)の試験。</summary>
public class KeepAliveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>起動したままのプロセスの偽物。発言を受け取ると、決めた答えの行を出す。</summary>
    internal sealed class FakeProcess : IProcessSession
    {
        private readonly Channel<string?> _out = Channel.CreateUnbounded<string?>();
        private readonly Func<string, IEnumerable<string?>> _respond;

        public FakeProcess(IReadOnlyList<string> args, Func<string, IEnumerable<string?>> respond)
        {
            Args = args;
            _respond = respond;
        }

        public IReadOnlyList<string> Args { get; }
        public List<string> Inputs { get; } = new();
        public bool Disposed { get; private set; }
        public bool IsAlive => !Disposed;
        public string RecentErrors => "偽のエラー";

        public void Send(string line)
        {
            Inputs.Add(line);
            foreach (var l in _respond(line)) _out.Writer.TryWrite(l);
        }

        public async Task<string?> ReadLineAsync(CancellationToken ct) => await _out.Reader.ReadAsync(ct);

        public void Dispose() => Disposed = true;
    }

    /// <summary>起動したままのプロセスに対応した偽の実行役。起動のたびに FakeProcess を作って記録する。</summary>
    internal sealed class SessionRunner(Func<string, IEnumerable<string?>> respond) : IProcessRunner
    {
        public List<FakeProcess> Started { get; } = new();
        public bool SupportsSessions => true;

        public IProcessSession StartSession(string fileName, IReadOnlyList<string> args, string workDir)
        {
            var p = new FakeProcess(args, respond);
            Started.Add(p);
            return p;
        }

        public Task RunLinesAsync(string fileName, IReadOnlyList<string> args, string workDir, TimeSpan timeout,
            Action<string> onLine, CancellationToken ct) => throw new InvalidOperationException("起動したままのはずなのに、毎回起動した");
    }

    private static string ClaudeResult(string text, string session = "s1") =>
        JsonSerializer.Serialize(new { type = "result", subtype = "success", is_error = false, result = text, session_id = session });

    private static string AgyResult(string text, string conv = "c1") =>
        JsonSerializer.Serialize(new { @event = "result", result = new { conversation_id = conv, status = "SUCCESS", response = text } });

    private static ClaudeAgent Claude(IProcessRunner runner) =>
        new(new ClaudeSettings("claude", "sonnet", new[] { "--tools", "Read" }, null, new[] { "--tools", "Read,Edit" }),
            runner, "w", Timeout);

    [Fact]
    public async Task クローディは_同じ会話の2回目の発言を_起動したままのCLIに送る()
    {
        var runner = new SessionRunner(_ => new[] { ClaudeResult("はい") });
        var agent = Claude(runner);

        var first = await agent.AskAsync("1回目", null, null, CancellationToken.None);
        await agent.AskAsync("2回目", first.ConversationId, null, CancellationToken.None);

        var process = Assert.Single(runner.Started);
        Assert.Equal(2, process.Inputs.Count);
        Assert.Contains("2回目", process.Inputs[1]);
        Assert.DoesNotContain("--resume", process.Args); // 新しい会話として起動した
        Assert.DoesNotContain(process.Args, a => a.Contains("1回目")); // 本文は引数に入れない
        Assert.False(process.Disposed);
    }

    [Fact]
    public async Task 起動の条件が変わったら_起動し直して会話を続ける()
    {
        var runner = new SessionRunner(_ => new[] { ClaudeResult("はい", "s1") });
        var agent = Claude(runner);
        var first = await agent.AskAsync("1回目", null, null, CancellationToken.None);

        agent.SetModel("opus");
        await agent.AskAsync("2回目", first.ConversationId, null, CancellationToken.None);

        Assert.Equal(2, runner.Started.Count);
        Assert.True(runner.Started[0].Disposed); // 古いCLIは終わらせる
        var args = runner.Started[1].Args.ToList();
        Assert.Equal("opus", args[args.IndexOf("--model") + 1]);
        Assert.Equal("s1", args[args.IndexOf("--resume") + 1]); // 会話IDで続きから
    }

    [Fact]
    public async Task 続ける会話が変わったら_起動し直す()
    {
        var runner = new SessionRunner(_ => new[] { ClaudeResult("はい", "s1") });
        var agent = Claude(runner);
        await agent.AskAsync("1回目", null, null, CancellationToken.None);

        await agent.AskAsync("新しい会話", null, null, CancellationToken.None); // 会話IDを外した(会話が消えていたときの保険など)

        Assert.Equal(2, runner.Started.Count);
        Assert.DoesNotContain("--resume", runner.Started[1].Args);
    }

    [Fact]
    public async Task 停止したら_CLIを終わらせて_次は起動し直す()
    {
        var runner = new SessionRunner(_ => Array.Empty<string>()); // 答えが届かない
        var agent = Claude(runner);
        using var cts = new CancellationTokenSource();

        var asking = agent.AskAsync("長い作業", null, null, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);

        Assert.True(runner.Started[0].Disposed);
        Assert.False(agent.Kept.IsRunning);
    }

    [Fact]
    public async Task 答えの途中でCLIが終わったら_失敗にして_次は起動し直す()
    {
        var runner = new SessionRunner(_ => new string?[] { null });
        var agent = Claude(runner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync("本文", null, null, CancellationToken.None));
        Assert.Contains("途中で終わりました", ex.Message);
        Assert.True(runner.Started[0].Disposed);
    }

    [Fact]
    public async Task 時間切れなら_CLIを終わらせて時間切れの失敗にする()
    {
        var runner = new SessionRunner(_ => Array.Empty<string>());
        var agent = Claude(runner);
        agent.SetTimeout(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => agent.AskAsync("本文", null, null, CancellationToken.None));
        Assert.True(runner.Started[0].Disposed);
    }

    [Fact]
    public async Task 一部できるでは_起動したままでも承認の問い合わせに答えられる()
    {
        const string request =
            "{\"type\":\"control_request\",\"request_id\":\"r1\",\"request\":{\"subtype\":\"can_use_tool\"," +
            "\"tool_name\":\"PowerShell\",\"input\":{\"command\":\"git status\"}}}";
        var runner = new SessionRunner(input => input.Contains("control_response")
            ? new[] { ClaudeResult("済みました") }
            : new[] { request });
        var settings = new ClaudeSettings("claude", "sonnet", new[] { "--tools", "Read" }, null, new[] { "--tools", "Read,Edit" },
            null, new[] { "--tools", "Read,Edit,PowerShell" });
        var agent = new ClaudeAgent(settings, runner, "w", Timeout) { WriteMode = true };
        agent.CommandMode = true;

        var reply = await agent.AskAsync("git status して", null, u => u.Approval?.Answer(true), CancellationToken.None);

        Assert.Equal("済みました", reply.Text);
        var process = Assert.Single(runner.Started);
        Assert.Contains("--permission-prompt-tool", process.Args);
        Assert.Contains("\"allow\"", process.Inputs[1]);
    }

    [Fact]
    public async Task 読むだけでは_許可の問い合わせを受ける指定を付けない()
    {
        var runner = new SessionRunner(_ => new[] { ClaudeResult("はい") });
        await Claude(runner).AskAsync("本文", null, null, CancellationToken.None);
        Assert.DoesNotContain("--permission-prompt-tool", runner.Started[0].Args);
    }

    [Fact]
    public async Task ジェミリーは_本文を空にして起動し_発言はeventの形で送る()
    {
        var runner = new SessionRunner(_ => new[] { AgyResult("リンゴです") });
        var agent = new AgyAgent(new AgySettings("agy", new[] { "--mode", "plan" }, "[読むだけ]"), runner, "w", Timeout);

        var first = await agent.AskAsync("好きな果物は?", null, null, CancellationToken.None);
        await agent.AskAsync("もう一度", first.ConversationId, null, CancellationToken.None);

        var process = Assert.Single(runner.Started);
        Assert.Equal("-p=", process.Args[0]);
        Assert.Equal("stream-json", process.Args[process.Args.ToList().IndexOf("--input-format") + 1]);
        using var doc = JsonDocument.Parse(process.Inputs[0]);
        Assert.Equal("user", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal("[読むだけ]好きな果物は?", doc.RootElement.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal("リンゴです", first.Text);
    }

    [Fact]
    public async Task 会議を閉じると_起動したままのCLIを終わらせる()
    {
        var runner = new SessionRunner(_ => new[] { ClaudeResult("はい") });
        var agent = Claude(runner);
        await agent.AskAsync("本文", null, null, CancellationToken.None);

        agent.Shutdown();

        Assert.True(runner.Started[0].Disposed);
    }

    [Fact]
    public async Task 設定でOFFにしたら_今までどおり発言ごとに起動する()
    {
        var runner = new FakeRunner { Lines = { ClaudeResult("はい") } };
        var agent = Claude(runner);
        agent.KeepAlive = false;
        var reply = await agent.AskAsync("本文", null, null, CancellationToken.None);
        Assert.Equal("はい", reply.Text);
        Assert.Contains(runner.LastArgs, a => a.Contains("本文"));
    }

    [Fact]
    public void 設定ファイルにkeepAliveがなければ_起動したままにする()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "settings.json");
        Assert.True(SettingsLoader.Load(path).KeepAlive);
    }
}
