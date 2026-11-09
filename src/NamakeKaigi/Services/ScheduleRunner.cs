using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// 予約の時計役。「いま何時か」を渡されるたびに、前に見た時刻からいままでの間に予定の時刻が来た予約を返す。
/// 時計や画面を知らない(試験では時刻を直接渡す)。
/// </summary>
public sealed class ScheduleRunner
{
    private readonly Dictionary<string, CronExpression> _crons = new();
    private readonly Dictionary<string, string> _cronErrors = new();
    private IReadOnlyList<ScheduledJob> _jobs = Array.Empty<ScheduledJob>();
    private TimeSpan _grace;
    private DateTime _lastCheck;

    /// <summary>時計役を作る。</summary>
    /// <param name="start">動き始めた時刻(これより前の予定は見ない)</param>
    /// <param name="graceMinutes">予定から、この分数までの遅れなら実行する</param>
    public ScheduleRunner(DateTime start, int graceMinutes)
    {
        _lastCheck = start;
        _grace = TimeSpan.FromMinutes(Math.Max(0, graceMinutes));
    }

    /// <summary>見ている予約の一覧。</summary>
    public IReadOnlyList<ScheduledJob> Jobs => _jobs;

    /// <summary>予約の一覧を入れ替える(cron式を読み直す。読めない式の予約は、動かさず、理由を覚えておく)。</summary>
    /// <param name="jobs">新しい予約の一覧</param>
    /// <param name="graceMinutes">遅れを許す分数</param>
    public void SetJobs(IReadOnlyList<ScheduledJob> jobs, int graceMinutes)
    {
        _jobs = jobs;
        _grace = TimeSpan.FromMinutes(Math.Max(0, graceMinutes));
        _crons.Clear();
        _cronErrors.Clear();
        foreach (var job in jobs)
        {
            if (CronExpression.TryParse(job.Cron, out var cron, out var error)) _crons[job.Id] = cron!;
            else _cronErrors[job.Id] = error;
        }
    }

    /// <summary>cron式が読めなかった予約の理由(読めていれば空)。</summary>
    /// <param name="jobId">予約の識別名</param>
    /// <returns>理由</returns>
    public string CronError(string jobId) => _cronErrors.GetValueOrDefault(jobId, "");

    /// <summary>次に動く時刻(動かない・式が読めない予約はnull)。</summary>
    /// <param name="job">予約</param>
    /// <param name="from">この時刻より後を探す</param>
    /// <returns>次の時刻</returns>
    public DateTime? NextRun(ScheduledJob job, DateTime from) =>
        job.Enabled && _crons.TryGetValue(job.Id, out var cron) ? cron.Next(from) : null;

    /// <summary>前に見た時刻からいままでの間に、予定の時刻が来た予約を返す(1つの予約は、1回の確認で1回まで)。</summary>
    /// <param name="now">いまの時刻</param>
    /// <returns>動かす予約と、見送る予約(遅れが許す範囲を超えたもの)</returns>
    public ScheduleTick Check(DateTime now)
    {
        var due = new List<DueJob>();
        var missed = new List<DueJob>();
        if (now <= _lastCheck) return new ScheduleTick(due, missed);
        foreach (var job in _jobs.Where(j => j.Enabled))
        {
            if (!_crons.TryGetValue(job.Id, out var cron)) continue;
            // 何分も空いた(スリープ明けなど)ときは、その間の最後の予定を対象にする
            DateTime? scheduled = null;
            for (var t = cron.Next(_lastCheck); t is not null && t <= now; t = cron.Next(t.Value)) scheduled = t;
            if (scheduled is null) continue;
            (now - scheduled.Value <= _grace ? due : missed).Add(new DueJob(job, scheduled.Value));
        }
        _lastCheck = now;
        return new ScheduleTick(due, missed);
    }
}

/// <summary>時刻が来た予約と、その予定の時刻。</summary>
/// <param name="Job">予約</param>
/// <param name="ScheduledAt">予定の時刻</param>
public sealed record DueJob(ScheduledJob Job, DateTime ScheduledAt);

/// <summary>1回の確認の結果。</summary>
/// <param name="Due">いま動かす予約</param>
/// <param name="Missed">遅れが大きすぎて見送る予約</param>
public sealed record ScheduleTick(IReadOnlyList<DueJob> Due, IReadOnlyList<DueJob> Missed);
