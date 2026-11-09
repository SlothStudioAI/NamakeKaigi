using System.Text.Json;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>「一部できる」の承認ボタン(許可リストの外の操作を、ユーザーの承認で通す)の試験。</summary>
public class ApprovalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly string[] ReadOnly = { "--tools", "Read" };
    private static readonly string[] Writable = { "--tools", "Read,Edit", "--permission-mode", "acceptEdits" };
    private static readonly string[] Cmd = { "--tools", "Read,Edit,PowerShell", "--allowedTools", "PowerShell(dotnet build*)" };
    private static readonly string[] Full = { "--permission-mode", "auto" };

    // 実際のCLIの出力(2026-10-26 実測)を元にした、許可の問い合わせの行
    private const string ToolRequestLine =
        "{\"type\":\"control_request\",\"request_id\":\"r1\",\"request\":{\"subtype\":\"can_use_tool\"," +
        "\"tool_name\":\"PowerShell\",\"input\":{\"command\":\"git status\",\"description\":\"状態を見る\"}," +
        "\"description\":\"git status\",\"tool_use_id\":\"t1\"}}";

    private const string ResultLine =
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"済みました\",\"session_id\":\"s1\"}";

    private static ClaudeAgent Agent(FakeRunner runner, bool commands = true)
    {
        var settings = new ClaudeSettings("claude", "sonnet", ReadOnly, null, Writable, "[状態]読むだけ", Cmd, null,
            Full, "[状態]何でもできる");
        var agent = new ClaudeAgent(settings, runner, "w", Timeout) { WriteMode = true };
        agent.CommandMode = commands;
        return agent;
    }

    /// <summary>送られた答えの行から、behavior と updatedInput を取り出す。</summary>
    private static (string Behavior, JsonElement Response) ParseAnswer(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var outer = doc.RootElement.GetProperty("response");
        Assert.Equal("control_response", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("r1", outer.GetProperty("request_id").GetString());
        var inner = outer.GetProperty("response").Clone();
        return (inner.GetProperty("behavior").GetString()!, inner);
    }

    [Fact]
    public void 許可の問い合わせから_道具の名前とコマンドの文を読み取る()
    {
        var request = ClaudeControl.TryParseToolRequest(ToolRequestLine);
        Assert.NotNull(request);
        Assert.Equal("r1", request!.RequestId);
        Assert.Equal("PowerShell", request.ToolName);
        Assert.Equal("git status", request.Detail);
        Assert.Null(ClaudeControl.TryParseToolRequest(ResultLine));
    }

    [Fact]
    public void 許可の答えには元の引数をそのまま付け_拒否の答えには理由を付ける()
    {
        var request = ClaudeControl.TryParseToolRequest(ToolRequestLine)!;

        var (allow, allowBody) = ParseAnswer(ClaudeControl.BuildToolResponse(request, true));
        Assert.Equal("allow", allow);
        Assert.Equal("git status", allowBody.GetProperty("updatedInput").GetProperty("command").GetString());

        var (deny, denyBody) = ParseAnswer(ClaudeControl.BuildToolResponse(request, false));
        Assert.Equal("deny", deny);
        Assert.False(string.IsNullOrEmpty(denyBody.GetProperty("message").GetString()));
    }

    [Fact]
    public void 発言は日本語のまま1行のJSONで送る()
    {
        var line = ClaudeControl.BuildUserMessage("1行目\n「こんにちは」");
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("user", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("1行目\n「こんにちは」", doc.RootElement.GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task 一部できるでは_本文を標準入力で送り_問い合わせを受ける引数で起動する()
    {
        var runner = new FakeRunner { Lines = { ResultLine } };
        var agent = Agent(runner);

        var reply = await agent.AskAsync("本文です", null, null, CancellationToken.None);

        Assert.Equal("済みました", reply.Text);
        Assert.Contains("--permission-prompt-tool", runner.LastArgs);
        Assert.Equal("stdio", runner.LastArgs[runner.LastArgs.ToList().IndexOf("--permission-prompt-tool") + 1]);
        Assert.Equal("stream-json", runner.LastArgs[runner.LastArgs.ToList().IndexOf("--input-format") + 1]);
        Assert.DoesNotContain(runner.LastArgs, a => a.Contains("本文です")); // 本文は引数に入れない
        Assert.Contains("本文です", runner.FirstInput);
        Assert.Contains("[状態] いまは「読み書き」", runner.FirstInput); // 段階の知らせも、本文と一緒に送る
        Assert.True(runner.InputClosed); // 結果が届いたら入力を閉じて、CLIを終わらせる
    }

    [Fact]
    public async Task 読むだけと何でもできるは_今までどおり本文を引数で渡す()
    {
        var runner = new FakeRunner { Lines = { ResultLine } };
        var agent = Agent(runner);
        agent.WriteMode = false;
        await agent.AskAsync("読むだけの本文", null, null, CancellationToken.None);
        Assert.Contains(runner.LastArgs, a => a.Contains("読むだけの本文"));
        Assert.Null(runner.FirstInput);

        var full = Agent(runner);
        full.FullMode = true;
        await full.AskAsync("何でもの本文", null, null, CancellationToken.None);
        Assert.Contains(runner.LastArgs, a => a.Contains("何でもの本文"));
        Assert.DoesNotContain("--permission-prompt-tool", runner.LastArgs);
    }

    [Theory]
    [InlineData(true, "allow")]
    [InlineData(false, "deny")]
    public async Task 問い合わせが届くと画面に知らせ_ユーザーの答えをCLIへ返す(bool userAllows, string expected)
    {
        var runner = new FakeRunner { Lines = { ToolRequestLine, ResultLine } };
        var agent = Agent(runner);
        ToolApproval? asked = null;

        await agent.AskAsync("git status して", null, update =>
        {
            if (update.Kind != AgentUpdateKind.Approval) return;
            asked = update.Approval;
            asked!.Answer(userAllows);
        }, CancellationToken.None);

        Assert.NotNull(asked);
        Assert.Equal("PowerShell: git status", asked!.Text);
        Assert.Equal(1, runner.PauseCount); // 答えを待つ間は、タイムアウトを止める
        Assert.Equal(expected, ParseAnswer(Assert.Single(runner.Sent)).Behavior);
    }

    [Fact]
    public async Task 知らせる先がないときは_問い合わせを断る()
    {
        var runner = new FakeRunner { Lines = { ToolRequestLine, ResultLine } };
        await Agent(runner).AskAsync("本文", null, null, CancellationToken.None);
        Assert.Equal("deny", ParseAnswer(Assert.Single(runner.Sent)).Behavior);
    }

    [Fact]
    public async Task 答えを待っている間に停止したら_キャンセルになる()
    {
        var runner = new FakeRunner { Lines = { ToolRequestLine, ResultLine } };
        using var cts = new CancellationTokenSource();
        var asking = Agent(runner).AskAsync("本文", null, update =>
        {
            if (update.Kind == AgentUpdateKind.Approval) cts.Cancel();
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        Assert.Empty(runner.Sent);
    }

    [Fact]
    public async Task 対応していない種類の問い合わせには_断りを返す()
    {
        const string other = "{\"type\":\"control_request\",\"request_id\":\"r9\",\"request\":{\"subtype\":\"hook_callback\"}}";
        var runner = new FakeRunner { Lines = { other, ResultLine } };
        await Agent(runner).AskAsync("本文", null, _ => { }, CancellationToken.None);

        using var doc = JsonDocument.Parse(Assert.Single(runner.Sent));
        var response = doc.RootElement.GetProperty("response");
        Assert.Equal("error", response.GetProperty("subtype").GetString());
        Assert.Equal("r9", response.GetProperty("request_id").GetString());
    }

    [Fact]
    public async Task 画面では返答中の吹き出しにボタンが出て_押すと答えが返り_ボタンが消える()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "git status して", TargetClaude = true };
        var sending = vm.SendAsync();
        var bubble = vm.Messages[1];
        var approval = new ToolApproval("PowerShell", "git status");

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Approval, approval.Text, approval));
        Assert.True(bubble.HasApproval);
        Assert.Equal("PowerShell: git status", bubble.ApprovalText);
        Assert.Equal("⏳ 承認を待っています…", bubble.StatusLine);

        bubble.AllowCommand.Execute(null);
        Assert.True(await approval.Result);
        Assert.False(bubble.HasApproval);

        session.Finish(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "済みました"));
        await sending;
    }

    [Fact]
    public async Task 答える前に失敗したら_問い合わせは拒否で閉じてボタンも消える()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "git status して", TargetClaude = true };
        var sending = vm.SendAsync();
        var bubble = vm.Messages[1];
        var approval = new ToolApproval("PowerShell", "git status");

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Approval, approval.Text, approval));
        session.Finish(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Error, "回答を停止しました。"));
        await sending;

        Assert.False(await approval.Result);
        Assert.False(bubble.HasApproval);
    }
}
