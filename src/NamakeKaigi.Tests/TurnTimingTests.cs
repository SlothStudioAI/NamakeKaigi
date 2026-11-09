using System.IO;
using System.Text.Json;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>発言ごとの時間の記録(WBS 11.51)の試験。</summary>
public class TurnTimingTests
{
    private static List<JsonElement> ReadTimings(MarkdownLogWriter log)
    {
        var path = Path.Combine(Path.GetDirectoryName(log.StatePath)!, TurnTimingLog.FileName);
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList()
            : new List<JsonElement>();
    }

    [Fact]
    public async Task 発言ごとに_時間と使い回しの別が_timing_jsonlに1行ずつ残る()
    {
        using var dir = new TempDir();
        var log = new MarkdownLogWriter(dir.Path, DateTime.Now);
        var claude = new FakeAgent("claude", "Claude", (_, _, _) => new AgentReply("こんにちは", "c1"))
        {
            Updates = new[] { new AgentUpdate(AgentUpdateKind.Delta, "こん") },
            LastTurnReusedCli = true,
        };
        var session = new ChatSession(new IChatAgent[] { claude }, log, 4000);

        await session.SendAsync("やあ", new[] { "claude" }, _ => { });

        var row = Assert.Single(ReadTimings(log));
        Assert.Equal("claude", row.GetProperty("Who").GetString());
        Assert.Equal("ok", row.GetProperty("Outcome").GetString());
        Assert.True(row.GetProperty("ReusedCli").GetBoolean());
        Assert.Equal(5, row.GetProperty("ReplyChars").GetInt32());
        Assert.True(row.GetProperty("FirstTextMs").GetInt64() >= 0); // 最初の文字が届いた
        Assert.True(row.GetProperty("TotalMs").GetInt64() >= row.GetProperty("FirstTextMs").GetInt64());
    }

    [Fact]
    public async Task 失敗した発言も_errorとして残る()
    {
        using var dir = new TempDir();
        var log = new MarkdownLogWriter(dir.Path, DateTime.Now);
        var gemini = new FakeAgent("gemini", "Gemini", (_, _, _) => throw new InvalidOperationException("失敗"));
        var session = new ChatSession(new IChatAgent[] { gemini }, log, 4000);

        await session.SendAsync("やあ", new[] { "gemini" }, _ => { });

        var row = Assert.Single(ReadTimings(log));
        Assert.Equal("error", row.GetProperty("Outcome").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("ReusedCli").ValueKind);
        Assert.Equal(-1, row.GetProperty("FirstTextMs").GetInt64()); // 最初の文字は届かなかった
    }

    [Fact]
    public void フォルダがなければ_何もせず_例外も出さない()
    {
        TurnTimingLog.Append(Path.Combine(Path.GetTempPath(), "namake_no_such_" + Guid.NewGuid().ToString("N")),
            new TurnTiming(DateTime.Now, "claude", "ok", 0, 0, 0, 0, null, 0));
    }

    [Fact]
    public async Task クローディは_起動したCLIを使い回したか新しく起動したかを返す()
    {
        var runner = new KeepAliveTests.SessionRunner(_ => new[]
        {
            JsonSerializer.Serialize(new { type = "result", subtype = "success", is_error = false, result = "はい", session_id = "s1" }),
        });
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", new[] { "--tools", "Read" }, null, new[] { "--tools", "Read,Edit" }),
            runner, "w", TimeSpan.FromSeconds(30));
        Assert.Null(agent.LastTurnReusedCli); // まだ聞いていない

        var first = await agent.AskAsync("1回目", null, null, CancellationToken.None);
        Assert.False(agent.LastTurnReusedCli); // 初めては、新しく起動した

        await agent.AskAsync("2回目", first.ConversationId, null, CancellationToken.None);
        Assert.True(agent.LastTurnReusedCli); // 同じ条件・同じ会話は、使い回し

        agent.SetModel("opus");
        await agent.AskAsync("3回目", first.ConversationId, null, CancellationToken.None);
        Assert.False(agent.LastTurnReusedCli); // 条件が変わったので、起動し直した
    }

    [Fact]
    public async Task 起動し直した理由が_最初_条件の変更_会話の変更の順に記録される()
    {
        var runner = new KeepAliveTests.SessionRunner(_ => new[]
        {
            JsonSerializer.Serialize(new { type = "result", subtype = "success", is_error = false, result = "はい", session_id = "s1" }),
        });
        var agent = new ClaudeAgent(new ClaudeSettings("claude", "sonnet", new[] { "--tools", "Read" }, null, new[] { "--tools", "Read,Edit" }),
            runner, "w", TimeSpan.FromSeconds(30));
        Assert.Null(agent.LastTurnRestartReason);

        var first = await agent.AskAsync("1回目", null, null, CancellationToken.None);
        Assert.Equal("first", agent.LastTurnRestartReason);

        await agent.AskAsync("2回目", first.ConversationId, null, CancellationToken.None);
        Assert.Null(agent.LastTurnRestartReason); // 使い回し

        agent.SetModel("opus");
        await agent.AskAsync("3回目", first.ConversationId, null, CancellationToken.None);
        Assert.Equal("key", agent.LastTurnRestartReason);

        await agent.AskAsync("4回目", "別の会話", null, CancellationToken.None);
        Assert.Equal("conversation", agent.LastTurnRestartReason);
    }

    [Fact]
    public void 起動し直した理由が_記録の行に出る()
    {
        var dir = Path.Combine(Path.GetTempPath(), "namake_timing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            TurnTimingLog.Append(dir, new TurnTiming(DateTime.Now, "gemini", "ok", 0, 0, 0, 0, false, 0, "dead"));
            var row = JsonDocument.Parse(File.ReadAllLines(Path.Combine(dir, TurnTimingLog.FileName))[0]).RootElement;
            Assert.Equal("dead", row.GetProperty("RestartReason").GetString());
        }
        finally { Directory.Delete(dir, true); }
    }
}
