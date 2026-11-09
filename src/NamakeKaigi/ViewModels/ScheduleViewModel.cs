using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>「自動実行」窓の1行(予約1件の表示と、ON/OFF・今すぐ実行)。</summary>
public sealed class ScheduleItemViewModel : ViewModelBase
{
    private readonly ScheduleViewModel _owner;
    private ScheduledJob _job;
    private string _nextRunText = "";
    private string _lastResultText = "";
    private bool _isRunning;

    /// <summary>1行を作る。</summary>
    /// <param name="owner">予約の一覧</param>
    /// <param name="job">予約</param>
    public ScheduleItemViewModel(ScheduleViewModel owner, ScheduledJob job)
    {
        _owner = owner;
        _job = job;
        RunNowCommand = new RelayCommand(() => _ = _owner.RunNowAsync(Job), () => !IsRunning);
    }

    /// <summary>予約の中身。</summary>
    public ScheduledJob Job
    {
        get => _job;
        internal set
        {
            _job = value;
            Raise(nameof(Job));
            Raise(nameof(Name));
            Raise(nameof(Enabled));
            Raise(nameof(ModelText));
        }
    }

    /// <summary>名前。</summary>
    public string Name => _job.Name;

    /// <summary>実行する時刻のcron式。</summary>
    public string Cron => _job.Cron;

    /// <summary>相手と段階の表示(例: ジェミリー・何でもできる)。</summary>
    public string AgentText
    {
        get
        {
            var participant = Participants.All.FirstOrDefault(p => p.Id == _job.Agent);
            var level = PermissionLevelOption.For(canFull: true).FirstOrDefault(o => o.Id == _job.Level)?.Label ?? _job.Level;
            return $"{participant?.DisplayName ?? _job.Agent}・{level}";
        }
    }

    /// <summary>モデルの表示(省略=ふだんの設定のまま / 空=指定なし / モデル名)。</summary>
    public string ModelText => _job.Model is null ? "ふだんのまま" : _job.Model.Length == 0 ? "指定なし" : _job.Model;

    /// <summary>送る本文。</summary>
    public string Prompt => _job.Prompt;

    /// <summary>動かすか。切り替えると、保存ファイルにも書く。</summary>
    public bool Enabled
    {
        get => _job.Enabled;
        set
        {
            if (value == _job.Enabled) return;
            _owner.SetEnabled(_job.Id, value);
        }
    }

    /// <summary>次に動く時刻の表示(OFFなら「止まっています」、式が読めなければその理由)。</summary>
    public string NextRunText
    {
        get => _nextRunText;
        internal set => Set(ref _nextRunText, value);
    }

    /// <summary>最後の実行結果の表示。</summary>
    public string LastResultText
    {
        get => _lastResultText;
        internal set => Set(ref _lastResultText, value);
    }

    /// <summary>いま実行中か。</summary>
    public bool IsRunning
    {
        get => _isRunning;
        internal set
        {
            if (!Set(ref _isRunning, value)) return;
            ((RelayCommand)RunNowCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>今すぐ実行する命令。</summary>
    public ICommand RunNowCommand { get; }
}

/// <summary>
/// 「自動実行」窓の状態。予約の一覧を読み、時刻になったら新しい会議室で実行し、結果を履歴に残す。
/// 時計(いまの時刻の取得と、定期的に Tick を呼ぶこと)は外から渡す。
/// </summary>
public sealed class ScheduleViewModel : ViewModelBase
{
    private readonly MeetingRooms _rooms;
    private readonly ScheduleStore _store;
    private readonly Func<DateTime> _clock;
    private readonly HashSet<string> _running = new();
    private ScheduleFile _file = ScheduleFile.Empty;
    private bool _loadFailed; // 保存ファイルを読めなかったとき true(空の一覧で、壊れたファイルを上書きしないため)
    private ScheduleRunner _runner;
    private string _errorText = "";

    /// <summary>作る。</summary>
    /// <param name="rooms">会議室の一覧(予約はここに部屋を開いて実行する)</param>
    /// <param name="store">予約と履歴の保存先</param>
    /// <param name="clock">いまの時刻を返す関数</param>
    public ScheduleViewModel(MeetingRooms rooms, ScheduleStore store, Func<DateTime> clock)
    {
        _rooms = rooms;
        _store = store;
        _clock = clock;
        _runner = new ScheduleRunner(clock(), 5);
        ReloadCommand = new RelayCommand(Reload);
        Reload();
    }

    /// <summary>予約の一覧(画面の行)。</summary>
    public ObservableCollection<ScheduleItemViewModel> Items { get; } = new();

    /// <summary>実行の履歴(新しい順)。</summary>
    public ObservableCollection<ScheduleRunRecord> History { get; } = new();

    /// <summary>保存ファイルが読めないなどの、気づいてほしい知らせ(なければ空)。</summary>
    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    /// <summary>気づいてほしい知らせを画面に出す(想定外の失敗を、黙って捨てないため)。</summary>
    /// <param name="message">知らせの文</param>
    public void ReportError(string message) => ErrorText = message;

    /// <summary>保存ファイルの場所(画面に出して、直接開けるようにする)。</summary>
    public string FilePath => _store.Path;

    /// <summary>時刻を見に行く間隔(秒。保存ファイルの値)。</summary>
    public int TickSec => Math.Max(1, _file.TickSec);

    /// <summary>保存ファイルを読み直す命令。</summary>
    public ICommand ReloadCommand { get; }

    /// <summary>保存ファイルを読み直して、一覧と次の時刻を作り直す(動かし始めた時刻は、そのまま)。</summary>
    public void Reload()
    {
        try
        {
            _file = _store.Load();
            _loadFailed = false;
            ErrorText = "";
        }
        catch (Exception ex)
        {
            // 壊れているときは、前に読めた内容のまま動かし続けず、止めて知らせる(気づかないまま夜が空振りしないように)
            _file = ScheduleFile.Empty;
            _loadFailed = true;
            ErrorText = ex.Message;
        }
        _runner.SetJobs(_file.Jobs, _file.GraceMinutes);
        RebuildItems();
        RefreshHistory();
        RefreshTexts();
    }

    /// <summary>時刻を見て、来ている予約を実行する(タイマーが、TickSec ごとに呼ぶ)。実行が終わるまで待つ。</summary>
    public async Task TickAsync()
    {
        var now = _clock();
        var tick = _runner.Check(now);
        foreach (var missed in tick.Missed)
        {
            Record(new ScheduleRunRecord(missed.Job.Id, missed.Job.Name, missed.ScheduledAt, now, now, ScheduleRunRecord.Skipped,
                $"予定の時刻から{_file.GraceMinutes}分以上遅れて気づいたため、見送りました(スリープ中だった可能性があります)。", false, ""));
        }
        var runs = tick.Due.Select(d => RunAsync(d.Job, d.ScheduledAt, manual: false)).ToList();
        RefreshTexts();
        await Task.WhenAll(runs);
        RefreshTexts();
    }

    /// <summary>「今すぐ実行」。予定とは別に、いま1回実行する。</summary>
    /// <param name="job">実行する予約(OFFでも、手動なら実行する)</param>
    public Task RunNowAsync(ScheduledJob job) => RunAsync(job, _clock(), manual: true);

    /// <summary>予約のON/OFFを切り替えて、保存する。</summary>
    /// <param name="jobId">予約の識別名</param>
    /// <param name="enabled">動かすならtrue</param>
    public void SetEnabled(string jobId, bool enabled)
    {
        var jobs = _file.Jobs.Select(j => j.Id == jobId ? j with { Enabled = enabled } : j).ToList();
        // 切り替えた行は、その場で更新する(行を作り直すと、いま押しているチェックが消えて見えるため)
        if (!Persist(jobs, rebuild: false)) return;
        foreach (var item in Items.Where(i => i.Job.Id == jobId)) item.Job = jobs.First(j => j.Id == jobId);
        RefreshTexts();
    }

    /// <summary>予約を足す。識別名がすでにあれば、その予約を置き換える(編集)。保存して、一覧を作り直す。</summary>
    /// <param name="job">足す(または置き換える)予約</param>
    public void AddOrUpdate(ScheduledJob job)
    {
        var jobs = _file.Jobs.ToList();
        var index = jobs.FindIndex(j => j.Id == job.Id);
        if (index >= 0) jobs[index] = job;
        else jobs.Add(job);
        Persist(jobs);
    }

    /// <summary>予約を消して、保存する(過去の実行の履歴は残す)。</summary>
    /// <param name="jobId">消す予約の識別名</param>
    public void Delete(string jobId) => Persist(_file.Jobs.Where(j => j.Id != jobId).ToList());

    /// <summary>追加・編集の窓の状態を作る。選べるモデルは、いま見ている会議室のAIの一覧を使う。</summary>
    /// <param name="job">編集する予約(新しく足すならnull)</param>
    /// <returns>編集の状態</returns>
    public ScheduleEditViewModel CreateEditor(ScheduledJob? job) =>
        new(job, _clock, agentId =>
            _rooms.Current.Members.FirstOrDefault(m => m.Participant.Id == agentId)?.ModelOptions.ToList()
            ?? new List<ModelOption>());

    /// <summary>予約の一覧を保存して、時計役と画面の一覧に反映する(保存に失敗したら、画面の一覧は変えず、知らせる)。</summary>
    /// <param name="jobs">新しい予約の一覧</param>
    /// <param name="rebuild">保存できたあと、画面の一覧を作り直すか</param>
    /// <returns>保存できたらtrue</returns>
    private bool Persist(List<ScheduledJob> jobs, bool rebuild = true)
    {
        if (_loadFailed)
        {
            // 読めなかったファイルは、直してもらうまで触らない(いま保存すると、それまでの予約がすべて消える)
            ErrorText = $"予約のファイルを読めないため、保存しませんでした。ファイルを直してから「読み直す」を押してください({_store.Path})";
            RebuildItems();
            RefreshTexts();
            return false;
        }
        var next = _file with { Jobs = jobs };
        try
        {
            _store.Save(next);
        }
        catch (Exception ex)
        {
            ErrorText = $"予約を保存できませんでした: {ex.Message}";
            RebuildItems(); // 画面のON/OFFなどを、保存されている状態に戻す
            RefreshTexts();
            return false;
        }
        _file = next;
        _runner.SetJobs(_file.Jobs, _file.GraceMinutes);
        if (rebuild)
        {
            RebuildItems();
            RefreshTexts();
        }
        return true;
    }

    /// <summary>画面の一覧を、いまの予約から作り直す。</summary>
    private void RebuildItems()
    {
        Items.Clear();
        foreach (var job in _file.Jobs)
        {
            var item = new ScheduleItemViewModel(this, job);
            item.IsRunning = _running.Contains(job.Id);
            Items.Add(item);
        }
    }

    private async Task RunAsync(ScheduledJob job, DateTime scheduledAt, bool manual)
    {
        if (!_running.Add(job.Id))
        {
            var now = _clock();
            Record(new ScheduleRunRecord(job.Id, job.Name, scheduledAt, now, now, ScheduleRunRecord.Skipped,
                "前の実行がまだ終わっていないため、見送りました。", manual, ""));
            return;
        }
        SetRunning(job.Id, true);
        try
        {
            Record(await _rooms.RunScheduledAsync(job, scheduledAt, manual, _clock));
        }
        finally
        {
            _running.Remove(job.Id);
            SetRunning(job.Id, false);
        }
    }

    private void SetRunning(string jobId, bool running)
    {
        foreach (var item in Items.Where(i => i.Job.Id == jobId)) item.IsRunning = running;
    }

    /// <summary>結果を履歴ファイルと画面に足す。</summary>
    private void Record(ScheduleRunRecord record)
    {
        try
        {
            _store.AppendHistory(_store.HistoryPath(_file), record);
        }
        catch (Exception ex)
        {
            ErrorText = $"実行の記録を書けませんでした: {ex.Message}";
        }
        History.Insert(0, record);
        RefreshTexts();
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var r in _store.ReadRecentHistory(_store.HistoryPath(_file), 50)) History.Add(r);
    }

    /// <summary>各行の「次の時刻」「最後の結果」の表示を作り直す。</summary>
    private void RefreshTexts()
    {
        var now = _clock();
        foreach (var item in Items)
        {
            var error = _runner.CronError(item.Job.Id);
            var next = _runner.NextRun(item.Job, now);
            item.NextRunText = error.Length > 0 ? "⚠ " + error
                : !item.Job.Enabled ? "止まっています"
                : next is null ? "(次の予定なし)"
                : next.Value.ToString("M/d(ddd) HH:mm");
            var last = History.FirstOrDefault(r => r.JobId == item.Job.Id);
            item.LastResultText = last is null ? "まだ実行していません"
                : $"{last.StartedAt:M/d HH:mm} {last.ResultLabel}" + (last.Note.Length > 0 ? $"({last.Note})" : "");
        }
    }

}
