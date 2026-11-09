using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>会議の参加者を選ぶ機能(1on1・ゲストなしで始める)の試験。</summary>
public class ParticipantSelectionTests
{
    /// <summary>入力欄の発言を1件送って、指定したAIの返答を完成させる。</summary>
    private static async Task SendOneAsync(MainViewModel vm, FakeSession session, string answererId)
    {
        var sending = vm.SendAsync();
        session.Finish(new ChatProgress(answererId, ChatProgressKind.Done, "はい"));
        await sending;
    }

    [Fact]
    public void 既定は全員が参加していて_全員のボタンも出る()
    {
        var vm = new MainViewModel(() => new FakeSession());

        Assert.True(vm.ClaudePresent && vm.GeminiPresent && vm.ChappyPresent);
        Assert.True(vm.ShowEveryoneTarget);
        Assert.True(vm.ShowAllOrderChoice);
        Assert.Equal(ChatTarget.Everyone, vm.Target);
        Assert.All(vm.Members.Where(m => m.CanChoosePresence), m => Assert.True(m.CanChangePresence));
        Assert.False(vm.Members.First(m => m.Participant.Id == Participants.UserId).CanChoosePresence); // ユーザー本人には欄を出さない
    }

    [Fact]
    public async Task 設定で1人だけ選ぶと_1on1で始まり_その人だけに送る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session, defaultParticipants: new[] { Participants.ClaudeId });

        Assert.True(vm.ClaudePresent);
        Assert.False(vm.GeminiPresent);
        Assert.False(vm.ChappyPresent);
        Assert.False(vm.ShowEveryoneTarget);
        Assert.Equal(ChatTarget.Claude, vm.Target);
        Assert.Equal("不参加", vm.GeminiMember.StatusText);

        vm.InputText = "相談です";
        await SendOneAsync(vm, session, Participants.ClaudeId);
        Assert.Equal(new[] { Participants.ClaudeId }, session.Sent[^1].Targets);
    }

    [Fact]
    public void 知らない名前や空の設定は_全員として扱う()
    {
        Assert.True(new MainViewModel(() => new FakeSession(), defaultParticipants: new[] { "unknown" }).GeminiPresent);
        Assert.True(new MainViewModel(() => new FakeSession(), defaultParticipants: Array.Empty<string>()).ChappyPresent);
        var vm = new MainViewModel(() => new FakeSession(), defaultParticipants: new[] { "GEMINI", " chappy " });
        Assert.False(vm.ClaudePresent);
        Assert.True(vm.GeminiPresent && vm.ChappyPresent);
    }

    [Fact]
    public async Task 参加を外した人は_全員の宛先から外れる()
    {
        var session = new FakeSession { AllOrder = AllOrders.Parallel };
        var vm = new MainViewModel(() => session);

        vm.GeminiMember.IsPresent = false;

        Assert.Equal(ChatTarget.Everyone, vm.Target);
        Assert.False(vm.GeminiPresent);
        Assert.True(vm.ShowAllOrderChoice); // 参加しているAIが2人以上なら、順番を選べる(チャッピーを含む2人でも)
        Assert.StartsWith("クローディ・チャッピーが同時に", vm.AllOrderHint);
        vm.InputText = "みんな";
        await SendOneAsync(vm, session, Participants.ClaudeId);
        Assert.Equal(new[] { Participants.ClaudeId, Participants.ChappyId }, session.Sent[^1].Targets);
    }

    [Fact]
    public void 参加しているAIが0人のとき_説明は主語なしにならず_参加を促す()
    {
        // AIのCLIが1つも入っていない(最後の1人は外せないので、0人になるのはこの場合だけ)
        var vm = new MainViewModel(() => new FakeSession(), installedAis: System.Array.Empty<string>());

        Assert.Contains("参加しているAIがいません", vm.AllOrderHint);
        Assert.DoesNotContain("同時に", vm.AllOrderHint);
    }

    [Fact]
    public void ユーザーのアバターは_ナマケのアイコン()
    {
        Assert.EndsWith("/Assets/namake_icon.png", Participants.User.AvatarUri);
    }

    [Fact]
    public void 宛先の人が抜けたら_宛先は全員に戻る()
    {
        var vm = new MainViewModel(() => new FakeSession()) { Target = ChatTarget.Gemini };

        vm.GeminiMember.IsPresent = false;

        Assert.Equal(ChatTarget.Everyone, vm.Target);
    }

    [Fact]
    public void AIが1人になると_宛先はその人になり_全員のボタンは消える()
    {
        var vm = new MainViewModel(() => new FakeSession());

        vm.GeminiMember.IsPresent = false;
        vm.ChappyMember.IsPresent = false;

        Assert.Equal(ChatTarget.Claude, vm.Target);
        Assert.False(vm.ShowEveryoneTarget);
        Assert.Equal("クローディだけが参加しています(1on1)", vm.AllOrderHint);

        vm.ChappyMember.IsPresent = true; // また2人になれば、全員のボタンが戻る
        Assert.True(vm.ShowEveryoneTarget);
    }

    [Fact]
    public void 先の人のほかに1人だけなら_2人の順番の一言になる()
    {
        var session = new FakeSession { AllOrder = AllOrders.ClaudeFirst };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Everyone };

        vm.GeminiMember.IsPresent = false;

        Assert.Equal("▶ 案と意見: クローディ(読む)が答え → チャッピー(読む)がそれを読んで、どう思うかを言います", vm.AllOrderHint);
    }

    [Fact]
    public void チャッピーを含む2人でも_全員のときは順番の選択欄を出す()
    {
        var vm = new MainViewModel(() => new FakeSession(),
            defaultParticipants: new[] { Participants.ClaudeId, Participants.ChappyId }) { Target = ChatTarget.Everyone };

        Assert.True(vm.ShowAllOrderChoice); // 順番が効くのに、変える欄がない、を防ぐ
        Assert.Equal(new[] { "同時", "クローディが先", "チャッピーが先" }, vm.AllOrderOptions.Select(o => o.Label));
    }

    [Fact]
    public void 最後の1人は外せない()
    {
        var vm = new MainViewModel(() => new FakeSession(), defaultParticipants: new[] { Participants.GeminiId });

        Assert.False(vm.GeminiMember.CanChangePresence);
        Assert.Contains("最後の1人", vm.GeminiMember.PresenceTooltip);
        vm.GeminiMember.IsPresent = false;

        Assert.True(vm.GeminiMember.IsPresent);
        Assert.True(vm.GeminiPresent);
    }

    [Fact]
    public async Task 会議が始まったら_参加者は変えられない()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "はじめます", Target = ChatTarget.Claude };
        await SendOneAsync(vm, session, Participants.ClaudeId);

        Assert.All(vm.Members.Where(m => m.CanChoosePresence), m => Assert.False(m.CanChangePresence));
        vm.GeminiMember.IsPresent = false; // 外そうとしても、元に戻る
        Assert.True(vm.GeminiMember.IsPresent);
        vm.ClaudeMember.SetPresentSilently(false);
        vm.ClaudeMember.IsPresent = true; // 足そうとしても、元に戻る
        Assert.False(vm.ClaudeMember.IsPresent);
    }

    [Fact]
    public async Task 新しい会議では_参加者が既定に戻り_選び直せる()
    {
        var first = new FakeSession();
        var second = new FakeSession();
        var sessions = new Queue<FakeSession>(new[] { first, second });
        var vm = new MainViewModel(sessions.Dequeue) { Target = ChatTarget.Claude };
        vm.GeminiMember.IsPresent = false;
        vm.InputText = "1on1で話した";
        await SendOneAsync(vm, first, Participants.ClaudeId);

        vm.StartNewMeeting();

        Assert.True(vm.GeminiPresent && vm.ClaudePresent && vm.ChappyPresent);
        Assert.All(vm.Members.Where(m => m.CanChoosePresence), m => Assert.True(m.CanChangePresence));
    }

    [Fact]
    public async Task 参加していない人への呼びかけは_宛先を変えず_普通の本文として送る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session, defaultParticipants: new[] { Participants.ClaudeId });

        vm.InputText = "ジェミ、これ見て";
        await SendOneAsync(vm, session, Participants.ClaudeId);

        Assert.Equal(new[] { Participants.ClaudeId }, session.Sent[^1].Targets);
        Assert.Contains("ジェミ、これ見て", session.Sent[^1].Text);
    }

    [Fact]
    public async Task 参加している人への呼びかけは_今までどおり宛先を切り替える()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session, defaultParticipants: new[] { Participants.ClaudeId, Participants.GeminiId })
        {
            Target = ChatTarget.Claude,
            InputText = "ジェミ、これ見て",
        };
        await SendOneAsync(vm, session, Participants.GeminiId);

        Assert.Equal(new[] { Participants.GeminiId }, session.Sent[^1].Targets);
    }

    [Fact]
    public async Task 参加していない人の_聞くボタンは出ない()
    {
        var alone = new FakeSession();
        var aloneVm = new MainViewModel(() => alone, defaultParticipants: new[] { Participants.ClaudeId }) { InputText = "答えて" };
        await SendOneAsync(aloneVm, alone, Participants.ClaudeId);
        Assert.DoesNotContain(aloneVm.Messages.Last(m => !m.IsUser).AskTargets, t => t.CanAsk);

        var pair = new FakeSession();
        var pairVm = new MainViewModel(() => pair, defaultParticipants: new[] { Participants.ClaudeId, Participants.GeminiId })
            { InputText = "答えて", Target = ChatTarget.Claude };
        await SendOneAsync(pairVm, pair, Participants.ClaudeId);
        Assert.Equal(new[] { Participants.GeminiId },
            pairVm.Messages.Last(m => !m.IsUser).AskTargets.Where(t => t.CanAsk).Select(t => t.Id));
    }

    [Fact]
    public void 過去の会議を開き直すと_その会議で話した人だけが参加している()
    {
        var reopened = new FakeSession
        {
            RestoredEntries = new[]
            {
                new ChatEntry(Participants.UserId, "前の質問", new DateTime(2026, 10, 28, 9, 0, 0), new[] { Participants.GeminiId }),
                new ChatEntry(Participants.GeminiId, "前の返答", new DateTime(2026, 10, 28, 9, 0, 5)),
            },
        };
        var initial = new FakeSession { OnOpenPastMeeting = _ => reopened };
        var vm = new MainViewModel(() => initial, defaultParticipants: new[] { Participants.ClaudeId });
        Assert.False(vm.GeminiPresent);

        vm.OpenPastMeeting("past");

        Assert.True(vm.GeminiPresent); // 話した人は、参加している
        Assert.False(vm.ClaudePresent); // 既定の参加者でも、その会議にいなかった人は、参加にしない
        Assert.False(vm.ChappyPresent);
        Assert.Equal(ChatTarget.Gemini, vm.Target); // 1on1なので、宛先はその人
        Assert.All(vm.Members.Where(m => m.CanChoosePresence), m => Assert.False(m.CanChangePresence)); // 開き直した会議は、始まっている
    }

    [Fact]
    public async Task 別々に見る列は_チャッピーの分もある()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "みんな", Target = ChatTarget.Everyone };
        var sending = vm.SendAsync();
        session.Finish(new ChatProgress(Participants.ChappyId, ChatProgressKind.Done, "ペンギンです"));
        await sending;

        Assert.Contains(vm.ChappyMessages, m => m.Speaker.Id == Participants.ChappyId);
        Assert.Contains(vm.ChappyMessages, m => m.IsUser && m.Text == "みんな"); // 宛先に入っていた発言も、列に出る
        Assert.Contains(vm.ClaudeMessages, m => m.IsUser && m.Text == "みんな");
    }

    [Fact]
    public void ログのChatGPTの発言は_チャッピーとして復元される()
    {
        using var dir = new TempDir();
        var log = new MarkdownLogWriter(dir.Path, new DateTime(2026, 10, 28, 10, 0, 0));
        log.Append(new ChatEntry(Participants.UserId, "チャッピーに質問", new DateTime(2026, 10, 28, 10, 0, 5), new[] { Participants.ChappyId }));
        log.Append(new ChatEntry(Participants.ChappyId, "ペンギンです", new DateTime(2026, 10, 28, 10, 0, 9)));

        var restored = MeetingStore.LoadState(Assert.Single(MeetingStore.ListMeetings(dir.Path)).FolderPath);

        Assert.Equal(new[] { Participants.UserId, Participants.ChappyId }, restored.Entries.Select(e => e.SpeakerId));
        Assert.Equal(new[] { Participants.ChappyId }, restored.Entries[0].TargetIds);
    }

    [Fact]
    public void 会議の始めの画面を画像に描ける_全員が参加と1on1()
    {
        var all = MainWindowRenderTests.Render(new MainViewModel(() => new FakeSession()), "participants_all.png");
        var one = MainWindowRenderTests.Render(
            new MainViewModel(() => new FakeSession(), defaultParticipants: new[] { Participants.GeminiId }), "participants_one_on_one.png");

        Assert.True(new FileInfo(all).Length > 10_000);
        Assert.True(new FileInfo(one).Length > 10_000);
    }

    [Fact]
    public void 別々に見る画面を画像に描ける_3列_2列_クローディとチャッピーだけ()
    {
        var three = MainWindowRenderTests.Render(new MainViewModel(() => new FakeSession()) { IsSplitView = true }, "participants_split_3.png");
        var two = MainWindowRenderTests.Render(
            new MainViewModel(() => new FakeSession(), defaultParticipants: new[] { Participants.GeminiId, Participants.ChappyId }) { IsSplitView = true },
            "participants_split_2.png");
        var gap = MainWindowRenderTests.Render(
            new MainViewModel(() => new FakeSession(), defaultParticipants: new[] { Participants.ClaudeId, Participants.ChappyId }) { IsSplitView = true },
            "participants_split_claude_chappy.png");

        Assert.All(new[] { three, two, gap }, png => Assert.True(new FileInfo(png).Length > 10_000));
    }

    [Fact]
    public void 設定ファイルのdefaultParticipantsを読める_書かなければ全員()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings.json")))!.AsObject();
        Assert.Null(SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json")).DefaultParticipants);

        json["defaultParticipants"] = new System.Text.Json.Nodes.JsonArray("claude", "chappy");
        File.WriteAllText(path, json.ToJsonString());

        Assert.Equal(new[] { "claude", "chappy" }, SettingsLoader.Load(path).DefaultParticipants);
    }
}
