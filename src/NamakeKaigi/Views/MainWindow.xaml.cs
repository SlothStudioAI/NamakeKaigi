using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Views;

/// <summary>メイン画面。見た目のための処理(発言が増えたら一番下へスクロール、フォルダ・ハーネス設定ダイアログの表示)だけを持つ。</summary>
public partial class MainWindow : Window
{
    private const double BottomTolerance = 24;

    private RosterWindow? _rosterWindow;
    private ScheduleWindow? _scheduleWindow;
    private ScheduleViewModel? _schedule;
    private RemoteQrWindow? _remoteQrWindow;
    private string? _remoteUrl;

    /// <summary>メンバー名簿に出すメンバーを拾い上げる処理(起動時に設定される。なければ名簿は開けない)。</summary>
    public Func<IReadOnlyList<RosterMember>>? RosterScan { get; set; }

    /// <summary>「場所」の設定を読む処理(起動時に設定される。なければ「場所…」は何もしない)。</summary>
    public Func<PathSettings>? LoadLocations { get; set; }

    /// <summary>「場所」の設定を保存する処理(起動時に設定される)。</summary>
    public Action<PathSettings>? SaveLocations { get; set; }

    /// <summary>画面を作る。</summary>
    public MainWindow()
    {
        InitializeComponent();
        // 発言のリストの ScrollViewer は、ItemsControl のテンプレートの中にあるので、テンプレートを作ってから取り出す
        _scrollers = new[] { ChatList, ClaudeList, GeminiList, ChappyList }.Select(ScrollerOf).ToArray();
        CommandManager.AddPreviewExecutedHandler(Input, OnInputPreviewExecuted);
        Input.PreviewKeyDown += OnInputPreviewKeyDown;
        foreach (var scroller in Scrollers)
        {
            scroller.ScrollChanged += StickToBottom;
            // 切り替えて見える状態になったときも、一番下から始める
            scroller.IsVisibleChanged += (s, _) =>
            {
                if (((ScrollViewer)s).IsVisible) ((ScrollViewer)s).ScrollToEnd();
            };
        }
        DataContextChanged += (_, e) =>
        {
            // 部屋(タブ)を切り替えると、映す部屋が変わる。前の部屋の見張りを外してから、新しい部屋を見張る
            if (e.OldValue is MainViewModel old)
            {
                old.Messages.CollectionChanged -= OnMessagesChanged;
                old.PropertyChanged -= OnRoomPropertyChanged;
            }
            if (e.NewValue is not MainViewModel vm) return;
            vm.Messages.CollectionChanged += OnMessagesChanged;
            vm.PropertyChanged += OnRoomPropertyChanged;
            UpdateSplitColumns(vm);
            // 切り替えた部屋も、一番下から見せる
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                foreach (var scroller in Scrollers) scroller.ScrollToEnd();
            }));
        };
    }

    /// <summary>映している部屋の参加者が変わったら、「別々に見る」の列を作り直す。</summary>
    private void OnRoomPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (sender is MainViewModel vm
            && args.PropertyName is nameof(MainViewModel.ClaudePresent) or nameof(MainViewModel.GeminiPresent)
                or nameof(MainViewModel.ChappyPresent))
        {
            UpdateSplitColumns(vm);
        }
    }

    // 別々に見る画面の、列の最小の幅(参加している列だけに効かせる)
    private const double SplitColumnMinWidth = 240;

    // 列の間の隙間(仕切りの幅を含む)
    private const double SplitGapWidth = 12;

    /// <summary>
    /// 「別々に見る」画面の列を、参加している人だけにする。いない人の列は幅0にし、隣り合う列の間にだけ隙間(と仕切り)を置く。
    /// 列は 0=クローディ・2=ジェミリー・4=チャッピー、隙間は 1・3。クローディとチャッピーだけのときは、隙間は置くが仕切りは出さない(ドラッグで動かせるのは、隣り合う列の間だけ)。
    /// </summary>
    /// <param name="vm">画面の状態(誰が参加しているか)</param>
    private void UpdateSplitColumns(MainViewModel vm)
    {
        bool[] present = { vm.ClaudePresent, vm.GeminiPresent, vm.ChappyPresent };
        var columns = SplitGrid.ColumnDefinitions;
        for (var i = 0; i < present.Length; i++)
        {
            columns[i * 2].Width = present[i] ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            columns[i * 2].MinWidth = present[i] ? SplitColumnMinWidth : 0;
        }
        var gapAfterClaude = present[0] && (present[1] || present[2]);
        var gapAfterGemini = present[1] && present[2];
        columns[1].Width = new GridLength(gapAfterClaude ? SplitGapWidth : 0);
        columns[3].Width = new GridLength(gapAfterGemini ? SplitGapWidth : 0);
        ColumnSplitter.Visibility = present[0] && present[1] ? Visibility.Visible : Visibility.Collapsed;
        ColumnSplitter2.Visibility = gapAfterGemini ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>作業フォルダの札が押されたとき。フォルダ選択の窓を開き、選ばれたフォルダを作業フォルダにする。</summary>
    private void OnChooseWorkDir(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "作業フォルダを選ぶ" };
        // 設定のパスは「/」区切りなので、Windows用の「\」区切りにそろえる(そのままだと、窓が開けずに落ちる)
        var start = vm.WorkDir.Length > 0 ? System.IO.Path.GetFullPath(vm.WorkDir) : "";
        if (System.IO.Directory.Exists(start)) dialog.InitialDirectory = start;
        if (dialog.ShowDialog(this) == true) vm.ChangeWorkDir(dialog.FolderName);
    }

    /// <summary>カードの歯車(ツール・許可の設定)が押されたとき。AIに応じたハーネス設定画面を開く。</summary>
    private void OnOpenHarness(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not FrameworkElement { DataContext: MemberViewModel member }) return;

        if (member.Participant.Id == Participants.ClaudeId)
        {
            var current = vm.GetClaudeSettings();
            if (current is null) return;
            var dlg = new HarnessWindow(current, vm.UpdateClaudeHarness, vm.LoadClaudeConnectorsAsync) { Owner = this };
            dlg.ShowDialog();
        }
        else if (member.Participant.Id == Participants.GeminiId)
        {
            var current = vm.GetAgySettings();
            if (current is null) return;
            var dlg = new HarnessWindow(current, vm.CheckAgyUpdateAsync) { Owner = this };
            dlg.ShowDialog();
        }
        else if (member.Participant.Id == Participants.ChappyId)
        {
            var dlg = new HarnessWindow(new CodexSettings()) { Owner = this };
            dlg.ShowDialog();
        }
    }

    /// <summary>入力欄へファイル・フォルダがドラッグされてきたとき。TextBoxの既定動作より優先してコピー受け入れにする。</summary>
    private void OnInputDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    /// <summary>入力欄へファイル・フォルダがドロップされたとき。添付一覧へ追加する。</summary>
    private void OnInputDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            vm.AddAttachments(files);
            e.Handled = true;
        }
    }

    /// <summary>「📎」ボタンが押されたとき。ファイル選択ダイアログを開いて添付する。</summary>
    private void OnAttachFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添付するファイルを選ぶ(フォルダはドラッグ&ドロップで添付できます)",
            Multiselect = true,
        };
        var start = vm.WorkDir.Length > 0 ? System.IO.Path.GetFullPath(vm.WorkDir) : "";
        if (System.IO.Directory.Exists(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(this) == true)
        {
            vm.AddAttachments(dlg.FileNames);
        }
    }

    /// <summary>添付チップの「✕」が押されたとき。その添付を外す。</summary>
    private void OnRemoveAttachment(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is FrameworkElement { DataContext: AttachmentItem item })
        {
            vm.RemoveAttachment(item);
        }
    }

    /// <summary>「🎤」ボタンが押されたとき。入力欄にフォーカスしてWindows標準の音声入力(Win+H)を起動する。</summary>
    private void OnVoiceInput(object sender, RoutedEventArgs e)
    {
        Input.Focus();
        Input.CaretIndex = Input.Text.Length;
        VoiceInputHelper.LaunchWindowsVoiceTyping();
    }

    /// <summary>「過去の会議」が押されたとき。一覧ダイアログを開いて、選ばれた会議を復元する。</summary>
    private void OnOpenPastMeetings(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var meetings = vm.ListPastMeetings();
        var dlg = new PastMeetingsWindow(meetings, selected => vm.OpenPastMeeting(selected.FolderPath))
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    /// <summary>会議室のタブがダブルクリックされたとき。その場で、名前を書き換えられるようにする。</summary>
    private void OnRoomTabDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MainViewModel room }) return;
        room.BeginRenameRoom();
        e.Handled = true;
    }

    /// <summary>名前の入力欄でキーが押されたとき。Enterで決め、Escでやめる。</summary>
    private void OnRoomTitleEditKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MainViewModel room }) return;
        if (e.Key == Key.Enter)
        {
            room.CommitRenameRoom();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            room.CancelRenameRoom();
            e.Handled = true;
        }
    }

    /// <summary>名前の入力欄から、ほかへ移ったとき。入力した名前を決める(EnterやEscで終わったあとは、何もしない)。</summary>
    private void OnRoomTitleEditLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel room }) room.CommitRenameRoom();
    }

    /// <summary>名前の入力欄が見えるようになったとき。すぐ打てるよう、入力欄に移って、全体を選ぶ。</summary>
    private void OnRoomTitleEditVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;
        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>「メンバー名簿」が押されたとき。名簿の窓を開く(会話と並べて見られるよう、閉じなくても操作できる。開いていれば前に出す)。</summary>
    private void OnOpenRoster(object sender, RoutedEventArgs e)
    {
        if (RosterScan is null) return;
        if (_rosterWindow is not null)
        {
            _rosterWindow.Activate();
            return;
        }
        var appearance = (DataContext as MainViewModel)?.Appearance;
        _rosterWindow = new RosterWindow(new RosterViewModel(RosterScan, appearance)) { Owner = this };
        _rosterWindow.Closed += (_, _) => _rosterWindow = null;
        _rosterWindow.Show();
    }

    /// <summary>リモートのURL(合言葉つき)。nullならリモートは動いていない(「スマホ」ボタンも出さない)。</summary>
    public string? RemoteUrl
    {
        get => _remoteUrl;
        set
        {
            _remoteUrl = value;
            RemoteButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>リモートのQRコードの窓を開く(開いていれば前に出す)。</summary>
    /// <param name="note">上に出す説明(なければ既定の説明)</param>
    public void ShowRemoteQr(string? note = null)
    {
        if (RemoteUrl is null) return;
        if (_remoteQrWindow is not null)
        {
            _remoteQrWindow.Activate();
            return;
        }
        _remoteQrWindow = new RemoteQrWindow(RemoteUrl, note) { Owner = this };
        _remoteQrWindow.Closed += (_, _) => _remoteQrWindow = null;
        _remoteQrWindow.Show();
    }

    /// <summary>「スマホ」が押されたとき。QRコードの窓を開く。</summary>
    private void OnOpenRemoteQr(object sender, RoutedEventArgs e) => ShowRemoteQr();

    /// <summary>予約(定期実行)の状態。nullなら自動実行の機能は使わない(ボタンも出さない)。</summary>
    public ScheduleViewModel? Schedule
    {
        get => _schedule;
        set
        {
            _schedule = value;
            ScheduleButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible; // 予約の機能を使うときだけ、ボタンを出す
        }
    }

    /// <summary>「自動実行」が押されたとき。予約の窓を開く(閉じなくても会議を操作できる。開いていれば前に出す)。</summary>
    private void OnOpenSchedule(object sender, RoutedEventArgs e)
    {
        if (Schedule is null) return;
        if (_scheduleWindow is not null)
        {
            _scheduleWindow.Activate();
            return;
        }
        _scheduleWindow = new ScheduleWindow(Schedule) { Owner = this };
        _scheduleWindow.Closed += (_, _) => _scheduleWindow = null;
        _scheduleWindow.Show();
    }

    /// <summary>「設定」: 設定画面(配色テーマ・制限時間・イラストの表示・表示名)を開く。</summary>
    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new SettingsWindow(vm, () => OnOpenAppearance(this, new RoutedEventArgs()), OpenLocations)
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    /// <summary>「📁 場所…」が押されたとき。場所の設定ダイアログを開き、保存されたら次の起動から効くことを知らせる。</summary>
    private void OpenLocations()
    {
        if (LoadLocations is null || SaveLocations is null) return;
        var owner = OwnedWindows.OfType<SettingsWindow>().FirstOrDefault() as Window ?? this;
        var dlg = new LocationsWindow(LoadLocations(), SaveLocations) { Owner = owner };
        if (dlg.ShowDialog() == true)
            MessageBox.Show(owner, "保存しました。次の起動から効きます。", "場所の設定", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>「✏️ 名前変更…」が押されたとき。表示名の設定ダイアログを開く。</summary>
    private void OnOpenAppearance(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dlg = new AppearanceWindow(vm.Appearance, vm.UpdateAppearance)
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    /// <summary>
    /// 入力欄でキーが押されたとき。貼り付けのキー(Ctrl+V・Shift+Insert)なら、クリップボードの画像・ファイルを添付にする。
    /// 入力欄は、クリップボードに文字がないと貼り付けの命令自体を動かさないことがあるため、キーを直接受け取る。
    /// </summary>
    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key; // 日本語入力がオンのとき
        if (!IsPasteGesture(key, Keyboard.Modifiers)) return;
        if (TryAttachFromClipboard()) e.Handled = true;
    }

    /// <summary>入力欄で貼り付け(右クリックの「貼り付け」など)が実行されたとき。画像・ファイルなら添付にする。</summary>
    private void OnInputPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste) return;
        if (TryAttachFromClipboard()) e.Handled = true;
    }

    /// <summary>貼り付けのキー操作か(Ctrl+V または Shift+Insert)。</summary>
    /// <param name="key">押されたキー</param>
    /// <param name="modifiers">同時に押されている修飾キー</param>
    /// <returns>貼り付けのキーならtrue</returns>
    public static bool IsPasteGesture(Key key, ModifierKeys modifiers) =>
        (key == Key.V && modifiers == ModifierKeys.Control) ||
        (key == Key.Insert && modifiers == ModifierKeys.Shift);

    /// <summary>
    /// クリップボードに画像があれば会議フォルダへPNG保存して、ファイル(エクスプローラーでコピーしたもの)があればそのまま、添付一覧へ追加する。
    /// </summary>
    /// <returns>添付したらtrue(このときは文字の貼り付けをしない)。文字だけのときはfalse</returns>
    private bool TryAttachFromClipboard()
    {
        if (DataContext is not MainViewModel vm) return false;
        try
        {
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image is null) return false;
                var savedPath = ClipboardImageHelper.SavePngToMeetingFolder(image, vm.EnsureMeetingFolder());
                vm.AddAttachments(new[] { savedPath });
                return true;
            }
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                vm.AddAttachments(files);
                return files.Count > 0;
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // ほかのアプリがクリップボードを使用中のときは、普通の貼り付けに任せる
        }
        return false;
    }

    /// <summary>ログのボタン(見出しの右)が押されたとき。いまの会議の log.md を既定アプリで開く(まだなければフォルダを開く)。</summary>
    private void OnOpenLogFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || string.IsNullOrWhiteSpace(vm.LogPath)) return;
        var fullPath = Path.GetFullPath(vm.LogPath);
        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        else
        {
            OnOpenLogFolder(sender, e);
        }
    }

    /// <summary>ログのボタンの右クリックで「保存先フォルダを開く」が選ばれたとき。いまの会議の保存先フォルダをエクスプローラーで開く。</summary>
    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var logPath = vm.EnsureMeetingFolder();
        if (string.IsNullOrWhiteSpace(logPath)) return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch (Exception)
            {
                // エクスプローラーの起動失敗時は何もしない
            }
        }
    }

    private bool _sidebarCollapsed;
    private GridLength _sidebarWidthBeforeCollapse;

    /// <summary>左のメンバー欄を縮める / 元の幅に戻す。縮める前の幅を覚えておき、広げるときに戻す。</summary>
    /// <param name="sender">押されたボタン。</param>
    /// <param name="e">イベント引数。</param>
    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        if (_sidebarCollapsed)
        {
            _sidebarWidthBeforeCollapse = SidebarColumn.Width;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            SidebarPanel.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidebarColumn.MinWidth = 200;
            SidebarColumn.Width = _sidebarWidthBeforeCollapse;
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
        }
        SidebarToggleButton.Content = _sidebarCollapsed ? "" : "";
        SidebarToggleButton.ToolTip = _sidebarCollapsed ? "左のメンバー欄を広げます" : "左のメンバー欄を縮めます";
    }

    /// <summary>独自タイトルバーの「最小化」ボタン。</summary>
    private void OnMinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>独自タイトルバーの「最大化 / 元に戻す」ボタン。</summary>
    private void OnMaximizeRestoreWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>独自タイトルバーの「閉じる」ボタン。</summary>
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    // 発言のリスト(まとめて見る・各AIの列)のスクロール部分
    private readonly ScrollViewer[] _scrollers;

    private IEnumerable<ScrollViewer> Scrollers => _scrollers;

    /// <summary>発言のリスト(ItemsControl)のテンプレートの中から、スクロール部分を取り出す。</summary>
    /// <param name="list">発言のリスト</param>
    /// <returns>スクロール部分</returns>
    private static ScrollViewer ScrollerOf(ItemsControl list)
    {
        list.ApplyTemplate();
        return (ScrollViewer)list.Template.FindName("PART_Scroll", list);
    }

    /// <summary>一番下を見ていたなら、文字が増えても(返答が流れてきても)、一番下に付いていく。</summary>
    private static void StickToBottom(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange <= 0) return;
        var previousBottom = e.VerticalOffset - e.VerticalChange + e.ViewportHeight;
        var previousExtent = e.ExtentHeight - e.ExtentHeightChange;
        if (previousBottom >= previousExtent - BottomTolerance) ((ScrollViewer)sender).ScrollToEnd();
    }

    /// <summary>あなたが発言したときは、見ている位置にかかわらず、一番下へ移る。</summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (e.NewItems?.OfType<MessageViewModel>().Any(m => m.IsUser) != true) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            foreach (var scroller in Scrollers) scroller.ScrollToEnd();
        }));
    }
}
