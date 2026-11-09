using System.ComponentModel;
using System.IO;
using System.Text.Json;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>ゲストのチャッピー(Codex CLI)の試験。</summary>
public class ChappyGuestTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // codex exec --json の出力の形(公開の資料に基づく。本物での確認は、インストール後に行う)
    private static string Line(object o) => JsonSerializer.Serialize(o);
    private static readonly string Started = Line(new { type = "thread.started", thread_id = "t-123" });
    private static readonly string Message = Line(new { type = "item.completed", item = new { id = "item_1", type = "agent_message", text = "ペンギンです" } });
    private static readonly string Completed = Line(new { type = "turn.completed", usage = new { input_tokens = 10, output_tokens = 3 } });

    [Fact]
    public async Task 答えと会話IDを読み取り_本文は標準入力で渡す()
    {
        var runner = new FakeRunner { Lines = { Started, Message, Completed } };
        var agent = new CodexAgent(new CodexSettings(), runner, "w", Timeout);
        var deltas = new List<string>();

        var reply = await agent.AskAsync("あなたの動物は?", null,
            u => { if (u.Kind == AgentUpdateKind.Delta) deltas.Add(u.Text); }, CancellationToken.None);

        Assert.Equal("ペンギンです", reply.Text);
        Assert.Equal("t-123", reply.ConversationId);
        Assert.Equal(new[] { "ペンギンです" }, deltas);
        Assert.Equal(new[] { "exec", "--json", "-c", "sandbox_mode=read-only", "--skip-git-repo-check", "-" }, runner.LastArgs);
        Assert.StartsWith("[状態] あなたは、この会議の参加者(チャッピー)です。いまは「読む」", runner.LastInput);
        Assert.EndsWith("あなたの動物は?", runner.LastInput);
    }

    [Fact]
    public void 続きの会話は_会話IDで_resumeする()
    {
        var agent = new CodexAgent(new CodexSettings(Model: "gpt-5"), new FakeRunner(), "w", Timeout);
        var args = agent.BuildArgs("t-123");
        Assert.Equal(new[] { "exec", "resume", "t-123", "--json", "--model", "gpt-5" }, args.Take(6));
        Assert.Equal("-", args[^1]);
        // resume は --sandbox を受け付けない(unexpected argument で終わる)ので、権限は -c で渡す
        Assert.DoesNotContain("--sandbox", args);
        Assert.Contains("sandbox_mode=read-only", args);
    }

    [Fact]
    public void 古い設定の_sandbox_は_resumeでも通る_c_sandbox_mode_に直る()
    {
        var settings = new CodexSettings(ExtraArgs: new[] { "--sandbox", "read-only", "--skip-git-repo-check" });
        var agent = new CodexAgent(settings, new FakeRunner(), "w", Timeout);
        foreach (var conversationId in new string?[] { null, "t-1" })
        {
            var args = agent.BuildArgs(conversationId);
            Assert.DoesNotContain("--sandbox", args);
            var i = args.ToList().IndexOf("-c");
            Assert.Equal("sandbox_mode=read-only", args[i + 1]);
        }
    }

    [Fact]
    public void チャッピーは_段階を持てて_既定は読むだけ()
    {
        var agent = new CodexAgent(new CodexSettings(), new FakeRunner(), "w", Timeout);
        Assert.True(agent.CanWrite);
        Assert.True(agent.CanFull);
        Assert.False(agent.WriteMode);
        Assert.False(agent.FullMode);
        Assert.False(((IChatAgent)agent).CanRunCommands); // 「コマンドも使う」の段は、チャッピーにはない(一部できるでも、作業フォルダの中のコマンドは使える)
        Assert.Contains("読む", agent.HarnessSummary);
    }

    [Fact]
    public void 一部できるは_作業フォルダの中を書き換える引数と添え書きに切り替わる()
    {
        var agent = new CodexAgent(new CodexSettings(), new FakeRunner(), "w", Timeout) { WriteMode = true };
        foreach (var conversationId in new string?[] { null, "t-1" })
        {
            var args = agent.BuildArgs(conversationId);
            Assert.Contains("sandbox_mode=workspace-write", args);
            Assert.DoesNotContain("--sandbox", args);
        }
        Assert.StartsWith("[状態] あなたは、この会議の参加者(チャッピー)です。いまは「読み書き」", agent.BuildInput("本文"));
        Assert.Contains("読み書き", agent.HarnessSummary);
        Assert.False(string.IsNullOrWhiteSpace(agent.WriterWarning));
    }

    [Fact]
    public void 何でもできるは_書く担当のときだけ有効で_制限なしの引数と添え書きになる()
    {
        var agent = new CodexAgent(new CodexSettings(), new FakeRunner(), "w", Timeout);

        agent.FullMode = true; // 書く担当でないので、無視される
        Assert.False(agent.FullMode);

        agent.WriteMode = true;
        agent.FullMode = true;
        Assert.True(agent.FullMode);
        Assert.Contains("sandbox_mode=danger-full-access", agent.BuildArgs(null));
        Assert.StartsWith("[状態] あなたは、この会議の参加者(チャッピー)です。いまは「全部」", agent.BuildInput("本文"));
        Assert.Contains("全部", agent.HarnessSummary);
        Assert.False(string.IsNullOrWhiteSpace(agent.FullWarning));

        agent.WriteMode = false; // 書く担当をやめたら、何でもできるも外れて、読むだけに戻る
        Assert.False(agent.FullMode);
        Assert.Contains("sandbox_mode=read-only", agent.BuildArgs(null));
        Assert.StartsWith("[状態] あなたは、この会議の参加者(チャッピー)です。いまは「読む」", agent.BuildInput("本文"));
    }

    [Fact]
    public void 段階ごとの引数と添え書きは_設定で変えられる()
    {
        var settings = new CodexSettings(
            WriterExtraArgs: new[] { "-c", "sandbox_mode=workspace-write", "--skip-git-repo-check", "--color", "never" },
            FullExtraArgs: new[] { "--dangerously-bypass-approvals-and-sandbox" },
            WriterNote: "【書き換え中】", FullNote: "【全部OK】");
        var agent = new CodexAgent(settings, new FakeRunner(), "w", Timeout) { WriteMode = true };
        Assert.Contains("never", agent.BuildArgs(null));
        Assert.StartsWith("【書き換え中】", agent.BuildInput("本文"));

        agent.FullMode = true;
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", agent.BuildArgs(null));
        Assert.DoesNotContain("-c", agent.BuildArgs(null));
        Assert.StartsWith("【全部OK】", agent.BuildInput("本文"));
    }

    [Fact]
    public async Task カードの段階を選ぶと_チャッピーへの次の発言から権限が切り替わる()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        using var dir = new TempDir();
        var runner = new FakeRunner { Lines = { Started, Message, Completed } };
        var session = new ChatSessionFactory(settings with { MeetingsDir = dir.Path }, runner).Create();
        var vm = new MainViewModel(() => session);

        var chappy = vm.ChappyMember;
        Assert.True(chappy.CanWrite);
        Assert.True(chappy.CanFull);
        Assert.Equal(new[] { "read", "partial", "full" }, chappy.LevelOptions.Select(o => o.Id));
        Assert.True(chappy.CanChooseModel); // モデルも選べる

        vm.TargetChappy = true;
        vm.InputText = "読むだけで";
        await vm.SendAsync();
        Assert.Contains("sandbox_mode=read-only", runner.LastArgs);

        chappy.SelectedLevelId = "partial";
        vm.InputText = "書き換えて";
        await vm.SendAsync();
        Assert.Contains("sandbox_mode=workspace-write", runner.LastArgs);
        Assert.StartsWith("[状態] あなたは、この会議の参加者(チャッピー)です。いまは「読み書き」", runner.LastInput);

        chappy.SelectedLevelId = "full";
        vm.InputText = "何でも";
        await vm.SendAsync();
        Assert.Contains("sandbox_mode=danger-full-access", runner.LastArgs);

        chappy.SelectedLevelId = "read";
        vm.InputText = "戻す";
        await vm.SendAsync();
        Assert.Contains("sandbox_mode=read-only", runner.LastArgs);
    }

    [Fact]
    public async Task 失敗の知らせが来たら_失敗にする()
    {
        var failed = Line(new { type = "turn.failed", error = new { message = "ログインしてください" } });
        var runner = new FakeRunner { Lines = { Started, failed } };
        var agent = new CodexAgent(new CodexSettings(), runner, "w", Timeout);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync("本文", null, null, CancellationToken.None));
        Assert.Contains("ログインしてください", ex.Message);
    }

    [Fact]
    public async Task Codexが入っていなければ_入れ方の分かる失敗にする()
    {
        var runner = new FakeRunner { ThrowOnStart = new Win32Exception(2) };
        var agent = new CodexAgent(new CodexSettings(), runner, "w", Timeout);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync("本文", null, null, CancellationToken.None));
        Assert.Contains("codex login", ex.Message);
    }

    [Fact]
    public void チャッピーは基本の参加者で_ペンギンの絵を出す_モデルの欄と歯車も出す()
    {
        var member = new MemberViewModel(Participants.Chappy, null, null, null, null, null);
        Assert.False(Participants.Chappy.IsGuest);
        Assert.True(Participants.Chappy.HasControls);
        Assert.True(Participants.Chappy.HasAvatar);
        Assert.EndsWith("/Assets/Avatars/leader-chappy.jpg", Participants.Chappy.AvatarUri);
        Assert.Equal("ChatGPT", Participants.Chappy.SubTitle);
        Assert.True(member.CanChoosePresence);
        Assert.True(member.ShowImageAvatar);
        Assert.False(member.ShowInitialAvatar);
        Assert.Equal("チ", member.InitialText); // 簡易表示のときの頭文字
        Assert.True(member.CanChooseModel);
        Assert.True(member.CanEditHarness);
    }

    // codex debug models の出力(必要な項目だけ)。visibility が hide のものは出さない
    private const string ModelCatalog =
        "{\"models\":[" +
        "{\"slug\":\"gpt-reserve\",\"display_name\":\"GPT-Reserve\",\"visibility\":\"hide\",\"priority\":1}," +
        "{\"slug\":\"gpt-5.6-luna\",\"display_name\":\"GPT-5.6-Luna\",\"visibility\":\"list\",\"priority\":4}," +
        "{\"slug\":\"gpt-6-luna\",\"display_name\":\"GPT-6-Luna\",\"visibility\":\"list\",\"priority\":2}," +
        "{\"slug\":\"codex-auto-review\",\"display_name\":\"Codex Auto Review\",\"visibility\":\"hide\",\"priority\":5}]}";

    [Fact]
    public void モデル一覧は_画面に出す扱いのものだけを_並び順で読む()
    {
        Assert.Equal(new[] { ("gpt-6-luna", "GPT-6-Luna"), ("gpt-5.6-luna", "GPT-5.6-Luna") },
            CodexAgent.ParseModelList(ModelCatalog).Select(o => (o.Id, o.Label)));
        Assert.Empty(CodexAgent.ParseModelList("読めない文字"));
        Assert.Empty(CodexAgent.ParseModelList("{\"other\":1}"));
    }

    [Fact]
    public async Task モデル一覧は_codex_debug_modelsで取り_先頭はCodexの既定で_取れた一覧を残す()
    {
        using var dir = new TempDir();
        var cache = System.IO.Path.Combine(dir.Path, "codex-models.json");
        var runner = new FakeRunner { Lines = { ModelCatalog } };
        var agent = new CodexAgent(new CodexSettings(), runner, "w", Timeout) { ModelCachePath = cache };

        var options = await agent.LoadModelOptionsAsync(CancellationToken.None);

        Assert.Equal(new[] { "debug", "models" }, runner.LastArgs);
        Assert.Equal(new[] { "", "gpt-6-luna", "gpt-5.6-luna" }, options.Select(o => o.Id));
        Assert.Equal("Codexの既定", options[0].Label);
        // 次の起動では、残した一覧から出す(CLIに聞く前でも、表示名つきで並ぶ)
        var next = new CodexAgent(new CodexSettings(), new FakeRunner(), "w", Timeout) { ModelCachePath = cache };
        Assert.Equal(new[] { "", "gpt-6-luna", "gpt-5.6-luna" }, next.ModelOptions.Select(o => o.Id));
    }

    [Fact]
    public async Task モデル一覧が取れないときは_いまの一覧のまま_選んだモデルは起動の引数に入る()
    {
        var runner = new FakeRunner { ThrowAfter = new InvalidOperationException("ネットなし") };
        var agent = new CodexAgent(new CodexSettings(Model: "gpt-x"), runner, "w", Timeout);

        var options = await agent.LoadModelOptionsAsync(CancellationToken.None);
        Assert.Equal(new[] { "", "gpt-x" }, options.Select(o => o.Id)); // 既定と、いま選んでいるモデル

        agent.SetModel("gpt-6-luna");
        Assert.Equal(new[] { "--model", "gpt-6-luna" }, agent.BuildArgs(null).SkipWhile(a => a != "--model").Take(2));
        agent.SetModel("");
        Assert.DoesNotContain("--model", agent.BuildArgs(null)); // 「Codexの既定」なら、モデルを渡さない
        Assert.Equal("Codexの既定", agent.ModelLabel);
    }

    [Fact]
    public void 強度を選べて_選んだ強度は_model_reasoning_effortで渡す_空なら渡さない()
    {
        var agent = new CodexAgent(new CodexSettings(Effort: "high"), new FakeRunner(), "w", Timeout);
        Assert.True(agent.SupportsEffort);
        Assert.Equal("high", agent.EffortId);
        var args = agent.BuildArgs("t-1"); // 続きの会話(resume)でも渡す
        Assert.Equal(new[] { "-c", "model_reasoning_effort=high" },
            args.SkipWhile(a => a != "model_reasoning_effort=high").Prepend("-c").Take(2));
        Assert.Contains("model_reasoning_effort=high", args);

        agent.SetEffort("");
        Assert.DoesNotContain(agent.BuildArgs(null), a => a.StartsWith("model_reasoning_effort", StringComparison.Ordinal));
    }

    [Fact]
    public void ビジネスモードでは_チャッピーをChatGPTと出し_頭文字はGP()
    {
        var business = new AppearanceSettings(IsBusinessMode: true);
        Assert.Equal("ChatGPT", business.ResolveParticipantName(Participants.ChappyId));
        Assert.Equal("チャッピー", AppearanceSettings.Default.ResolveParticipantName(Participants.ChappyId));
        Assert.Equal("GP", AppearanceSettings.ResolveInitial(Participants.ChappyId, "ChatGPT"));
    }

    [Fact]
    public async Task 全員は_同時でも先の人を選んでも_チャッピーも入る()
    {
        var session = new FakeSession { AllOrder = AllOrders.Parallel };
        var vm = new MainViewModel(() => session) { InputText = "みんな", TargetEveryone = true };
        var sending = vm.SendAsync();
        Assert.Equal(new[] { Participants.ClaudeId, Participants.GeminiId, Participants.ChappyId }, session.Sent[^1].Targets);
        Assert.StartsWith("クローディ・ジェミリー・チャッピーが同時に", vm.AllOrderHint);
        session.Finish(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "はい"));
        await sending;

        vm.SelectedAllOrder = AllOrders.ClaudeFirst;
        vm.InputText = "順番で";
        var inTurn = vm.SendAsync();
        Assert.Equal(new[] { Participants.ClaudeId, Participants.GeminiId, Participants.ChappyId }, session.Sent[^1].Targets);
        session.Finish(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "はい"));
        await inTurn;
    }

    [Fact]
    public async Task 宛先チャッピーは_チャッピーだけに送る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "こんにちは", TargetChappy = true };
        var sending = vm.SendAsync();
        session.Finish(new ChatProgress(Participants.ChappyId, ChatProgressKind.Done, "よろしく"));
        await sending;

        Assert.Equal(new[] { Participants.ChappyId }, session.Sent[^1].Targets);
        // 吹き出しは、まとめて見る画面と、チャッピーの列に出す(クローディ・ジェミリーの列には入れない)
        Assert.Contains(vm.Messages, m => m.Speaker.Id == Participants.ChappyId);
        Assert.Contains(vm.ChappyMessages, m => m.Speaker.Id == Participants.ChappyId);
        Assert.Contains(vm.ChappyMessages, m => m.IsUser && m.Text == "こんにちは");
        Assert.DoesNotContain(vm.ClaudeMessages.Concat(vm.GeminiMessages), m => m.Speaker.Id == Participants.ChappyId);
        Assert.DoesNotContain(vm.ClaudeMessages.Concat(vm.GeminiMessages), m => m.IsUser && m.Text == "こんにちは");
    }
}
