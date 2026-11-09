using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>予約の追加・編集・削除と、予約ごとのモデル指定(ふだんのモデルを書き換えない)の試験。</summary>
public class ScheduleEditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "namake_schededit_" + Guid.NewGuid().ToString("N"));
    private readonly List<FakeSession> _sessions = new();
    private DateTime _now = DateTime.Parse("2026-11-05 10:00:00");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static IReadOnlyList<ModelOption> Models(string agentId) => agentId == Participants.GeminiId
        ? new[] { new ModelOption("gemini-4-argon-medium", "Argon(中)"), new ModelOption("gemini-3.8-flash-low", "Flash(低)") }
        : new[] { new ModelOption("sonnet", "sonnet"), new ModelOption("opus", "opus") };

    private ScheduleEditViewModel Editor(ScheduledJob? job = null) => new(job, () => _now, Models);

    private (ScheduleViewModel Vm, MeetingRooms Rooms) CreateVm(params ScheduledJob[] jobs)
    {
        var rooms = new MeetingRooms(() =>
        {
            var session = new FakeSession
            {
                Script = _ => new[] { new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "ok") },
            };
            _sessions.Add(session);
            return new MainViewModel(() => session);
        }, 10);
        var store = new ScheduleStore(Path.Combine(_dir, "schedule.json"));
        store.Save(new ScheduleFile(jobs));
        return (new ScheduleViewModel(rooms, store, () => _now), rooms);
    }

    private static ScheduledJob Job(string id, string? model = null) =>
        new(id, "予約 " + id, "0 4 * * *", Participants.GeminiId, PermissionLevelOption.PartialId, "実行して", false, model);

    /// <summary>偽の会議で動かせる予約(偽の会議では、書き換えできるのはクロだけ)。</summary>
    private static ScheduledJob RunnableJob(string id, string? model = null, string agent = Participants.ClaudeId,
        string level = PermissionLevelOption.PartialId) => Job(id, model) with { Agent = agent, Level = level };

    // ---- 編集の窓の状態 ----

    [Fact]
    public void 新しい予約は_OFFで始まり_名前とプロンプトが空なので保存できない()
    {
        var editor = Editor();

        Assert.False(editor.Enabled);
        Assert.Equal("名前を入れてください。", editor.ValidationError);
        editor.Name = "朝のチェック";
        Assert.Contains("プロンプト", editor.ValidationError);
        editor.Prompt = "手順を実行して";
        Assert.Equal("", editor.ValidationError);
    }

    [Fact]
    public void cron式を入れると_次の3回が出る_書き間違いは理由が出る()
    {
        var editor = Editor();
        editor.Cron = "0 0,2 * * *";
        string Show(string text) => DateTime.Parse(text).ToString("M/d(ddd) HH:mm"); // 曜日の言葉は、パソコンの言語設定に合わせる
        Assert.Equal($"次の3回: {Show("2026-11-06 00:00")} / {Show("2026-11-06 02:00")} / {Show("2026-11-07 00:00")}", editor.CronPreview);

        editor.Cron = "毎日4時";
        Assert.StartsWith("⚠", editor.CronPreview);
        editor.Name = "x";
        editor.Prompt = "y";
        Assert.NotEmpty(editor.ValidationError);
        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void 新規の保存は_識別名を新しく付け_編集は識別名を引き継ぐ()
    {
        var created = Editor();
        created.Name = " 朝 ";
        created.Prompt = " 実行 ";
        var a = created.Build();
        var b = Editor().Also(e => { e.Name = "n"; e.Prompt = "p"; }).Build();
        Assert.StartsWith("job-", a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal("朝", a.Name); // 前後の空白は取る
        Assert.Equal("実行", a.Prompt);

        var edited = Editor(Job("keep")).Also(e => e.Name = "改名").Build();
        Assert.Equal("keep", edited.Id);
        Assert.Equal("改名", edited.Name);
    }

    [Fact]
    public void 相手を変えると_段階の名前とモデルの選択肢がその相手のものに変わる()
    {
        var editor = Editor(); // ジェミから始まる
        Assert.Equal("読み書き", editor.LevelOptions.Single(o => o.Id == PermissionLevelOption.PartialId).Label);
        Assert.Contains(editor.ModelChoices, c => c.Value == "" && c.Label.Contains("agy"));
        Assert.Contains(editor.ModelChoices, c => c.Value == "gemini-3.8-flash-low");

        editor.SelectedAgentId = Participants.ClaudeId;
        Assert.Equal("読み書き", editor.LevelOptions.Single(o => o.Id == PermissionLevelOption.PartialId).Label);
        Assert.DoesNotContain(editor.ModelChoices, c => c.Value == ""); // 「指定なし」はジェミだけ
        Assert.Contains(editor.ModelChoices, c => c.Value == "opus");
        Assert.Null(editor.SelectedModel.Value); // 相手を変えたら、ふだんのままに戻る
    }

    [Fact]
    public void チャッピーはモデルを選べず_保存してもモデル指定は付かない()
    {
        var editor = Editor();
        editor.Name = "n";
        editor.Prompt = "p";
        editor.SelectedAgentId = Participants.ChappyId;
        Assert.False(editor.CanChooseModel);
        Assert.Null(editor.Build().Model);
    }

    [Fact]
    public void モデルの3つの指定が_そのまま予約に入り_読み直しても選ばれている()
    {
        // 省略=ふだんのまま / 空=指定なし / 名前=そのモデル
        foreach (var model in new string?[] { null, "", "gemini-3.8-flash-low" })
        {
            var editor = Editor();
            editor.Name = "n";
            editor.Prompt = "p";
            editor.SelectedModel = editor.ModelChoices.Single(c => c.Value == model);
            var job = editor.Build();
            Assert.Equal(model, job.Model);
            Assert.Equal(model, Editor(job).SelectedModel.Value);
        }
    }

    [Fact]
    public void 一覧にないモデルが書かれていても_消さずに選択肢へ足す()
    {
        var editor = Editor(Job("old", model: "gemini-0-old"));
        Assert.Equal("gemini-0-old", editor.SelectedModel.Value);
        Assert.Contains("一覧にありません", editor.SelectedModel.Label);
    }

    [Fact]
    public void 相手に使えない段階は_読むだけに戻る()
    {
        var editor = Editor(Job("a") with { Level = "bogus" });
        Assert.Equal(PermissionLevelOption.ReadId, editor.SelectedLevelId);
    }

    // ---- 予約の追加・更新・削除 ----

    [Fact]
    public void 追加と編集と削除が_保存され_読み直しても残る()
    {
        var (vm, _) = CreateVm(Job("a"));

        vm.AddOrUpdate(Job("b"));
        Assert.Equal(new[] { "a", "b" }, vm.Items.Select(i => i.Job.Id));

        vm.AddOrUpdate(Job("a") with { Name = "改名した" }); // 同じ識別名は置き換え
        Assert.Equal(new[] { "a", "b" }, vm.Items.Select(i => i.Job.Id));
        Assert.Equal("改名した", vm.Items[0].Name);

        vm.Delete("b");
        var saved = new ScheduleStore(Path.Combine(_dir, "schedule.json")).Load().Jobs;
        Assert.Equal(new[] { "a" }, saved.Select(j => j.Id));
        Assert.Equal("改名した", saved[0].Name);
    }

    [Fact]
    public void 新しい予約を足すと_時計役にも入り_次の時刻が出る()
    {
        var (vm, _) = CreateVm();
        vm.AddOrUpdate(Job("a") with { Enabled = true });

        Assert.StartsWith("11/6", vm.Items.Single().NextRunText);
    }

    [Fact]
    public void 保存に失敗したら_画面の一覧は変えず知らせる()
    {
        var (vm, _) = CreateVm(Job("a"));
        // 保存先の schedule.json を、フォルダにして書き込めなくする
        File.Delete(Path.Combine(_dir, "schedule.json"));
        Directory.CreateDirectory(Path.Combine(_dir, "schedule.json"));

        vm.AddOrUpdate(Job("b"));

        Assert.Contains("保存できませんでした", vm.ErrorText);
        Assert.Equal(new[] { "a" }, vm.Items.Select(i => i.Job.Id));
    }

    // ---- モデルの指定は、その部屋だけ ----

    [Fact]
    public async Task 予約のモデル指定は_その会議室に伝わり_指定なしなら何も変えない()
    {
        var (vm, _) = CreateVm(RunnableJob("withModel", model: "opus"), RunnableJob("usual"));

        await vm.RunNowAsync(vm.Items[0].Job);
        await vm.RunNowAsync(vm.Items[1].Job);

        Assert.Equal(new[] { (Participants.ClaudeId, "opus") }, _sessions[1].ModelChanges);
        Assert.Empty(_sessions[2].ModelChanges); // 指定なし(null)の予約は、モデルを触らない
        Assert.Equal(ScheduleRunRecord.Ok, vm.History[0].Result);
    }

    [Fact]
    public async Task チャッピーにモデルを指定した予約も_モデルの理由では失敗にしない()
    {
        var (vm, _) = CreateVm(RunnableJob("c", model: "x", agent: Participants.ChappyId, level: PermissionLevelOption.ReadId));

        await vm.RunNowAsync(vm.Items.Single().Job);

        Assert.DoesNotContain("モデルを選べません", vm.History.Single().Note ?? ""); // チャッピーもモデルを選べるようになった
    }

    [Fact]
    public void この会議だけのモデル切り替えは_設定の保存を呼ばない()
    {
        using var temp = new TempDir();
        var saved = new List<(string, string, string)>();
        var gemini = new FakeAgent(Participants.GeminiId, "Gemini", (_, _, _) => new AgentReply("x", "g"));
        var session = new ChatSession(new[] { gemini }, new MarkdownLogWriter(temp.Path, DateTime.Now), 4000,
            onModelOrEffortChanged: (a, m, e) => saved.Add((a, m, e)));

        session.SetModelForThisMeeting(Participants.GeminiId, "gemini-3.8-flash-low");
        Assert.Equal("gemini-3.8-flash-low", session.ModelIdOf(Participants.GeminiId));
        Assert.Empty(saved); // ふだんのモデルの設定は書き換えない

        session.SetModel(Participants.GeminiId, "gemini-4-argon-medium"); // 画面での選び直しは、これまでどおり保存する
        Assert.Single(saved);
    }
}

internal static class ObjectExtensions
{
    /// <summary>オブジェクトに処理をして、そのオブジェクトを返す(試験の書きやすさのため)。</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
