using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NamakeKaigi.Models;

namespace NamakeKaigi.ViewModels;

/// <summary>
/// 会議室(タブ)の一覧。部屋ごとに別の会議(別のCLIの会話)を持ち、別の話題を同時に進められる。
/// 起動時は1部屋。上限は設定ファイルで決める。画面は、いま選ばれている部屋の MainViewModel を映す。
/// </summary>
public sealed class MeetingRooms : ViewModelBase
{
    private readonly Func<MainViewModel> _createRoom;
    private readonly int _maxRooms;
    private MainViewModel _current;
    private bool _syncing;

    /// <summary>部屋の一覧を作り、最初の1部屋を開く。</summary>
    /// <param name="createRoom">新しい部屋(会議室1つ分の画面の状態)を作る関数</param>
    /// <param name="maxRooms">同時に開ける部屋の上限(1未満は1にそろえる)</param>
    public MeetingRooms(Func<MainViewModel> createRoom, int maxRooms)
    {
        _createRoom = createRoom;
        _maxRooms = Math.Max(1, maxRooms);
        AddRoomCommand = new RelayCommand(AddRoom, () => CanAddRoom);
        _current = Attach(createRoom());
        Items.Add(_current);
        RefreshCurrentMarks();
    }

    /// <summary>開いている部屋(タブの並び順)。</summary>
    public ObservableCollection<MainViewModel> Items { get; } = new();

    /// <summary>いま画面に映している部屋。</summary>
    public MainViewModel Current => _current;

    /// <summary>画面に映す部屋が変わったときに起きる(画面は、これで表示する部屋を差し替える)。</summary>
    public event Action<MainViewModel>? CurrentChanged;

    /// <summary>部屋を1つ増やす命令。</summary>
    public ICommand AddRoomCommand { get; }

    /// <summary>部屋を増やせるか(上限に達していなければtrue)。</summary>
    public bool CanAddRoom => Items.Count < _maxRooms;

    /// <summary>部屋を閉じられるか(最後の1部屋は閉じない)。</summary>
    public bool CanCloseRoom => Items.Count > 1;

    /// <summary>新しい部屋を開いて、そこへ移る(上限なら何もしない)。</summary>
    public void AddRoom()
    {
        if (!CanAddRoom) return;
        var room = Attach(_createRoom());
        room.RoomTitle = NextTitle();
        Items.Add(room);
        NotifyCountChanged();
        Select(room);
    }

    /// <summary>
    /// 予約の実行のために、新しい部屋を開く。いま見ている画面は切り替えない(入力中の文字や、読んでいる会議を邪魔しないため)。
    /// 上限のときは、予約で開いた部屋のうち、返答待ちでなく、いま画面に映していない一番古い部屋を閉じて、場所を空ける。
    /// </summary>
    /// <param name="title">部屋の名前</param>
    /// <returns>開いた部屋(場所を空けられなければnull)</returns>
    public MainViewModel? OpenScheduledRoom(string title)
    {
        if (!CanAddRoom)
        {
            var old = Items.FirstOrDefault(r => r.IsScheduledRoom && !r.IsBusy && !ReferenceEquals(r, _current));
            if (old is null) return null;
            Close(old);
        }
        var room = Attach(_createRoom());
        room.RoomTitle = title;
        room.IsScheduledRoom = true;
        Items.Add(room);
        NotifyCountChanged();
        return room;
    }

    /// <summary>
    /// 予約1件を、新しい部屋で実行して、結果を履歴の1行にして返す。部屋を開けないときは、実行せず「見送り」にする。
    /// </summary>
    /// <param name="job">実行する予約</param>
    /// <param name="scheduledAt">予定の時刻(手動ならいまの時刻)</param>
    /// <param name="manual">手動の「今すぐ実行」ならtrue</param>
    /// <param name="clock">いまの時刻を返す関数(試験で差し替える)</param>
    /// <returns>履歴に残す1行</returns>
    public async Task<ScheduleRunRecord> RunScheduledAsync(ScheduledJob job, DateTime scheduledAt, bool manual,
        Func<DateTime> clock)
    {
        var started = clock();
        var room = OpenScheduledRoom($"自動 {job.Name} {scheduledAt:HHmm}");
        if (room is null)
            return new ScheduleRunRecord(job.Id, job.Name, scheduledAt, started, clock(), ScheduleRunRecord.Skipped,
                $"会議室が上限({_maxRooms}部屋)で、閉じてよい予約の部屋もないため、見送りました。", manual, "");
        ScheduleOutcome outcome;
        try
        {
            outcome = await room.RunScheduledAsync(job);
        }
        catch (Exception ex)
        {
            outcome = new ScheduleOutcome(false, ex.Message);
        }
        return new ScheduleRunRecord(job.Id, job.Name, scheduledAt, started, clock(),
            outcome.Ok ? ScheduleRunRecord.Ok : ScheduleRunRecord.Error, outcome.Note, manual, room.LogPath);
    }

    /// <summary>その部屋へ移る。</summary>
    /// <param name="room">移る部屋(一覧にない部屋は無視)</param>
    public void Select(MainViewModel room)
    {
        if (!Items.Contains(room) || ReferenceEquals(room, _current)) return;
        _current = room;
        RefreshCurrentMarks();
        Raise(nameof(Current));
        CurrentChanged?.Invoke(room);
    }

    /// <summary>その部屋を閉じる(返答待ちを止め、裏のCLIを終わらせる)。最後の1部屋は閉じない。</summary>
    /// <param name="room">閉じる部屋</param>
    public void Close(MainViewModel room)
    {
        if (!CanCloseRoom || !Items.Contains(room)) return;
        var index = Items.IndexOf(room);
        var wasCurrent = ReferenceEquals(room, _current);
        room.CloseRoom();
        Items.Remove(room);
        room.PropertyChanged -= OnRoomPropertyChanged;
        room.AppearanceUpdated -= OnAppearanceUpdated;
        room.Rooms = null;
        NotifyCountChanged();
        if (wasCurrent) Select(Items[Math.Min(index, Items.Count - 1)]);
    }

    /// <summary>部屋を一覧につなぐ(部屋が一覧を参照できるようにし、テーマ・表示名の変更を見張る)。</summary>
    private MainViewModel Attach(MainViewModel room)
    {
        room.Rooms = this;
        room.PropertyChanged += OnRoomPropertyChanged;
        room.AppearanceUpdated += OnAppearanceUpdated;
        return room;
    }

    /// <summary>まだ使われていない一番小さい番号で、部屋の名前を作る(例: 会議室2)。</summary>
    private string NextTitle()
    {
        var n = 1;
        while (Items.Any(r => r.RoomTitle == $"会議室{n}")) n++;
        return $"会議室{n}";
    }

    /// <summary>どの部屋が選ばれているかの印を、すべての部屋に伝える。</summary>
    private void RefreshCurrentMarks()
    {
        foreach (var room in Items) room.NotifyCurrentRoomChanged();
    }

    /// <summary>部屋の数が変わったことを、追加・閉じるボタンに伝える。</summary>
    private void NotifyCountChanged()
    {
        Raise(nameof(CanAddRoom));
        Raise(nameof(CanCloseRoom));
        ((RelayCommand)AddRoomCommand).RaiseCanExecuteChanged();
        foreach (var room in Items) room.NotifyCurrentRoomChanged();
    }

    /// <summary>配色テーマは画面全体で1つなので、ある部屋で変えたら、ほかの部屋にも合わせる。</summary>
    private void OnRoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || e.PropertyName != nameof(MainViewModel.SelectedThemeId) || sender is not MainViewModel changed) return;
        _syncing = true;
        try
        {
            foreach (var room in Items.Where(r => !ReferenceEquals(r, changed))) room.SyncTheme(changed.SelectedThemeId);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>簡易表示モード・表示名も画面全体で1つなので、ある部屋で変えたら、ほかの部屋にも合わせる。</summary>
    private void OnAppearanceUpdated(MainViewModel changed, Models.AppearanceSettings appearance)
    {
        foreach (var room in Items.Where(r => !ReferenceEquals(r, changed))) room.SyncAppearance(appearance);
    }
}
