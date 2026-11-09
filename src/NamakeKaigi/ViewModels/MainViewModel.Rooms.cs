using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 会議室(タブ)の名前・ログ・切り替え。</summary>
public sealed partial class MainViewModel
{
    private MeetingRooms? _rooms;

    private string _roomTitle = "会議室1";

    private readonly int _roomTitleMaxChars;

    private bool _isRenamingRoom;

    private string _roomTitleEdit = "";

    /// <summary>この部屋が属する部屋の一覧(タブの行に使う)。1部屋だけで作ったときはnull。</summary>
    public MeetingRooms? Rooms
    {
        get => _rooms;
        internal set
        {
            if (!Set(ref _rooms, value)) return;
            NotifyCurrentRoomChanged();
        }
    }

    /// <summary>この部屋の名前(タブと見出しに出す)。会議フォルダの名前にもなる(変えると、いまの会議にも伝える)。</summary>
    public string RoomTitle
    {
        get => _roomTitle;
        set
        {
            if (!Set(ref _roomTitle, string.IsNullOrWhiteSpace(value) ? _roomTitle : value.Trim())) return;
            _session.SetMeetingTitle(_roomTitle);
            RaiseLogPathChanged(); // 会議フォルダの名前が変わる
        }
    }

    /// <summary>タブの名前を、その場で書き換えている最中ならtrue。</summary>
    public bool IsRenamingRoom
    {
        get => _isRenamingRoom;
        private set => Set(ref _isRenamingRoom, value);
    }

    /// <summary>書き換え中の名前(入力欄の中身)。</summary>
    public string RoomTitleEdit
    {
        get => _roomTitleEdit;
        set => Set(ref _roomTitleEdit, value ?? "");
    }

    /// <summary>名前の書き換えを始める(入力欄に、いまの名前を入れる)。</summary>
    public void BeginRenameRoom()
    {
        RoomTitleEdit = _roomTitle;
        IsRenamingRoom = true;
    }

    /// <summary>書き換え中の名前を確定する。空なら、いまの名前のまま。</summary>
    public void CommitRenameRoom()
    {
        if (!IsRenamingRoom) return;
        IsRenamingRoom = false;
        RenameRoom(RoomTitleEdit);
    }

    /// <summary>名前の書き換えをやめる(いまの名前のまま)。</summary>
    public void CancelRenameRoom() => IsRenamingRoom = false;

    /// <summary>
    /// 会議室の名前を変える(ユーザーが書き換えたとき)。タブと見出しに出して、いまの会議(log.md の1行目と、会議フォルダの名前)にも書く。
    /// 空・空白だけなら変えない。長さは、上限(設定)で切る。
    /// </summary>
    /// <param name="name">新しい名前</param>
    public void RenameRoom(string name)
    {
        var clean = string.Join(" ", (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return;
        if (clean.Length > _roomTitleMaxChars) clean = clean[.._roomTitleMaxChars];
        RoomTitle = clean;
    }

    /// <summary>会議フォルダを用意する(まだなければ作る)。発言の前に画像を貼ったときも、この会議のフォルダに保存するため。</summary>
    /// <returns>log.md のパス(この親のフォルダが、会議フォルダ)</returns>
    public string EnsureMeetingFolder()
    {
        _session.EnsureMeetingFolder();
        RaiseLogPathChanged(); // 同じ名前の会議があれば「名前 (2)」になるので、表示を合わせる
        return _session.LogPath;
    }

    /// <summary>ログのパス(ファイル名)が変わったことを、画面に伝える。</summary>
    private void RaiseLogPathChanged()
    {
        Raise(nameof(LogPath));
        Raise(nameof(LogPathDisplay));
    }

    /// <summary>この部屋が、いま画面に映っている部屋ならtrue。</summary>
    public bool IsCurrentRoom => Rooms is null || ReferenceEquals(Rooms.Current, this);

    /// <summary>この部屋のタブを押したときに、この部屋へ移る命令。</summary>
    public ICommand SelectRoomCommand { get; }

    /// <summary>この部屋を閉じる命令(最後の1部屋は閉じられない)。</summary>
    public ICommand CloseRoomCommand { get; }

    /// <summary>表示名・簡易表示モードを更新したときに起きる(ほかの部屋に合わせるため)。</summary>
    public event Action<MainViewModel, AppearanceSettings>? AppearanceUpdated;

    /// <summary>画面に映る部屋が変わった・部屋の数が変わったことを、タブの表示に伝える。</summary>
    internal void NotifyCurrentRoomChanged()
    {
        Raise(nameof(IsCurrentRoom));
        ((RelayCommand)CloseRoomCommand).RaiseCanExecuteChanged();
    }

    /// <summary>ほかの部屋で変えられた配色テーマに合わせる(保存は変えた部屋がすませているので、ここでは保存しない)。</summary>
    /// <param name="themeId">合わせるテーマID</param>
    internal void SyncTheme(string themeId)
    {
        Set(ref _selectedThemeId, ThemeCatalog.GetOrDefault(themeId).Id, nameof(SelectedThemeId));
    }

    /// <summary>ほかの部屋で変えられた表示名・簡易表示モードに合わせる。</summary>
    /// <param name="appearance">合わせる設定</param>
    internal void SyncAppearance(AppearanceSettings appearance)
    {
        _session.SetAppearance(appearance);
        ApplyAppearanceToAll();
    }

    /// <summary>この部屋を閉じる前の後片付け(返答待ちを止め、読み上げを止め、起動したままのCLIを終わらせる)。</summary>
    public void CloseRoom()
    {
        CancelPending();
        _session.Shutdown();
    }

    /// <summary>会議ログのファイルパス(ツールチップや開く処理に使うフルパス)。</summary>
    public string LogPath => _session.LogPath;

    /// <summary>画面の上に出す会議ログのパス(簡易表示モード時は末尾だけに省略)。</summary>
    public string LogPathDisplay => Appearance.FormatLogPath(LogPath);
}
