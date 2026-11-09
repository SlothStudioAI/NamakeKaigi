using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>予約の実行(新しい部屋を開いて送る・画面を奪わない・失敗をごまかさない・上限で見送る)の試験。</summary>
public class ScheduleViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "namake_schedvm_" + Guid.NewGuid().ToString("N"));
    private readonly List<FakeSession> _sessions = new();
    private DateTime _now = DateTime.Parse("2026-11-05 23:59:40");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private (ScheduleViewModel Vm, MeetingRooms Rooms) Create(int maxRooms, Func<string, ChatProgress[]>? script, params ScheduledJob[] jobs)
    {
        var rooms = new MeetingRooms(() =>
        {
            var session = new FakeSession { Script = script };
            _sessions.Add(session);
            return new MainViewModel(() => session);
        }, maxRooms);
        var store = new ScheduleStore(Path.Combine(_dir, "schedule.json"));
        store.Save(new ScheduleFile(jobs, GraceMinutes: 5));
        return (new ScheduleViewModel(rooms, store, () => _now), rooms);
    }

    private static ScheduledJob Job(string id, string agent = Participants.ClaudeId, string level = PermissionLevelOption.PartialId,
        string cron = "0 0 * * *") => new(id, "台本 " + id, cron, agent, level, "手順書を開いて実行して");

    private static ChatProgress[] Reply(string text) =>
        new[] { new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, text) };

    [Fact]
    public async Task 時刻になると新しい部屋で実行し_いま見ている画面は切り替えない()
    {
        var (vm, rooms) = Create(3, _ => Reply("できました"), Job("write"));
        var viewed = rooms.Current;

        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();

        Assert.Equal(2, rooms.Items.Count);
        Assert.Same(viewed, rooms.Current);
        var room = rooms.Items[1];
        Assert.True(room.IsScheduledRoom);
        Assert.Equal("自動 台本 write 0000", room.RoomTitle);
        Assert.Equal("手順書を開いて実行して", _sessions[1].Sent.Single().Text);
        Assert.Equal(new[] { Participants.ClaudeId }, _sessions[1].Sent.Single().Targets);
        var record = vm.History.Single();
        Assert.Equal(ScheduleRunRecord.Ok, record.Result);
        Assert.False(record.Manual);
        Assert.Equal(DateTime.Parse("2026-11-06 00:00"), record.ScheduledAt);
        Assert.Contains("✔ 返答あり", vm.Items.Single().LastResultText);
    }

    [Fact]
    public async Task 履歴はファイルにも残り_読み直しても見える()
    {
        var (vm, rooms) = Create(3, _ => Reply("ok"), Job("write"));
        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();

        var again = new ScheduleViewModel(rooms, new ScheduleStore(Path.Combine(_dir, "schedule.json")), () => _now);
        Assert.Single(again.History);
        Assert.Equal("write", again.History[0].JobId);
    }

    [Fact]
    public async Task エラーの返答は失敗として記録する()
    {
        var (vm, _) = Create(3, _ => new[] { new ChatProgress(Participants.ClaudeId, ChatProgressKind.Error, "タイムアウトしました") },
            Job("write"));
        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();

        var record = vm.History.Single();
        Assert.Equal(ScheduleRunRecord.Error, record.Result);
        Assert.Contains("タイムアウト", record.Note);
    }

    [Fact]
    public async Task 使えない段階は_別の段階で黙って動かさず失敗にする()
    {
        // テスト用の会議では、Claudeは「何でもできる」にできない
        var (vm, _) = Create(3, _ => Reply("ok"), Job("full", level: PermissionLevelOption.FullId));
        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();

        var record = vm.History.Single();
        Assert.Equal(ScheduleRunRecord.Error, record.Result);
        Assert.Contains("full", record.Note);
        Assert.Empty(_sessions[1].Sent); // 何も送っていない
    }

    [Fact]
    public async Task 知らない相手は失敗にする()
    {
        var (vm, _) = Create(3, _ => Reply("ok"), Job("x", agent: "nobody"));
        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();

        Assert.Equal(ScheduleRunRecord.Error, vm.History.Single().Result);
    }

    [Fact]
    public async Task 部屋が上限なら見送り_予約で開いた古い部屋があれば閉じて場所を空ける()
    {
        var (vm, rooms) = Create(2, _ => Reply("ok"), Job("a", cron: "0 0 * * *"), Job("b", cron: "0 1 * * *"));

        _now = DateTime.Parse("2026-11-06 00:00:10"); // 1つ目 → 2部屋になり上限
        await vm.TickAsync();
        Assert.Equal(2, rooms.Items.Count);

        _now = DateTime.Parse("2026-11-06 01:00:10"); // 2つ目 → 1つ目の予約の部屋を閉じて開く
        await vm.TickAsync();
        Assert.Equal(2, rooms.Items.Count);
        Assert.Equal("自動 台本 b 0100", rooms.Items[1].RoomTitle);
        Assert.Equal(1, _sessions[1].ShutdownCount); // 閉じた部屋の裏のCLIは終わらせている
        Assert.All(vm.History, r => Assert.Equal(ScheduleRunRecord.Ok, r.Result));
    }

    [Fact]
    public async Task 場所を空けられないときは見送りと記録する()
    {
        // 上限1部屋: 人が使う部屋しかない
        var (vm, rooms) = Create(1, _ => Reply("ok"), Job("a"));
        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();

        Assert.Single(rooms.Items);
        var record = vm.History.Single();
        Assert.Equal(ScheduleRunRecord.Skipped, record.Result);
        Assert.Contains("上限", record.Note);
    }

    [Fact]
    public async Task 遅れすぎは見送りと記録する()
    {
        var (vm, rooms) = Create(3, _ => Reply("ok"), Job("a"));
        _now = DateTime.Parse("2026-11-06 00:40:00"); // 0時から40分後に初めて気づいた(許す遅れは5分)
        await vm.TickAsync();

        Assert.Single(rooms.Items);
        Assert.Equal(ScheduleRunRecord.Skipped, vm.History.Single().Result);
        Assert.Contains("遅れ", vm.History.Single().Note);
    }

    [Fact]
    public async Task 前の実行が終わっていない予約は_重ねて動かさない()
    {
        var (vm, _) = Create(3, script: null, Job("slow")); // 返答が来るまで終わらない
        _now = DateTime.Parse("2026-11-06 00:00:10");
        var first = vm.TickAsync(); // 返答待ちで止まる
        Assert.True(vm.Items.Single().IsRunning);

        var second = vm.RunNowAsync(vm.Items.Single().Job); // 手動で重ねる
        await second;

        Assert.Equal(ScheduleRunRecord.Skipped, vm.History[0].Result);
        Assert.Contains("終わっていない", vm.History[0].Note);

        _sessions[1].Finish(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "遅れて完了"));
        await first;
        Assert.False(vm.Items.Single().IsRunning);
        Assert.Equal(ScheduleRunRecord.Ok, vm.History[0].Result);
    }

    [Fact]
    public async Task ONとOFFを切り替えると保存され_OFFは動かない()
    {
        var (vm, rooms) = Create(3, _ => Reply("ok"), Job("a"));
        vm.Items.Single().Enabled = false;

        Assert.False(new ScheduleStore(Path.Combine(_dir, "schedule.json")).Load().Jobs.Single().Enabled);
        Assert.Equal("止まっています", vm.Items.Single().NextRunText);

        _now = DateTime.Parse("2026-11-06 00:00:10");
        await vm.TickAsync();
        Assert.Single(rooms.Items);
        Assert.Empty(vm.History);

        vm.Items.Single().Enabled = true;
        Assert.StartsWith("11/", vm.Items.Single().NextRunText);
    }

    [Fact]
    public async Task 手動の今すぐ実行は_OFFの予約でも動く()
    {
        var (vm, rooms) = Create(3, _ => Reply("ok"), Job("a") with { Enabled = false });
        await vm.RunNowAsync(vm.Items.Single().Job);

        Assert.Equal(2, rooms.Items.Count);
        Assert.True(vm.History.Single().Manual);
    }

    [Fact]
    public void 保存ファイルが壊れていたら_止めて知らせる()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "schedule.json"), "{ 壊れている");
        var rooms = new MeetingRooms(() => new MainViewModel(() => new FakeSession()), 3);

        var vm = new ScheduleViewModel(rooms, new ScheduleStore(Path.Combine(_dir, "schedule.json")), () => _now);

        Assert.Contains("読めません", vm.ErrorText);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void 保存ファイルが壊れているあいだは_追加と削除で上書きしない_直して読み直せば保存できる()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "schedule.json");
        const string broken = "{ \"jobs\": [ 壊れている";
        File.WriteAllText(path, broken);
        var rooms = new MeetingRooms(() => new MainViewModel(() => new FakeSession()), 3);
        var vm = new ScheduleViewModel(rooms, new ScheduleStore(path), () => _now);

        vm.AddOrUpdate(Job("new"));
        vm.Delete("new");

        Assert.Equal(broken, File.ReadAllText(path)); // 壊れたファイルは、そのまま
        Assert.Contains("保存しませんでした", vm.ErrorText);

        File.WriteAllText(path, "{ \"jobs\": [] }");
        vm.Reload();
        vm.AddOrUpdate(Job("new"));

        Assert.Single(vm.Items);
        Assert.Contains("\"new\"", File.ReadAllText(path));
    }

    [Fact]
    public void 末尾のカンマやコメントがあっても_保存ファイルを読める()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "schedule.json");
        File.WriteAllText(path, "{ // 手で書き足した\n \"graceMinutes\": 7, \"jobs\": [], }");

        var file = new ScheduleStore(path).Load();

        Assert.Equal(7, file.GraceMinutes);
    }

    [Fact]
    public void cron式が読めない予約は_理由を出して動かさない()
    {
        var (vm, _) = Create(3, _ => Reply("ok"), Job("bad", cron: "毎日"));
        Assert.StartsWith("⚠", vm.Items.Single().NextRunText);
    }
}
