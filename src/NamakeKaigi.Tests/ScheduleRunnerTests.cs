using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using Xunit;

namespace NamakeKaigi.Tests;

/// <summary>予約の時計役(時刻になった予約を返す)と、予約・履歴の保存の試験。</summary>
public class ScheduleRunnerTests
{
    private static ScheduledJob Job(string id, string cron, bool enabled = true) =>
        new(id, id, cron, Participants.GeminiId, PermissionLevelOption.FullId, "テスト", enabled);

    private static ScheduleRunner Runner(string start, int grace, params ScheduledJob[] jobs)
    {
        var runner = new ScheduleRunner(DateTime.Parse(start), grace);
        runner.SetJobs(jobs, grace);
        return runner;
    }

    [Fact]
    public void Check_予定の時刻をまたいだら一度だけ返す()
    {
        var runner = Runner("2026-11-04 23:59:30", 5, Job("write", "0 0,2 * * *"));
        Assert.Empty(runner.Check(DateTime.Parse("2026-11-04 23:59:45")).Due); // まだ
        var tick = runner.Check(DateTime.Parse("2026-11-05 00:00:10"));
        Assert.Equal(new[] { "write" }, tick.Due.Select(d => d.Job.Id));
        Assert.Equal(DateTime.Parse("2026-11-05 00:00"), tick.Due[0].ScheduledAt);
        Assert.Empty(runner.Check(DateTime.Parse("2026-11-05 00:00:40")).Due); // 同じ予定は、もう返さない
    }

    [Fact]
    public void Check_OFFの予約と式が読めない予約は動かさない()
    {
        var runner = Runner("2026-11-05 00:00:00", 5, Job("off", "* * * * *", enabled: false), Job("bad", "xx"));
        var tick = runner.Check(DateTime.Parse("2026-11-05 00:05:00"));
        Assert.Empty(tick.Due);
        Assert.Empty(tick.Missed);
        Assert.NotEmpty(runner.CronError("bad"));
        Assert.Equal("", runner.CronError("off"));
    }

    [Fact]
    public void Check_遅れが許す範囲なら実行し_超えたら見送る()
    {
        var runner = Runner("2026-11-05 03:50:00", 5, Job("a", "0 4 * * *"));
        // 4時を3分過ぎて確認(スリープ明けなど)→ 許す範囲(5分)なので実行
        var late = runner.Check(DateTime.Parse("2026-11-05 04:03:00"));
        Assert.Single(late.Due);
        Assert.Empty(late.Missed);

        var runner2 = Runner("2026-11-05 03:50:00", 5, Job("a", "0 4 * * *"));
        // 4時を30分過ぎて確認 → 見送る(予定の時刻は4:00と記録できる)
        var tooLate = runner2.Check(DateTime.Parse("2026-11-05 04:30:00"));
        Assert.Empty(tooLate.Due);
        Assert.Equal(DateTime.Parse("2026-11-05 04:00"), tooLate.Missed.Single().ScheduledAt);
    }

    [Fact]
    public void Check_長く空いても_同じ予約は1回だけで_最後の予定を対象にする()
    {
        var runner = Runner("2026-11-05 00:00:00", 5, Job("every", "*/10 * * * *"));
        var tick = runner.Check(DateTime.Parse("2026-11-05 01:00:30"));
        Assert.Equal(DateTime.Parse("2026-11-05 01:00"), tick.Due.Single().ScheduledAt); // 1:00が最後の予定(30秒の遅れ)
    }

    [Fact]
    public void Check_夜の5つの予約が_それぞれの時刻に1回ずつ動く()
    {
        var jobs = new[]
        {
            Job("script", "0 0,2 * * *"), Job("review", "0 1,3 * * *"), Job("make", "0 4 * * *"),
            Job("inspect", "0 5,7 * * *"), Job("fix", "0 6 * * *"),
        };
        var runner = Runner("2026-11-04 23:59:00", 5, jobs);
        var fired = new List<string>();
        for (var t = DateTime.Parse("2026-11-04 23:59:30"); t < DateTime.Parse("2026-11-05 08:00:00"); t = t.AddSeconds(30))
            foreach (var d in runner.Check(t).Due) fired.Add($"{d.ScheduledAt:HH:mm} {d.Job.Id}");
        Assert.Equal(new[]
        {
            "00:00 script", "01:00 review", "02:00 script", "03:00 review",
            "04:00 make", "05:00 inspect", "06:00 fix", "07:00 inspect",
        }, fired);
    }

    [Fact]
    public void NextRun_OFFならnull_ONなら次の時刻()
    {
        var on = Job("on", "0 4 * * *");
        var off = Job("off", "0 4 * * *", enabled: false);
        var runner = Runner("2026-11-05 00:00:00", 5, on, off);
        Assert.Equal(DateTime.Parse("2026-11-05 04:00"), runner.NextRun(on, DateTime.Parse("2026-11-05 00:00")));
        Assert.Null(runner.NextRun(off, DateTime.Parse("2026-11-05 00:00")));
    }

    [Fact]
    public void Store_保存して読み直せる_ファイルがなければ空_履歴は新しい順()
    {
        var dir = Path.Combine(Path.GetTempPath(), "namake_sched_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ScheduleStore(Path.Combine(dir, "schedule.json"));
            Assert.Empty(store.Load().Jobs);

            var file = new ScheduleFile(new[] { Job("script", "0 0,2 * * *") with { Prompt = "C:\\a\\b.md を開いて「実行」" } },
                TickSec: 20, GraceMinutes: 7);
            store.Save(file);
            var loaded = store.Load();
            Assert.Equal(20, loaded.TickSec);
            Assert.Equal(7, loaded.GraceMinutes);
            Assert.Equal("C:\\a\\b.md を開いて「実行」", loaded.Jobs.Single().Prompt);
            Assert.False(File.Exists(store.Path + ".tmp"));

            var history = store.HistoryPath(loaded);
            Assert.Equal(Path.Combine(dir, "schedule-history.jsonl"), history);
            for (var i = 1; i <= 3; i++)
                store.AppendHistory(history, new ScheduleRunRecord("script", "台本", DateTime.Parse("2026-11-05 00:00"),
                    DateTime.Parse("2026-11-05 00:00:05"), DateTime.Parse($"2026-11-05 00:0{i}:00"), ScheduleRunRecord.Ok, "n" + i,
                    false, "C:\\log.md"));
            var recent = store.ReadRecentHistory(history, 2);
            Assert.Equal(new[] { "n3", "n2" }, recent.Select(r => r.Note));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Store_壊れたファイルは黙って空にせず_例外にする()
    {
        var dir = Path.Combine(Path.GetTempPath(), "namake_sched_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "schedule.json");
            File.WriteAllText(path, "{ これはJSONではない");
            Assert.Throws<InvalidDataException>(() => new ScheduleStore(path).Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>この試験ファイルのソースの場所(コンパイル時に決まる)。</summary>
    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void 実際のschedule_jsonは読めて_式と相手と段階が正しい()
    {
        // この試験のソースの場所から親をさかのぼって、プロジェクトの schedule/schedule.json を探す(出力先がどこでも見つかる)
        var dir = new DirectoryInfo(Path.GetDirectoryName(ThisFile())!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "schedule", "schedule.json"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var file = new ScheduleStore(Path.Combine(dir!.FullName, "schedule", "schedule.json")).Load();
        Assert.NotEmpty(file.Jobs);
        Assert.Equal(file.Jobs.Count, file.Jobs.Select(j => j.Id).Distinct().Count()); // 識別名の重複なし
        foreach (var job in file.Jobs)
        {
            Assert.True(CronExpression.TryParse(job.Cron, out _, out var error), $"{job.Id}: {error}");
            Assert.Contains(job.Agent, new[] { Participants.ClaudeId, Participants.GeminiId, Participants.ChappyId });
            Assert.Contains(job.Level, new[] { PermissionLevelOption.ReadId, PermissionLevelOption.PartialId, PermissionLevelOption.FullId });
            Assert.False(string.IsNullOrWhiteSpace(job.Prompt));
            Assert.DoesNotContain(((char)92).ToString(), job.Prompt); // パスは / で書く(JSONで円記号が消える事故を防ぐ)
        }
    }
}
