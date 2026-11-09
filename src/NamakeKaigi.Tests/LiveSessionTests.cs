using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>本物のCLI(claude・agy)をつないだ確認。環境変数 NAMAKE_LIVE=1 のときだけ動く(時間と使用量がかかるため)。</summary>
public class LiveSessionTests
{
    [Fact]
    public async Task 本物のCLIで全員宛ての1往復ができる()
    {
        if (Environment.GetEnvironmentVariable("NAMAKE_LIVE") != "1") return;

        using var dir = new TempDir();
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"))
            with { MeetingsDir = dir.Path };
        var session = new ChatSessionFactory(settings, new ProcessRunner()).Create();
        var progress = new List<ChatProgress>();

        await session.SendAsync("水耕栽培で育てる野菜を、一言で1つ提案して。",
            new[] { Participants.ClaudeId, Participants.GeminiId }, progress.Add);
        var results = progress.Where(p => p.Kind is ChatProgressKind.Done or ChatProgressKind.Error).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Kind == ChatProgressKind.Done, r.Text));
        Assert.Contains(progress, p => p.Kind == ChatProgressKind.Delta); // 流れてきた文字も届く
        Assert.Contains(progress, p => p.Kind == ChatProgressKind.Model && p.AgentId == Participants.ClaudeId);
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Text)));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("gemini")]
    public async Task 本物のCLIで_起動したまま2回話しかけると_起動は1回で2回とも答え_前の話を覚えている(string who)
    {
        if (Environment.GetEnvironmentVariable("NAMAKE_LIVE") != "1") return;

        using var dir = new TempDir();
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        var timeout = TimeSpan.FromSeconds(180);
        IChatAgent agent = who == "claude"
            ? new ClaudeAgent(settings.Claude, new ProcessRunner(), dir.Path, timeout)
            : new AgyAgent(settings.Agy, new ProcessRunner(), dir.Path, timeout);
        agent.SetModel(who == "claude" ? "haiku" : "gemini-3.8-flash-low");
        try
        {
            var first = await agent.AskAsync("合言葉は「すだち」です。覚えて、了解とだけ答えて。", null, null, CancellationToken.None);
            var started = DateTime.Now;
            var second = await agent.AskAsync("合言葉は何でしたか。合言葉だけ答えて。", first.ConversationId, null, CancellationToken.None);
            var secondSec = (DateTime.Now - started).TotalSeconds;

            var kept = agent is ClaudeAgent c ? c.Kept : ((AgyAgent)agent).Kept;
            Assert.Equal(1, kept.StartCount);
            Assert.True(kept.IsRunning);
            Assert.Contains("すだち", second.Text);
            Console.WriteLine($"{who}: 2回目 {secondSec:0.0}秒");
        }
        finally
        {
            agent.Shutdown();
        }
    }

    [Fact]
    public async Task 本物のCLIで_一部できるの許可リストの外のコマンドは_承認を求めてから実行される()
    {
        if (Environment.GetEnvironmentVariable("NAMAKE_LIVE") != "1") return;

        using var dir = new TempDir();
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        var agent = new ClaudeAgent(settings.Claude, new ProcessRunner(), dir.Path, TimeSpan.FromSeconds(180))
            { WriteMode = true };
        agent.CommandMode = true;
        agent.SetModel("haiku");
        var asked = new List<ToolApproval>();

        var reply = await agent.AskAsync("PowerShellで New-Item -ItemType File -Name approval_test.txt を実行して、結果を一言で教えてください。", null,
            update =>
            {
                if (update.Kind != AgentUpdateKind.Approval) return;
                asked.Add(update.Approval!);
                update.Approval!.Answer(true);
            }, CancellationToken.None);

        Assert.True(asked.Count == 1, $"問い合わせが{asked.Count}件。返答: {reply.Text}");
        Assert.Equal("PowerShell", asked[0].ToolName);
        Assert.Contains("approval_test.txt", asked[0].Detail);
        Assert.True(File.Exists(Path.Combine(dir.Path, "approval_test.txt")), reply.Text); // 許可したので、実行された
    }
}
