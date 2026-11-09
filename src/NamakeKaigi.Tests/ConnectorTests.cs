using System.IO;
using System.Windows.Controls;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>外部連携(MCP)を、連携ごとにON/OFFする仕組みの試験。</summary>
public class ConnectorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // 10/26 にこのPCで実際に出た `claude mcp list` の出力
    private static readonly string[] ListOutput =
    {
        "Checking MCP server health…",
        "",
        "claude.ai Claude Docs: https://api.anthropic.com/v1/pages/mcp - ✔ Connected",
        "claude.ai Google Calendar: https://calendarmcp.googleapis.com/mcp/v1 - ✔ Connected",
        "claude.ai Gmail: https://gmailmcp.googleapis.com/mcp/v1 - ✔ Connected",
        "claude.ai Google Drive: https://drivemcp.googleapis.com/mcp/v1 - ✔ Connected",
    };

    private const string ResultLine =
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"はい\",\"session_id\":\"s1\"}";

    /// <summary>`mcp list` と、ふつうの発言で、違う出力を返す偽のプロセス実行。</summary>
    private sealed class ScriptRunner : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public bool FailList { get; set; }

        public Task RunLinesAsync(string fileName, IReadOnlyList<string> args, string workDir,
            TimeSpan timeout, Action<string> onLine, CancellationToken ct)
        {
            Calls.Add(args);
            if (args is ["mcp", "list"])
            {
                if (FailList) return Task.FromException(new TimeoutException("時間切れ"));
                foreach (var line in ListOutput) onLine(line);
            }
            else
            {
                onLine(ResultLine);
            }
            return Task.CompletedTask;
        }

        // 「一部できる」は、標準入力でやりとりする形で起動する
        public Task RunConversationAsync(string fileName, IReadOnlyList<string> args, string workDir,
            TimeSpan timeout, string firstInput, Func<string, IProcessChannel, Task> onLine, CancellationToken ct)
        {
            Calls.Add(args);
            return onLine(ResultLine, new FakeRunner().AsChannel());
        }
    }

    private static ClaudeAgent FullAgent(ScriptRunner runner, IReadOnlyList<string>? enabled, int cacheSec = 300)
    {
        var settings = new ClaudeSettings("claude", "sonnet", new[] { "--tools", "Read" }, null,
            new[] { "--tools", "Read,Edit" }, null, null, null, new[] { "--permission-mode", "auto" }, "[状態]何でもできる",
            enabled, cacheSec);
        var agent = new ClaudeAgent(settings, runner, "w", Timeout) { WriteMode = true };
        agent.FullMode = true;
        return agent;
    }

    [Fact]
    public void 一覧の出力から_連携の名前だけを取り出す()
    {
        Assert.Equal(
            new[] { "claude.ai Claude Docs", "claude.ai Google Calendar", "claude.ai Gmail", "claude.ai Google Drive" },
            ConnectorCatalog.ParseList(ListOutput));
        Assert.Empty(ConnectorCatalog.ParseList(new[] { "No MCP servers configured. Use `claude mcp add` to add a server." }));
    }

    [Theory]
    [InlineData("claude.ai Gmail", "mcp__claude_ai_Gmail")]
    [InlineData("claude.ai Google Calendar", "mcp__claude_ai_Google_Calendar")]
    [InlineData("my-server", "mcp__my-server")]
    public void 連携の名前は_道具の名前の頭に直す(string name, string prefix)
    {
        Assert.Equal(prefix, ConnectorCatalog.ToolPrefix(name));
    }

    [Fact]
    public void ONが1つもないか_一覧が分からないときは_連携をすべて読み込まない()
    {
        Assert.Equal(new[] { "--strict-mcp-config" }, ConnectorCatalog.BuildArgs(null, ConnectorCatalog.ParseList(ListOutput)));
        Assert.Equal(new[] { "--strict-mcp-config" }, ConnectorCatalog.BuildArgs(new[] { "claude.ai Gmail" }, null));
    }

    [Fact]
    public void ONにしていない連携だけを止め_新しく増えた連携もOFFになる()
    {
        var known = ConnectorCatalog.ParseList(ListOutput).Append("claude.ai Slack").ToList();
        var args = ConnectorCatalog.BuildArgs(new[] { "claude.ai Google Calendar" }, known);

        Assert.Equal("--disallowedTools", args[0]);
        Assert.Equal(new[] { "mcp__claude_ai_Claude_Docs", "mcp__claude_ai_Gmail", "mcp__claude_ai_Google_Drive", "mcp__claude_ai_Slack" },
            args.Skip(1));
        Assert.Empty(ConnectorCatalog.BuildArgs(known, known)); // 全部ONなら、何も足さない
    }

    [Fact]
    public async Task 何でもできるで連携をONにしていると_一覧を取ってOFFの連携を止める()
    {
        var runner = new ScriptRunner();
        var agent = FullAgent(runner, new[] { "claude.ai Gmail" });

        await agent.AskAsync("1回目", null, null, CancellationToken.None);
        await agent.AskAsync("2回目", "s1", null, CancellationToken.None);

        // 一覧は1回だけ取り、2回目は使い回す
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(new[] { "mcp", "list" }, runner.Calls[0]);
        var args = runner.Calls[1].ToList();
        var i = args.IndexOf("--disallowedTools");
        Assert.True(i > 0);
        Assert.Contains("mcp__claude_ai_Google_Drive", args.Skip(i));
        Assert.DoesNotContain("mcp__claude_ai_Gmail", args);
        Assert.DoesNotContain("--strict-mcp-config", args);
        Assert.Contains("外部連携は claude.ai Gmail だけが使えます", args[1]); // AIにも、使える連携を伝える
    }

    [Fact]
    public async Task 一覧を取れなかったら_連携をすべて止めて答える()
    {
        var runner = new ScriptRunner { FailList = true };
        var agent = FullAgent(runner, new[] { "claude.ai Gmail" });

        var reply = await agent.AskAsync("本文", null, null, CancellationToken.None);

        Assert.Equal("はい", reply.Text);
        Assert.Contains("--strict-mcp-config", runner.Calls[^1]);
    }

    [Fact]
    public async Task 連携をONにしていない_または何でもできるでないときは_一覧を取らない()
    {
        var runner = new ScriptRunner();
        var agent = FullAgent(runner, null);
        await agent.AskAsync("本文", null, null, CancellationToken.None);
        Assert.Single(runner.Calls);

        var partial = FullAgent(runner, new[] { "claude.ai Gmail" });
        partial.FullMode = false;
        await partial.AskAsync("本文", null, null, CancellationToken.None);
        Assert.Equal(2, runner.Calls.Count);
        Assert.DoesNotContain("--disallowedTools", runner.Calls[^1]);
    }

    [Fact]
    public void 設定の画面で_チェックした連携だけがONで保存され_見つからない連携も残る()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "settings.json"), path);
        var settings = SettingsLoader.Load(path);
        var current = settings.Claude with { EnabledConnectors = new[] { "claude.ai Gmail", "消えた連携" } };

        UiThread.Run(() =>
        {
            var dlg = new HarnessWindow(current, _ => { });
            dlg.ShowConnectors(ConnectorCatalog.ParseList(ListOutput));
            var boxes = ((WrapPanel)dlg.FindName("ConnectorsCheckPanel")).Children.OfType<CheckBox>().ToList();

            Assert.Equal(5, boxes.Count); // 一覧の4つ＋見つからない1つ
            Assert.Equal("消えた連携 (いまは見つかりません)", boxes[^1].Content);
            Assert.True(boxes.Single(b => (string)b.Tag == "claude.ai Gmail").IsChecked);

            boxes.Single(b => (string)b.Tag == "claude.ai Gmail").IsChecked = false;
            boxes.Single(b => (string)b.Tag == "claude.ai Google Calendar").IsChecked = true;
            SettingsLoader.SaveClaudeHarness(path, dlg.BuildEditedClaudeSettings());

            // 一覧が取れなかった画面で保存しても、ONの連携は今のまま
            var failed = new HarnessWindow(current, _ => { });
            failed.ShowConnectors(null);
            Assert.Equal(current.EnabledConnectors, failed.BuildEditedClaudeSettings().EnabledConnectors);
        });

        Assert.Equal(new[] { "claude.ai Google Calendar", "消えた連携" }, SettingsLoader.Load(path).Claude.EnabledConnectors);
    }
}
