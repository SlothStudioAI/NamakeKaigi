using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

public class MainViewModelTests
{
    private static ChatProgress Done(string id, string text) => new(id, ChatProgressKind.Done, text);
    private static ChatProgress Delta(string id, string text) => new(id, ChatProgressKind.Delta, text);
    private static ChatProgress Error(string id, string text) => new(id, ChatProgressKind.Error, text);

    private static MemberViewModel Claude(MainViewModel vm) => vm.ClaudeMember;

    [Fact]
    public void ジェミリーは労力をモデルと別に選べず_クローディは選べる()
    {
        var session = new FakeSession { NoEffortAgents = { Participants.GeminiId } };
        var vm = new MainViewModel(() => session);

        Assert.True(vm.ClaudeMember.CanChooseEffort);
        Assert.False(vm.GeminiMember.CanChooseEffort);
    }

    [Fact]
    public void 制限時間の選択欄は_会議の値で始まり_選ぶと会議に伝わる()
    {
        var session = new FakeSession { TimeoutSec = 450 };
        var vm = new MainViewModel(() => session);

        Assert.Equal(450, vm.SelectedTimeoutSec);
        Assert.Equal(new[] { 300, 450, 600, 1800, 3600, 7200 }, vm.TimeoutOptions.Select(o => o.Seconds));
        Assert.Equal("⏱ 10分", vm.TimeoutOptions.Single(o => o.Seconds == 600).Label);
        Assert.Equal("⏱ 120分", vm.TimeoutOptions.Single(o => o.Seconds == 7200).Label);
        Assert.Equal("⏱ 450秒", vm.TimeoutOptions.Single(o => o.Seconds == 450).Label);

        vm.SelectedTimeoutSec = 3600;
        Assert.Equal(new[] { 3600 }, session.TimeoutChanges);
    }

    [Theory]
    [InlineData(180)]
    [InlineData(1200)]
    public void 一覧から外した3分と20分を保存していても_その値を出して選んだままにする(int saved)
    {
        var session = new FakeSession { TimeoutSec = saved };
        var vm = new MainViewModel(() => session);

        Assert.Equal(saved, vm.SelectedTimeoutSec);
        Assert.Equal(new[] { 300, 600, 1800, 3600, 7200 }.Append(saved).Order(), vm.TimeoutOptions.Select(o => o.Seconds));
    }

    [Fact]
    public async Task 全員宛ての送信で_あなたの吹き出しと_届いた返答が並ぶ()
    {
        var session = new FakeSession
        {
            Script = _ => new[] { Done(Participants.GeminiId, "ジェミリーの案"), Done(Participants.ClaudeId, "クローディの案"), Done(Participants.ChappyId, "チャッピーの案") },
        };
        var vm = new MainViewModel(() => session) { InputText = "案をください" };

        await vm.SendAsync();

        Assert.Equal(new[] { "あなた", "ジェミリー", "クローディ", "チャッピー" }, vm.Messages.Select(m => m.DisplayName));
        Assert.True(vm.Messages[0].IsUser);
        Assert.Equal("", vm.InputText);
        Assert.Equal(new[] { Participants.ClaudeId, Participants.GeminiId, Participants.ChappyId }, session.Sent[0].Targets); // 同時なら、ゲストも入る
        Assert.All(vm.Members, m => Assert.Equal(0, m.PendingCount));
    }

    [Fact]
    public async Task 宛先を選ぶと_その1人だけに送る()
    {
        var session = new FakeSession { Script = _ => new[] { Done(Participants.GeminiId, "はい") } };
        var vm = new MainViewModel(() => session) { InputText = "ジェミリーへ", TargetGemini = true };

        await vm.SendAsync();

        Assert.Equal(new[] { Participants.GeminiId }, session.Sent[0].Targets);
        Assert.False(vm.TargetEveryone);
    }

    [Fact]
    public async Task 返答が流れてくると_1つの吹き出しに文字が増え_完成すると全文に置き換わる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "提案して", TargetClaude = true };

        var sending = vm.SendAsync();
        Assert.Equal("考え中…", Claude(vm).StatusText); // まだ1文字も届いていない
        Assert.Equal(2, vm.Messages.Count); // あなたの吹き出しと、考え中の仮の吹き出し
        Assert.False(vm.Messages[1].HasText);
        Assert.True(vm.Messages[1].ShowStatus);
        Assert.Equal("💭 考え中…", vm.Messages[1].StatusLine);

        session.Push(Delta(Participants.ClaudeId, "バジ"));
        session.Push(Delta(Participants.ClaudeId, "ルです"));

        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("バジルです", vm.Messages[1].Text);
        Assert.True(vm.Messages[1].IsStreaming);
        Assert.Equal("回答中…", Claude(vm).StatusText);

        session.Finish(Done(Participants.ClaudeId, "バジルです。おすすめです。"));
        await sending;

        Assert.Equal(2, vm.Messages.Count); // 吹き出しは増えない
        Assert.Equal("バジルです。おすすめです。", vm.Messages[1].Text);
        Assert.False(vm.Messages[1].IsStreaming);
        Assert.Equal("待機中", Claude(vm).StatusText);
    }

    [Fact]
    public async Task 作業の前のつぶやきは本文から外れて作業の欄に移り_本文には最後の回答だけが流れる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "調べて", TargetClaude = true };
        var sending = vm.SendAsync();
        var bubble = vm.Messages[1];

        session.Push(Delta(Participants.ClaudeId, "確認します。"));
        Assert.Equal("確認します。", bubble.Text);

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "AI_MEMORY.md を読んでいます"));
        Assert.False(bubble.HasText); // つぶやきは本文から外れる
        Assert.Equal("確認します。", bubble.StatusNote);
        Assert.Equal("⏳ AI_MEMORY.md を読んでいます…", bubble.StatusLine);
        Assert.True(bubble.ShowStatus);

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "AI_MEMORY.md を読みました"));
        Assert.Equal("✔ AI_MEMORY.md を読みました", bubble.StatusLine);

        session.Push(Delta(Participants.ClaudeId, "結果です"));
        Assert.Equal("結果です", bubble.Text);
        Assert.Equal("", bubble.StatusLine);

        session.Finish(Done(Participants.ClaudeId, "結果です。"));
        await sending;
        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("結果です。", bubble.Text);
        Assert.False(bubble.ShowStatus); // 済んだら作業の欄は消える
    }

    [Fact]
    public async Task 全員に同時に送ると_仮の吹き出しは下に並び_先に済んだ返答が上に来る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "案をください" };
        var sending = vm.SendAsync();
        Assert.Equal(new[] { "あなた", "クローディ", "ジェミリー", "チャッピー" }, vm.Messages.Select(m => m.DisplayName));

        session.Push(Done(Participants.GeminiId, "ジェミリーの案"));
        Assert.Equal(new[] { "あなた", "ジェミリー", "クローディ", "チャッピー" }, vm.Messages.Select(m => m.DisplayName));
        Assert.True(vm.Messages[2].IsStreaming); // まだ考え中のクローディとチャッピーが下
        Assert.True(vm.Messages[3].IsStreaming);

        session.Push(Done(Participants.ClaudeId, "クローディの案"));
        session.Finish(Done(Participants.ChappyId, "チャッピーの案"));
        await sending;
        Assert.Equal(new[] { "ジェミリーの案", "クローディの案", "チャッピーの案" }, vm.Messages.Skip(1).Select(m => m.Text));
        Assert.Single(vm.GeminiMessages, m => !m.IsUser);
    }

    [Fact]
    public async Task 返答途中に次の発言を送ると_あなたの吹き出しは考え中の吹き出しの上に入る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "1つ目", TargetClaude = true };
        var first = vm.SendAsync();

        vm.InputText = "2つ目";
        vm.TargetGemini = true;
        var second = vm.SendAsync();

        Assert.Equal(new[] { "1つ目", "2つ目" }, vm.Messages.Where(m => m.IsUser).Select(m => m.Text));
        Assert.All(vm.Messages.Skip(3), m => Assert.True(m.IsStreaming)); // 仮の吹き出し2つが一番下
        Assert.Equal(4, vm.Messages.Count);
        session.Push(Done(Participants.ClaudeId, "a"));
        session.Finish(Done(Participants.GeminiId, "b"));
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task 一文字も届かずに停止したら_仮の吹き出しは消えて_停止の知らせだけが残る()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "提案して", TargetClaude = true };
        var sending = vm.SendAsync();
        Assert.Equal(2, vm.Messages.Count);

        session.Finish(Error(Participants.ClaudeId, "回答を停止しました。"));
        await sending;

        Assert.Equal(new[] { "あなた", "クローディ" }, vm.Messages.Select(m => m.DisplayName));
        Assert.True(vm.Messages[1].IsError);
        Assert.Equal(2, vm.ClaudeMessages.Count);
    }

    [Fact]
    public async Task ファイルを読んでいる間は_状態にその作業が出て_文字が流れ始めると回答中に変わる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "記憶を読んで", TargetClaude = true };
        var sending = vm.SendAsync();

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "AI_MEMORY.md を読んでいます"));
        Assert.Equal("AI_MEMORY.md を読んでいます…", Claude(vm).StatusText);

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, ""));
        Assert.Equal("考え中…", Claude(vm).StatusText); // 作業が済んで、また考え中

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "CLAUDE.md を読んでいます"));
        session.Push(Delta(Participants.ClaudeId, "読みました"));
        Assert.Equal("回答中…", Claude(vm).StatusText);

        session.Finish(Done(Participants.ClaudeId, "読みました。"));
        await sending;
        Assert.Equal("待機中", Claude(vm).StatusText);
        Assert.Equal("", Claude(vm).ActivityText);
    }

    [Fact]
    public async Task 途中で失敗したら_流れた分は残し_エラーの吹き出しを足す()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "提案して", TargetClaude = true };
        var sending = vm.SendAsync();
        session.Push(Delta(Participants.ClaudeId, "途中まで"));

        session.Finish(Error(Participants.ClaudeId, "通信できません"));
        await sending;

        Assert.Equal(new[] { "あなた", "クローディ", "クローディ" }, vm.Messages.Select(m => m.DisplayName));
        Assert.False(vm.Messages[1].IsStreaming);
        Assert.True(vm.Messages[2].IsError);
        Assert.Equal("通信できません", vm.Messages[2].Text);
        Assert.Equal(0, Claude(vm).PendingCount);
    }

    [Fact]
    public async Task モデル名は_会議の設定から初期表示され_実際の名前が分かれば置き換わる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "こんにちは", TargetClaude = true };
        Assert.Equal("sonnet", vm.ClaudeMember.ModelText);
        Assert.Equal("未指定(agyの既定)", vm.GeminiMember.ModelText);
        Assert.True(vm.ClaudeMember.HasModel);
        Assert.False(vm.Members[0].HasModel); // あなたには出さない

        var sending = vm.SendAsync();
        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Model, "claude-sonnet-5-5"));
        session.Finish(Done(Participants.ClaudeId, "やあ"));
        await sending;

        Assert.Equal("claude-sonnet-5-5", vm.ClaudeMember.ModelText);
        Assert.Equal("未指定(agyの既定)", vm.GeminiMember.ModelText);
    }

    [Fact]
    public async Task 失敗した返答は_エラーの吹き出しになる()
    {
        var session = new FakeSession { Script = _ => new[] { Error(Participants.ClaudeId, "通信できません") } };
        var vm = new MainViewModel(() => session) { InputText = "こんにちは", TargetClaude = true };

        await vm.SendAsync();

        Assert.True(vm.Messages[1].IsError);
        Assert.Equal("通信できません", vm.Messages[1].Text);
    }

    [Fact]
    public async Task 返答待ちの間は_考え中になり_次の発言も送れる()
    {
        var session = new FakeSession(); // Finish を呼ぶまで返さない
        var vm = new MainViewModel(() => session) { InputText = "1つ目", TargetClaude = true };

        var sending = vm.SendAsync();
        var claude = vm.ClaudeMember;
        Assert.True(claude.IsThinking);
        Assert.Equal("考え中…", claude.StatusText);

        vm.InputText = "2つ目"; // 待っている間も入力できる
        var second = vm.SendAsync();
        Assert.Equal(2, claude.PendingCount);
        Assert.Equal(2, session.Sent.Count);

        session.Finish(Done(Participants.ClaudeId, "1つ目への返答"));
        session.Finish(Done(Participants.ClaudeId, "2つ目への返答"));
        await Task.WhenAll(sending, second);

        Assert.False(claude.IsThinking);
        Assert.Equal("待機中", claude.StatusText);
    }

    [Fact]
    public async Task 空の入力は送れない()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "   " };

        Assert.False(vm.SendCommand.CanExecute(null));
        await vm.SendAsync();
        Assert.Empty(session.Sent);

        vm.InputText = "あ";
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task 別々に見る列は_そのAIへの発言と返答だけが並ぶ()
    {
        var session = new FakeSession
        {
            Script = text => text == "全員へ"
                ? new[] { Done(Participants.ClaudeId, "Cの返答1"), Done(Participants.GeminiId, "Gの返答1"), Done(Participants.ChappyId, "チャッピーの返答1") }
                : new[] { Done(Participants.GeminiId, "Gの返答2") },
        };
        var vm = new MainViewModel(() => session);
        vm.InputText = "全員へ";
        await vm.SendAsync();
        vm.TargetGemini = true;
        vm.InputText = "ジェミリーだけへ";
        await vm.SendAsync();

        Assert.Equal(6, vm.Messages.Count); // まとめて見る: すべて(ゲストのチャッピーも)
        Assert.Equal(new[] { "全員へ", "Cの返答1" }, vm.ClaudeMessages.Select(m => m.Text));
        Assert.Equal(new[] { "全員へ", "Gの返答1", "ジェミリーだけへ", "Gの返答2" }, vm.GeminiMessages.Select(m => m.Text));
    }

    [Fact]
    public void 画面の切り替えは_見せ方だけを変える()
    {
        var vm = new MainViewModel(() => new FakeSession());
        Assert.True(vm.IsTimelineView);
        Assert.False(vm.IsSplitView);

        vm.IsSplitView = true;
        Assert.False(vm.IsTimelineView);

        vm.IsTimelineView = true;
        Assert.False(vm.IsSplitView);
    }

    [Fact]
    public async Task 作業が終わると_過去形で残り_文字が流れ始めるまで消えない()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "記憶を読んで", TargetClaude = true };
        var sending = vm.SendAsync();

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "AI_MEMORY.md を読んでいます"));
        Assert.Equal("AI_MEMORY.md を読んでいます…", Claude(vm).StatusText);

        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "AI_MEMORY.md を読みました"));
        Assert.Equal("AI_MEMORY.md を読みました", Claude(vm).StatusText); // 「…」を付けず、そのまま残る

        session.Push(Delta(Participants.ClaudeId, "読み"));
        Assert.Equal("回答中…", Claude(vm).StatusText);

        session.Finish(Done(Participants.ClaudeId, "読みました。"));
        await sending;
    }

    [Fact]
    public void 選べるモデルの一覧が出て_いま選ばれているモデルが選択された状態になる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session);

        Assert.Equal(new[] { "sonnet", "opus" }, vm.ClaudeMember.ModelOptions.Select(o => o.Id));
        Assert.Equal("sonnet", vm.ClaudeMember.SelectedModelId);
        Assert.True(vm.ClaudeMember.CanChooseModel);
        Assert.False(vm.Members[0].CanChooseModel); // あなたは選べない
        Assert.Empty(session.ModelChanges); // 一覧を入れただけでは、切り替えを頼まない
    }

    [Fact]
    public void モデルを選ぶと_会議に切り替えを頼み_表示も選んだモデルの名前になる()
    {
        var session = new FakeSession();
        session.InitialOptions[Participants.GeminiId] = new[]
        {
            new ModelOption("gemini-4-argon-medium", "Gemini 4 Argon (Medium)"),
            new ModelOption("gemini-3.8-flash-low", "Gemini 3.8 Flash (Low)"),
        };
        var vm = new MainViewModel(() => session);

        vm.GeminiMember.SelectedModelId = "gemini-3.8-flash-low";

        Assert.Equal(new[] { (Participants.GeminiId, "gemini-3.8-flash-low") }, session.ModelChanges);
        Assert.Equal("Gemini 3.8 Flash (Low)", vm.GeminiMember.ModelText);
    }

    /// <summary>agy の一覧(モデル×段階の組み合わせ)を、試験用に作る。</summary>
    private static ModelOption[] AgyOptions() => new[]
    {
        new ModelOption("gemini-4-argon-medium", "Gemini 4 Argon (Medium)"),
        new ModelOption("gemini-4-argon-high", "Gemini 4 Argon (High)"),
        new ModelOption("gemini-4-argon-low", "Gemini 4 Argon (Low)"),
        new ModelOption("gemini-3.8-flash-high", "Gemini 3.8 Flash (High)"),
        new ModelOption("gemini-3.8-flash-medium", "Gemini 3.8 Flash (Medium)"),
        new ModelOption("gemini-3.8-flash-low", "Gemini 3.8 Flash (Low)"),
        new ModelOption("gpt-oss-120b-medium", "GPT-OSS 120B (Medium)"),
    };

    [Fact]
    public void ジェミリーは_モデル名と段階の2つの欄で選べて_段階は低中高の順に並ぶ()
    {
        var session = new FakeSession { NoEffortAgents = { Participants.GeminiId } }; // agy は、労力を別に選べない
        session.CurrentModels[Participants.GeminiId] = "gemini-3.8-flash-medium";
        session.InitialOptions[Participants.GeminiId] = AgyOptions();
        var vm = new MainViewModel(() => session);
        var gemini = vm.GeminiMember;

        Assert.True(gemini.ShowVariantChoice);
        Assert.False(gemini.ShowSingleModelChoice);
        Assert.Equal(new[] { "Gemini 4 Argon", "Gemini 3.8 Flash", "GPT-OSS 120B" }, gemini.ModelFamilies);
        Assert.Equal("Gemini 3.8 Flash", gemini.SelectedFamily);
        Assert.Equal("medium", gemini.SelectedVariantLevelId);
        Assert.Equal(new[] { "低", "中", "高" }, gemini.VariantLevels.Select(l => l.Label));
        Assert.False(vm.ClaudeMember.ShowVariantChoice); // クローディは、今までどおり モデル＋労力
        Assert.Empty(session.ModelChanges);
    }

    [Fact]
    public void 段階を選ぶと_同じモデル名のその段階に切り替わる()
    {
        var session = new FakeSession { NoEffortAgents = { Participants.GeminiId } }; // agy は、労力を別に選べない
        session.CurrentModels[Participants.GeminiId] = "gemini-3.8-flash-medium";
        session.InitialOptions[Participants.GeminiId] = AgyOptions();
        var vm = new MainViewModel(() => session);

        vm.GeminiMember.SelectedVariantLevelId = "high";

        Assert.Equal(new[] { (Participants.GeminiId, "gemini-3.8-flash-high") }, session.ModelChanges);
        Assert.Equal("gemini-3.8-flash-high", vm.GeminiMember.SelectedModelId);
    }

    [Fact]
    public void モデル名を選ぶと_同じ段階に切り替わり_その段階がなければ中にする()
    {
        var session = new FakeSession { NoEffortAgents = { Participants.GeminiId } }; // agy は、労力を別に選べない
        session.CurrentModels[Participants.GeminiId] = "gemini-3.8-flash-low";
        session.InitialOptions[Participants.GeminiId] = AgyOptions();
        var vm = new MainViewModel(() => session);
        var gemini = vm.GeminiMember;

        gemini.SelectedFamily = "Gemini 4 Argon";
        Assert.Equal("gemini-4-argon-low", gemini.SelectedModelId); // 同じ「低」

        gemini.SelectedFamily = "GPT-OSS 120B";
        Assert.Equal("gpt-oss-120b-medium", gemini.SelectedModelId); // 「低」がないので「中」
        Assert.Equal(new[] { "中" }, gemini.VariantLevels.Select(l => l.Label));
        Assert.Equal("medium", gemini.SelectedVariantLevelId);
    }

    [Fact]
    public void 表示名が名前_段階の形でない一覧は_分けずに1つの欄で選ぶ()
    {
        var session = new FakeSession();
        session.InitialOptions[Participants.GeminiId] = new[] { new ModelOption("gemini-4-argon-medium", "gemini-4-argon-medium") };
        var vm = new MainViewModel(() => session);

        Assert.False(vm.GeminiMember.ShowVariantChoice);
        Assert.True(vm.GeminiMember.ShowSingleModelChoice);
    }

    [Fact]
    public async Task 起動後にCLIから一覧が届くと_選択欄と表示の名前が最新になる()
    {
        var session = new FakeSession { NoEffortAgents = { Participants.GeminiId } }; // agy は、労力を別に選べない
        session.CurrentModels[Participants.GeminiId] = "gemini-4-argon-medium";
        session.InitialOptions[Participants.GeminiId] = new[] { new ModelOption("gemini-4-argon-medium", "gemini-4-argon-medium") };
        session.LoadedOptions[Participants.GeminiId] = new[]
        {
            new ModelOption("gemini-4-argon-medium", "Gemini 4 Argon (Medium)"),
            new ModelOption("gemini-3.8-flash-low", "Gemini 3.8 Flash (Low)"),
        };
        var vm = new MainViewModel(() => session);
        await Task.Delay(50); // 起動時の一覧の取得が終わるのを待つ

        Assert.Equal(2, vm.GeminiMember.ModelOptions.Count);
        Assert.Equal("gemini-4-argon-medium", vm.GeminiMember.SelectedModelId);
        Assert.Equal("Gemini 4 Argon (Medium)", vm.GeminiMember.ModelText);
        Assert.Empty(session.ModelChanges);
    }

    [Fact]
    public async Task 新しい会議を始めると_画面が空になり_古い会議の知らせは捨てる()
    {
        var first = new FakeSession { LogPath = "first.md" };
        var second = new FakeSession { LogPath = "second.md" };
        var sessions = new Queue<FakeSession>(new[] { first, second });
        var vm = new MainViewModel(() => sessions.Dequeue()) { InputText = "古い会議の発言", TargetClaude = true };
        var sending = vm.SendAsync();

        vm.StartNewMeeting();
        Assert.Empty(vm.Messages);
        Assert.Empty(vm.ClaudeMessages);
        Assert.Equal("second.md", vm.LogPath);

        first.Push(Delta(Participants.ClaudeId, "遅れて届いた文字"));
        first.Finish(Done(Participants.ClaudeId, "遅れて届いた返答"));
        await sending;

        Assert.Empty(vm.Messages);
        Assert.Empty(vm.ClaudeMessages);
        Assert.All(vm.Members, m => Assert.Equal(0, m.PendingCount));
    }

    [Fact]
    public async Task ファイルやフォルダを添付すると_入力が空でも送れ_送信後に添付がクリアされる()
    {
        using var ext = new TempDir();
        var filePath = Path.Combine(ext.Path, "note.txt");
        File.WriteAllText(filePath, "添付の中身です");

        var session = new FakeSession
        {
            Script = _ => new[] { Done(Participants.ClaudeId, "読みました") },
        };
        var vm = new MainViewModel(() => session) { TargetClaude = true };

        vm.AddAttachments(new[] { filePath, filePath }); // 重複は1件にまとまる
        Assert.Single(vm.Attachments);
        Assert.True(vm.HasAttachments);
        Assert.True(vm.SendCommand.CanExecute(null)); // 本文が空でも添付があれば送れる

        await vm.SendAsync();

        Assert.Empty(vm.Attachments);
        Assert.False(vm.HasAttachments);
        Assert.Contains("note.txt", vm.Messages[0].Text);
        Assert.Contains("添付の中身です", session.Sent[0].Text);
        Assert.Single(session.ExtraDirs);
    }

    [Fact]
    public void 添付のバツボタンや新しい会議で_添付を取り消せる()
    {
        using var ext = new TempDir();
        var filePath = Path.Combine(ext.Path, "a.txt");
        File.WriteAllText(filePath, "a");

        var vm = new MainViewModel(() => new FakeSession());
        vm.AddAttachments(new[] { filePath });
        Assert.Single(vm.Attachments);

        vm.RemoveAttachment(vm.Attachments[0]);
        Assert.Empty(vm.Attachments);
        Assert.False(vm.HasAttachments);
    }

    [Fact]
    public void 労力の選択は_既定が中で_選び直すと会議に伝わる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session);

        Assert.Equal("medium", vm.ClaudeMember.SelectedEffortId);
        Assert.Equal("medium", vm.GeminiMember.SelectedEffortId);
        Assert.Empty(session.EffortChanges);

        vm.ClaudeMember.SelectedEffortId = "high";
        vm.GeminiMember.SelectedEffortId = "low";

        Assert.Equal(new[] { (Participants.ClaudeId, "high"), (Participants.GeminiId, "low") }, session.EffortChanges);
    }

    [Fact]
    public async Task 返答の先頭に担当の行が付いてきても_吹き出しには出さない()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "お願い", TargetClaude = true };
        var sending = vm.SendAsync();

        session.Push(Delta(Participants.ClaudeId, "担当: ツクル（実装担当）\n"));
        session.Push(Delta(Participants.ClaudeId, "了解しました。"));
        Assert.Equal("了解しました。", vm.Messages[1].Text);

        session.Finish(Done(Participants.ClaudeId, "担当: ツクル（実装担当）\n\n了解しました。完了です。"));
        await sending;
        Assert.Equal("了解しました。完了です。", vm.Messages[1].Text);
    }

    [Fact]
    public async Task 使用量の枠が届くと_クローディのカードに表示と警告状態が反映される()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "枠を教えて", TargetClaude = true };
        var sending = vm.SendAsync();

        var quota = new UsageQuota(85, DateTimeOffset.Now.AddHours(2), 20, DateTimeOffset.Now.AddDays(3));
        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Usage, quota.Encode()));
        session.Finish(Done(Participants.ClaudeId, "どうぞ"));
        await sending;

        Assert.True(vm.ClaudeMember.HasUsage);
        Assert.Equal("枠: 5時間 85% · 7日 20%", vm.ClaudeMember.UsageText);
        Assert.True(vm.ClaudeMember.IsUsageHigh);
        Assert.Contains("5時間枠: 85%", vm.ClaudeMember.UsageTooltip);
    }

    [Fact]
    public void 過去の会議を開き直すと_復元された発言が画面の吹き出しに並ぶ()
    {
        var pastEntries = new[]
        {
            new ChatEntry(Participants.UserId, "前の質問", new DateTime(2026, 10, 24, 10, 0, 0), new[] { Participants.ClaudeId }),
            new ChatEntry(Participants.ClaudeId, "前の返答", new DateTime(2026, 10, 24, 10, 0, 5)),
        };
        var reopenedSession = new FakeSession { LogPath = "past/log.md", RestoredEntries = pastEntries };
        var initialSession = new FakeSession { OnOpenPastMeeting = _ => reopenedSession };
        var vm = new MainViewModel(() => initialSession);

        vm.OpenPastMeeting("past");

        Assert.Equal("past/log.md", vm.LogPath);
        Assert.Equal(new[] { "前の質問", "前の返答" }, vm.Messages.Select(m => m.Text));
        Assert.Equal(new[] { "前の質問", "前の返答" }, vm.ClaudeMessages.Select(m => m.Text));
        Assert.Empty(vm.GeminiMessages);
    }

    [Fact]
    public async Task 返答待ちの間はIsBusyがtrueになり_停止コマンドでキャンセルできる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "長い質問", TargetClaude = true };
        Assert.False(vm.IsBusy);
        Assert.False(vm.CancelCommand.CanExecute(null));

        var sending = vm.SendAsync();
        Assert.True(vm.IsBusy);
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);
        await sending;

        Assert.Equal(1, session.CancelCallCount);
        Assert.False(vm.IsBusy);
        Assert.Contains(vm.Messages, m => m.IsError && m.Text.Contains("停止"));
    }

    [Fact]
    public async Task 吹き出しのほかの2人に聞くを押すと_そのAI宛てに意見を聞く()
    {
        var session = new FakeSession
        {
            Script = _ => new[] { Done(Participants.ChappyId, "わたしの意見です") },
        };
        var vm = new MainViewModel(() => session);
        var claudeBubble = new MessageViewModel(Participants.Claude, "クローディの設計案です", DateTime.Now);
        Assert.True(claudeBubble.CanAsk);
        Assert.Equal(new[] { "🔍 ジェミリーに聞く", "🔍 チャッピーに聞く" }, claudeBubble.AskTargets.Select(t => t.Label));

        await vm.AskOpinionAsync(claudeBubble, Participants.ChappyId);

        Assert.Equal(ChatTarget.Chappy, vm.Target);
        Assert.Single(session.Sent);
        Assert.Equal(new[] { Participants.ChappyId }, session.Sent[0].Targets);
        var prompt = session.Sent[0].Text;
        Assert.StartsWith("直前のクローディの発言について、あなたの視点でどう思うかを", prompt); // 査読ではなく、意見を聞く
        Assert.DoesNotContain("査読", prompt);
        Assert.Contains("クローディの設計案です", prompt);
        Assert.Equal("🔍 クローディの発言について、チャッピーに意見を聞きます。", vm.Messages[0].Text);
        Assert.Equal("わたしの意見です", vm.Messages.Last().Text);
    }

    [Fact]
    public async Task 意見を聞く文は_設定の文を使う()
    {
        var session = new FakeSession
        {
            Relay = new RelaySettings(OpinionNote: "{speaker}について一言"),
            Script = _ => new[] { Done(Participants.GeminiId, "一言です") },
        };
        var vm = new MainViewModel(() => session);
        var chappyBubble = new MessageViewModel(Participants.Chappy, "チャッピーの案", DateTime.Now);
        Assert.Equal(new[] { "🔍 クローディに聞く", "🔍 ジェミリーに聞く" }, chappyBubble.AskTargets.Select(t => t.Label));

        await vm.AskOpinionAsync(chappyBubble, Participants.GeminiId);

        Assert.StartsWith("チャッピーについて一言\n\n【対象の発言（チャッピー）】", session.Sent[0].Text);
    }

    [Fact]
    public async Task 簡易表示モードと名前変更で_見出し_参加者名_吹き出し_パス省略が切り替わる()
    {
        var session = new FakeSession
        {
            LogPath = "C:/Work/logs/20261025-110000/log.md",
            Script = _ => new[] { Done(Participants.ClaudeId, "了解です") },
        };
        var vm = new MainViewModel(() => session) { InputText = "テスト", TargetClaude = true };
        await vm.SendAsync();

        // 初期状態(標準)
        Assert.False(vm.IsBusinessMode);
        Assert.Equal("NamakeKaigi", vm.AppTitle);
        Assert.Equal("クローディ", vm.ClaudeDisplayName);
        Assert.Equal("ジェミリー", vm.GeminiDisplayName);
        Assert.True(vm.ClaudeMember.ShowImageAvatar);
        // 見出しの札は、標準でもフォルダ名だけ
        Assert.Equal("NamakeKaigi", vm.WorkDirName);

        // 簡易表示モードON(1クリック切り替え)
        vm.IsBusinessMode = true;
        Assert.Equal("NamakeKaigi", vm.AppTitle); // 名前は簡易表示モードでも同じ
        Assert.Equal("Claude", vm.ClaudeDisplayName);
        Assert.Equal("Gemini", vm.GeminiDisplayName);
        Assert.True(vm.ClaudeMember.ShowInitialAvatar);
        Assert.Equal("C", vm.ClaudeMember.InitialText);
        Assert.Equal("NamakeKaigi", vm.WorkDirDisplay);
        Assert.Equal("20261025-110000/log.md", vm.LogPathDisplay);
        // 既存の吹き出しも自動で名前とバッジに切り替わる
        Assert.Equal("Claude", vm.Messages[1].DisplayName);
        Assert.Equal("🔍 Geminiに聞く", vm.Messages[1].AskTargets[0].Label);

        // 自由な名前変更
        vm.UpdateAppearance(new AppearanceSettings(true, "リーダー", "メイン担当", "QA担当"));
        Assert.Equal("NamakeKaigi", vm.AppTitle); // アプリ名は変えられない
        Assert.Equal("NamakeKaigi — Meeting-Driven Development", vm.WindowHeaderTitle); // MDDも簡易表示で消えない
        Assert.Equal("メイン担当", vm.ClaudeDisplayName);
        Assert.Equal("QA担当", vm.GeminiDisplayName);
        Assert.Equal("🔍 QA担当に聞く", vm.Messages[1].AskTargets[0].Label);
    }
}
