using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>「全員」宛ての答える順番(同時／1人目→2人目)と、2人目への頼み書き(WBS 11.22・11.23・11.26)の試験。</summary>
public class AllOrderTests
{
    /// <summary>停止の合図が来るまで答えない偽のAI(停止の試験用)。</summary>
    private sealed class WaitingAgent : IChatAgent
    {
        public WaitingAgent(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public string ModelLabel => "fake-model";
        public string ModelId => "fake-model";
        public IReadOnlyList<ModelOption> ModelOptions { get; } = new[] { new ModelOption("fake-model", "fake-model") };
        public void SetModel(string modelId) { }
        public Task<IReadOnlyList<ModelOption>> LoadModelOptionsAsync(CancellationToken ct) => Task.FromResult(ModelOptions);

        /// <summary>聞かれたらtrueになる。</summary>
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AgentReply> AskAsync(string prompt, string? conversationId, Action<AgentUpdate>? onUpdate, CancellationToken ct)
        {
            Asked.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new AgentReply("届かない", null);
        }
    }

    private static ChatSession CreateSession(TempDir dir, string order, params IChatAgent[] agents) =>
        new(agents, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000, allOrder: order);

    private static readonly string[] Everyone = { Participants.ClaudeId, Participants.GeminiId };

    [Theory]
    [InlineData(AllOrders.Parallel, new[] { "claude", "gemini" })]
    [InlineData(AllOrders.ClaudeFirst, new[] { "claude", "gemini" })]
    [InlineData(AllOrders.GeminiFirst, new[] { "gemini", "claude" })]
    [InlineData("知らない値", new[] { "claude", "gemini" })]
    public void 宛先を_答える順に並べる(string order, string[] expected)
    {
        Assert.Equal(expected, AllOrders.Arrange(order, Everyone));
        Assert.Equal(new[] { Participants.GeminiId }, AllOrders.Arrange(order, new[] { Participants.GeminiId })); // 1人宛ては、そのまま
        Assert.False(AllOrders.IsSequential(order, new[] { Participants.GeminiId }));
    }

    private static readonly string[] Three = { Participants.ClaudeId, Participants.GeminiId, Participants.ChappyId };

    [Fact]
    public void 三人のときは_先の人を先頭にして_残りは元の並び_先の人がいなければ同時()
    {
        Assert.Equal(new[] { "chappy", "claude", "gemini" }, AllOrders.Arrange(AllOrders.ChappyFirst, Three));
        Assert.Equal(new[] { "gemini", "claude", "chappy" }, AllOrders.Arrange(AllOrders.GeminiFirst, Three));
        Assert.False(AllOrders.IsSequential(AllOrders.ChappyFirst, Everyone)); // チャッピーがいなければ、同時
        Assert.Equal(AllOrders.ChappyFirst, AllOrders.Normalize(AllOrders.ChappyFirst));
    }

    [Fact]
    public async Task 三人で先の人を選ぶと_残りの2人は先の人の答えを読んで_意見の頼み書きで答える()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("Cの意見", "c")) { WriteMode = true };
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("Gの意見", "g"));
        var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => new AgentReply("チャッピーの案です", "x"));
        var session = CreateSession(dir, AllOrders.ChappyFirst, claude, gemini, chappy);
        var progress = new List<ChatProgress>();

        await session.SendAsync("案を出して", Three, progress.Add);

        Assert.DoesNotContain("[順番]", chappy.Calls.Single().Prompt); // 先の人には添えない
        foreach (var follower in new[] { claude, gemini })
        {
            var prompt = follower.Calls.Single().Prompt;
            Assert.Contains("チャッピーの案です", prompt); // 先の人の答えを読める
            // あとの人が2人なら、書ける段階(クローディ)でも作業は頼まず、意見を頼む(同時に同じファイルを触らないため)
            Assert.EndsWith(new RelaySettings().ReviewNote.Replace("{first}", "Chappy"), prompt);
        }
        // ※あとの2人が互いの今回の答えを見ないことは、ここでは確かめない(偽のAIはすぐ答えるので、先に聞いた人の答えが、もう1人の本文に入る。
        //   本物のCLIは待ちが入るので、2人とも聞き始めてから答えが届く。「同時」と同じ仕組み)

        // あとの2人の「聞き始めた」は、先の人が答え終わってから届く
        var firstDone = progress.FindIndex(p => p.AgentId == Participants.ChappyId && p.Kind == ChatProgressKind.Done);
        Assert.All(new[] { Participants.ClaudeId, Participants.GeminiId }, id =>
            Assert.True(progress.FindIndex(p => p.AgentId == id && p.Kind == ChatProgressKind.Started) > firstDone));
    }

    [Fact]
    public async Task 三人で先の人が失敗したら_残りの2人には聞かず_2人ともに理由を出す()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("C", "c"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("G", "g"));
        var chappy = new FakeAgent(Participants.ChappyId, "Chappy", (_, _, _) => throw new InvalidOperationException("落ちた"));
        var session = CreateSession(dir, AllOrders.ChappyFirst, claude, gemini, chappy);
        var progress = new List<ChatProgress>();

        await session.SendAsync("案を出して", Three, progress.Add);

        Assert.Empty(claude.Calls);
        Assert.Empty(gemini.Calls);
        Assert.All(new[] { Participants.ClaudeId, Participants.GeminiId }, id =>
            Assert.Contains(progress, p => p.AgentId == id && p.Kind == ChatProgressKind.Error && p.Text.Contains("Chappyが答えられなかった")));
    }

    [Fact]
    public async Task 同時のときは_2人とも頼み書きなしで聞く()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("Cの案", "c"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("Gの案", "g"));
        var session = CreateSession(dir, AllOrders.Parallel, claude, gemini);

        await session.SendAsync("意見をください", Everyone, _ => { });

        Assert.DoesNotContain("[順番]", claude.Calls.Single().Prompt);
        Assert.DoesNotContain("[順番]", gemini.Calls.Single().Prompt);
    }

    [Fact]
    public async Task クローディが先のときは_ジェミリーがクローディの答えと意見の頼み書きを読んで答える()
    {
        using var dir = new TempDir();
        var calls = new List<string>();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) =>
        {
            calls.Add("claude");
            return new AgentReply("Cの案です", "c");
        });
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) =>
        {
            calls.Add("gemini");
            return new AgentReply("Gの意見です", "g");
        });
        var session = CreateSession(dir, AllOrders.ClaudeFirst, claude, gemini);
        var progress = new List<ChatProgress>();

        await session.SendAsync("案を出して", Everyone, progress.Add);

        Assert.Equal(new[] { "claude", "gemini" }, calls);
        var geminiPrompt = gemini.Calls.Single().Prompt;
        Assert.Contains("Cの案です", geminiPrompt); // 1人目の答えを読める
        Assert.Contains("上のClaudeの答えを読んで、あなたの視点でどう思うか", geminiPrompt); // 読むだけなので意見の頼み書き
        Assert.EndsWith(new RelaySettings().ReviewNote.Replace("{first}", "Claude"), geminiPrompt); // 頼み書きは最後に添える
        Assert.DoesNotContain("[順番]", claude.Calls.Single().Prompt); // 1人目には添えない

        // 2人目の「聞き始めた」は、1人目が答え終わってから届く(吹き出しが1人ずつ出る)
        var claudeDone = progress.FindIndex(p => p.AgentId == Participants.ClaudeId && p.Kind == ChatProgressKind.Done);
        var geminiStarted = progress.FindIndex(p => p.AgentId == Participants.GeminiId && p.Kind == ChatProgressKind.Started);
        Assert.True(claudeDone >= 0 && geminiStarted > claudeDone, $"Done={claudeDone} Started={geminiStarted}");

        // 2人目が答えたら止まる(1往復)
        Assert.Single(claude.Calls);
        Assert.Single(gemini.Calls);
    }

    [Fact]
    public async Task 二人目が書き換えできるときは_続きの作業の頼み書きを添える()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("続きをお願い", "c")) { WriteMode = true };
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("実装しました", "g"));
        var session = CreateSession(dir, AllOrders.GeminiFirst, claude, gemini);

        await session.SendAsync("実装して", Everyone, _ => { });

        var claudePrompt = claude.Calls.Single().Prompt;
        Assert.Contains("実装しました", claudePrompt);
        Assert.Contains("その続きの作業をしてください", claudePrompt);
        Assert.Contains("コミットはGeminiに任せてください", claudePrompt);
        Assert.Equal(new RelaySettings().WorkNote.Replace("{first}", "Gemini"), session.RelayNoteFor(Participants.ClaudeId, Participants.GeminiId));
    }

    [Fact]
    public async Task 設定の頼み書きを使い_名前の差し込みも効く()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => new AgentReply("案", "c"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("意見", "g"));
        var session = new ChatSession(new IChatAgent[] { claude, gemini }, new MarkdownLogWriter(dir.Path, DateTime.Now), 4000,
            allOrder: AllOrders.ClaudeFirst, relay: new RelaySettings(ReviewNote: "{first}を査読して"));

        await session.SendAsync("案を出して", Everyone, _ => { });

        Assert.EndsWith("Claudeを査読して", gemini.Calls.Single().Prompt);
    }

    [Fact]
    public async Task 一人目が失敗したら_二人目には聞かない()
    {
        using var dir = new TempDir();
        var claude = new FakeAgent(Participants.ClaudeId, "Claude", (_, _, _) => throw new InvalidOperationException("CLIが落ちた"));
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("呼ばれないはず", "g"));
        var session = CreateSession(dir, AllOrders.ClaudeFirst, claude, gemini);
        var progress = new List<ChatProgress>();

        await session.SendAsync("案を出して", Everyone, progress.Add);

        Assert.Empty(gemini.Calls);
        var skipped = progress.Single(p => p.AgentId == Participants.GeminiId);
        Assert.Equal(ChatProgressKind.Error, skipped.Kind); // 画面の「考え中」を戻すため、2人目にも結果を知らせる
        Assert.Equal("Claudeが答えられなかったので、聞きませんでした。", skipped.Text);
    }

    [Fact]
    public async Task 停止したら_待っている二人目も取り消す()
    {
        using var dir = new TempDir();
        var claude = new WaitingAgent(Participants.ClaudeId, "Claude");
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("呼ばれないはず", "g"));
        var session = CreateSession(dir, AllOrders.ClaudeFirst, claude, gemini);
        var progress = new List<ChatProgress>();

        var sending = session.SendAsync("案を出して", Everyone, p => { lock (progress) progress.Add(p); });
        await claude.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.CancelPending();
        await sending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(gemini.Calls);
        Assert.Equal("回答を停止しました。", progress.Single(p => p.AgentId == Participants.ClaudeId && p.Kind == ChatProgressKind.Error).Text);
        Assert.Equal("回答を停止しました。", progress.Single(p => p.AgentId == Participants.GeminiId).Text);
    }

    [Fact]
    public void 順番を変えると_一度だけ保存を頼み_知らない値は同時にそろえる()
    {
        using var dir = new TempDir();
        var saved = new List<string>();
        var session = new ChatSession(Array.Empty<IChatAgent>(), new MarkdownLogWriter(dir.Path, DateTime.Now), 4000,
            allOrder: "知らない値", onAllOrderChanged: saved.Add);

        Assert.Equal(AllOrders.Parallel, session.AllOrder);
        session.SetAllOrder(AllOrders.GeminiFirst);
        session.SetAllOrder(AllOrders.GeminiFirst); // 同じ値は保存しない
        Assert.Equal(new[] { AllOrders.GeminiFirst }, saved);
    }

    [Fact]
    public void 設定ファイルに順番がなければ同時で_選ぶと順番だけが書き込まれる()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "settings.json"), path);
        var before = SettingsLoader.Load(path);
        Assert.Null(before.AllOrder);
        Assert.Null(before.Relay); // 頼み書きは、書かれていなければ標準の文を使う

        SettingsLoader.SaveAllOrder(path, AllOrders.ClaudeFirst);

        var after = SettingsLoader.Load(path);
        Assert.Equal(AllOrders.ClaudeFirst, after.AllOrder);
        Assert.Equal(before.TimeoutSec, after.TimeoutSec); // ほかの設定は、そのまま
        Assert.Equal(before.Claude.ExtraArgs, after.Claude.ExtraArgs);
    }

    [Fact]
    public void 設定ファイルの頼み書きは_書かれた方だけを差し替える()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings.json")))!.AsObject();
        json["relay"] = new System.Text.Json.Nodes.JsonObject { ["reviewNote"] = "査読だけして" };
        File.WriteAllText(path, json.ToJsonString());

        var relay = SettingsLoader.Load(path).Relay!;
        Assert.Equal("査読だけして", relay.ReviewNote);
        Assert.Equal(new RelaySettings().WorkNote, relay.WorkNote);
    }

    [Fact]
    public void 順番のときは_一人目の仮の吹き出しだけを出し_二人目は番が来てから出す()
    {
        var session = new FakeSession { AllOrder = AllOrders.GeminiFirst };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Everyone, InputText = "案を出して" };

        _ = vm.SendAsync();

        Assert.Equal(new[] { Participants.GeminiId }, vm.Messages.Where(m => m.IsStreaming).Select(m => m.Speaker.Id));
        Assert.True(vm.ClaudeMember.IsThinking); // 2人目も「返答待ち」なので、停止ボタンが効く
        Assert.StartsWith("順番待ち", vm.ClaudeMember.ActivityText);

        session.Push(new ChatProgress(Participants.GeminiId, ChatProgressKind.Done, "Gの案"));
        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Started, ""));

        Assert.Equal(new[] { Participants.ClaudeId }, vm.Messages.Where(m => m.IsStreaming).Select(m => m.Speaker.Id));
        Assert.Equal("", vm.ClaudeMember.ActivityText);
    }

    [Fact]
    public void 同時のときは_ゲストも含めて全員の仮の吹き出しを出す()
    {
        var session = new FakeSession { AllOrder = AllOrders.Parallel };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Everyone, InputText = "意見をください" };

        _ = vm.SendAsync();

        Assert.Equal(3, vm.Messages.Count(m => m.IsStreaming)); // クローディ・ジェミリー・チャッピー
        Assert.Equal("", vm.ClaudeMember.ActivityText);
    }

    [Fact]
    public void 順番と段階の組み合わせで_何が起きるかの一言が切り替わる()
    {
        var session = new FakeSession { AllOrder = AllOrders.Parallel };
        var vm = new MainViewModel(() => session, defaultParticipants: new[] { Participants.ClaudeId, Participants.GeminiId });
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Contains("独立した意見", vm.AllOrderHint);

        vm.SelectedAllOrder = AllOrders.ClaudeFirst; // 2人とも読むだけ
        Assert.Equal("▶ 案と意見: クローディ(読む)が答え → ジェミリー(読む)がそれを読んで、どう思うかを言います", vm.AllOrderHint);

        raised.Clear();
        vm.ClaudeMember.SelectedLevelId = PermissionLevelOption.PartialId; // 1人目が書ける
        Assert.Contains(nameof(MainViewModel.AllOrderHint), raised); // 段階を変えると、画面の一言も出し直す
        Assert.Equal("▶ 作ってから確認: クローディ(読み書き)が作業し → ジェミリー(読む)がその結果を見て、意見を言います", vm.AllOrderHint);

        vm.SelectedAllOrder = AllOrders.GeminiFirst; // 2人目が書ける
        Assert.StartsWith("▶ 考えてから作る: ジェミリー(読む)が調べて案を出し → クローディ(読み書き)が", vm.AllOrderHint);

        vm.GeminiMember.SelectedLevelId = PermissionLevelOption.PartialId; // 2人とも書ける
        Assert.StartsWith("▶ 作業のリレー:", vm.AllOrderHint);
        Assert.Contains("コミットはジェミリーだけ", vm.AllOrderHint);
    }

    [Fact]
    public void 三人のときは_先の人のあとに残りの2人が同時に意見を言う会議風の一言になる()
    {
        var session = new FakeSession { AllOrder = AllOrders.ChappyFirst };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Everyone };

        Assert.Equal("チャッピーが先に答え、クローディ・ジェミリーがそれを読んで、同時に答えます(あとの人は作業はせず、意見だけ)",
            vm.AllOrderHint);

        vm.ChappyMember.IsPresent = false; // 先の人が抜けたら、「同時」に戻して保存する
        Assert.Equal(AllOrders.Parallel, vm.SelectedAllOrder);
        Assert.Equal(new[] { AllOrders.Parallel }, session.AllOrderChanges);
        Assert.Equal("クローディ・ジェミリーが同時に、別々に答えます(互いの今回の答えは見ない＝独立した意見)", vm.AllOrderHint);
        Assert.DoesNotContain(vm.AllOrderOptions, o => o.Id == AllOrders.ChappyFirst); // 抜けた人の「〇〇が先」は出さない
    }

    [Fact]
    public void 参加を変えても_先の人が残っていれば順番はそのままで_選択欄に出し直す()
    {
        var session = new FakeSession { AllOrder = AllOrders.ClaudeFirst };
        var vm = new MainViewModel(() => session);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ChappyMember.IsPresent = false;

        Assert.Equal(AllOrders.ClaudeFirst, vm.SelectedAllOrder);
        Assert.Empty(session.AllOrderChanges);
        // 選択肢を作り直したあとに今の値を知らせ直す(知らせないと、選択欄が空白のままになる)
        Assert.True(raised.LastIndexOf(nameof(MainViewModel.SelectedAllOrder)) > raised.LastIndexOf(nameof(MainViewModel.AllOrderOptions)));
    }

    [Fact]
    public void 見た目を変えて選択肢を作り直しても_選択欄に今の順番を出し直す()
    {
        var session = new FakeSession { AllOrder = AllOrders.GeminiFirst };
        var vm = new MainViewModel(() => session);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsBusinessMode = !vm.IsBusinessMode; // 名前の変更と同じく、選択肢を作り直す

        Assert.Equal(AllOrders.GeminiFirst, vm.SelectedAllOrder);
        Assert.True(raised.LastIndexOf(nameof(MainViewModel.SelectedAllOrder)) > raised.LastIndexOf(nameof(MainViewModel.AllOrderOptions)));
    }

    [Fact]
    public void 新しい会議に切り替えると_その会議の順番で判定して同時に戻す()
    {
        var first = new FakeSession { AllOrder = AllOrders.Parallel };
        var second = new FakeSession { AllOrder = AllOrders.ClaudeFirst };
        var sessions = new Queue<FakeSession>(new[] { first, second });
        var vm = new MainViewModel(() => sessions.Dequeue(),
            defaultParticipants: new[] { Participants.GeminiId, Participants.ChappyId });

        vm.StartNewMeeting(); // 前の会議の値(同時)ではなく、新しい会議の値(クローディが先)で判定する

        Assert.Equal(AllOrders.Parallel, vm.SelectedAllOrder);
        Assert.Equal(new[] { AllOrders.Parallel }, second.AllOrderChanges);
        Assert.Empty(first.AllOrderChanges);
    }

    [Fact]
    public void 先の人がいない会議を開くと_順番は同時になる()
    {
        var session = new FakeSession { AllOrder = AllOrders.ClaudeFirst };
        var vm = new MainViewModel(() => session, defaultParticipants: new[] { Participants.GeminiId, Participants.ChappyId });

        Assert.Equal(AllOrders.Parallel, vm.SelectedAllOrder);
        Assert.Equal(AllOrders.Parallel, session.AllOrder);
    }

    [Fact]
    public void 順番の選択欄は_会議の値で始まり_選ぶと会議に伝わり_空は無視する()
    {
        var session = new FakeSession { AllOrder = AllOrders.ClaudeFirst };
        var vm = new MainViewModel(() => session);

        Assert.Equal(AllOrders.ClaudeFirst, vm.SelectedAllOrder);
        Assert.Equal(new[] { "同時", "クローディが先", "ジェミリーが先", "チャッピーが先" }, vm.AllOrderOptions.Select(o => o.Label));

        vm.SelectedAllOrder = AllOrders.GeminiFirst;
        vm.SelectedAllOrder = null!; // 選択肢を作り直したときに届く空
        Assert.Equal(AllOrders.GeminiFirst, vm.SelectedAllOrder);
        Assert.Equal(new[] { AllOrders.GeminiFirst }, session.AllOrderChanges);
    }
}
