using System.IO;
using System.Windows;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Views;

namespace NamakeKaigi;

/// <summary>アプリの起動処理。設定を読み、画面に会議の進行役をつないで開く。</summary>
public partial class App : Application
{
    private System.Windows.Threading.DispatcherTimer? _scheduleTimer;
    private Services.Remote.RemoteServer? _remote;

    /// <summary>二重起動の判定に使う名前(ユーザーのセッションの中だけで有効)。</summary>
    private const string SingleInstanceName = "Local\\NamakeKaigi_SingleInstance";

    /// <summary>1つ目として取った名前(アプリが動いているあいだ持ち続け、終了時に手放す)。</summary>
    private SingleInstance? _singleInstance;

    /// <summary>リモート(スマホ用のWebページ)を、設定で有効なときだけ起動する。失敗してもアプリは続け、画面の知らせにする。</summary>
    /// <param name="remote">リモートの設定</param>
    /// <param name="settingsPath">ユーザーの設定ファイルのパス(合言葉を自動で作ったときの保存先)</param>
    /// <param name="rooms">会議室の一覧</param>
    /// <param name="window">メイン画面(「スマホ」ボタンとQRコードの窓を出す)</param>
    private async Task StartRemoteAsync(Models.RemoteSettings remote, string settingsPath, ViewModels.MeetingRooms rooms, MainWindow window)
    {
        try
        {
            var token = remote.Token;
            var generated = string.IsNullOrWhiteSpace(token);
            if (generated)
            {
                token = Services.Remote.RemoteAccess.NewToken();
                SettingsLoader.SaveRemoteToken(settingsPath, token);
            }
            var (address, loopbackOnly) = Services.Remote.RemoteAccess.PickBindAddress(
                remote.BindAddress, Services.Remote.RemoteAccess.LocalAddresses());
            var server = new Services.Remote.RemoteServer(remote, token!, address, new ViewModels.RemoteHost(rooms, Dispatcher));
            await server.StartAsync();
            _remote = server;
            window.RemoteUrl = server.Url;
            // 自動で作ったとき、またはこのPCの中だけのときは、QRコードの窓を一度見せる(あとはタイトルバーの「スマホ」から開ける)
            if (loopbackOnly)
                window.ShowRemoteQr("Tailscaleのアドレスが見つからないため、このPCの中だけで待ち受けています(スマホからはつながりません)。");
            else if (generated)
                window.ShowRemoteQr();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"リモートを開始できませんでした。\n{ex.Message}", "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>アプリを閉じるときに、リモートのサーバーも止める。</summary>
    /// <param name="e">終了の情報</param>
    protected override void OnExit(ExitEventArgs e)
    {
        if (_remote is not null) _remote.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>時刻を見て、来ている予約を実行する。予約の失敗でアプリが落ちないよう、例外は画面の知らせにする。</summary>
    /// <param name="schedule">予約の状態</param>
    private static async Task TickScheduleAsync(ScheduleViewModel schedule)
    {
        try
        {
            await schedule.TickAsync();
        }
        catch (Exception ex)
        {
            schedule.ReportError($"自動実行で想定外のエラーが起きました: {ex.Message}");
        }
    }

    /// <summary>
    /// 作業フォルダ(各AIが作業する場所)が空、または見つからないとき、選ぶ窓を出す(初回の起動など)。
    /// 選ばれたら保存する。選ばれなければ、この回だけ「ドキュメント」を使う(次の起動で、また聞く)。
    /// </summary>
    /// <param name="workDir">設定の作業フォルダ</param>
    /// <param name="settingsPath">ユーザーの設定ファイルのパス(選ばれたフォルダの保存先)</param>
    /// <returns>使う作業フォルダ</returns>
    private static string EnsureWorkDir(string workDir, string settingsPath)
    {
        if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir)) return workDir;
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "作業フォルダを選んでください(各AIが、ファイルを読み書きする場所です。あとから変えられます)",
            InitialDirectory = documents,
        };
        if (dialog.ShowDialog() != true) return documents.Replace('\\', '/');
        SettingsLoader.SaveWorkDir(settingsPath, dialog.FolderName);
        return dialog.FolderName.Replace('\\', '/');
    }

    // 画面に知らせた例外のメッセージ(同じ例外が繰り返されても、知らせは1回だけにする)
    private readonly HashSet<string> _reportedErrors = new();

    /// <summary>
    /// 画面の操作の中で起きた、受け止められていない例外。記録して、1回だけ知らせ、アプリは続ける
    /// (クリップボードの失敗や保存の失敗などで、会議ごと落ちないようにする)。
    /// </summary>
    /// <param name="sender">送り主</param>
    /// <param name="e">例外の情報</param>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Append(ErrorLog.DefaultPath(), "画面の操作", e.Exception, DateTime.Now);
        e.Handled = true;
        if (!_reportedErrors.Add(e.Exception.GetType().FullName + ": " + e.Exception.Message)) return;
        MessageBox.Show(
            $"想定外のエラーが起きましたが、ナマケ会議は続けられます。\n\n{e.Exception.Message}\n\n詳しい内容は、次のファイルに残しました。\n{ErrorLog.DefaultPath()}",
            "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>裏の処理(別のスレッド・Task)で起きた、受け止められていない例外を、記録だけする。</summary>
    /// <param name="source">どこで受け止めたか</param>
    /// <param name="exception">例外(オブジェクトでなければnull)</param>
    private static void LogBackgroundError(string source, object? exception)
    {
        if (exception is Exception ex) ErrorLog.Append(ErrorLog.DefaultPath(), source, ex, DateTime.Now);
    }

    /// <summary>起動時に、設定を読んでメイン画面を開く。</summary>
    /// <param name="e">起動の情報</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 二重起動の防止: すでに起動していれば、その窓を前に出して、この2つ目は終わる(開発用に、起動引数で許せる)
        if (!SingleInstance.AllowsMultiple(e.Args))
        {
            _singleInstance = SingleInstance.TryAcquire(SingleInstanceName);
            if (!_singleInstance.IsFirst)
            {
                SingleInstance.BringExistingToFront();
                MessageBox.Show("ナマケ会議は、もう起動しています。開いている窓を前に出しました。",
                    "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogBackgroundError("裏の処理(Task)", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogBackgroundError("裏の処理(スレッド)", args.ExceptionObject);
        try
        {
            // 既定の値(exe の隣)に、ユーザーの設定(%APPDATA%)を重ねて読む。アプリで変えた設定は、ユーザーの設定にだけ書く
            var defaultsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
            var settingsPath = SettingsLoader.DefaultLocalPath();
            // ユーザーの設定が壊れていても起動できるよう、壊れたファイルは別の名前に退避して、既定の設定で始める
            var quarantined = SettingsLoader.QuarantineBrokenLocal(settingsPath, DateTime.Now);
            if (quarantined is not null)
                MessageBox.Show(
                    $"ユーザーの設定ファイルが壊れていたため、別の名前に移して、標準の設定で起動します。\n\n移した先:\n{quarantined}\n\n以前の設定を戻したいときは、このファイルを開いて直してください。",
                    "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Warning);
            var settings = SettingsLoader.ResolvePaths(SettingsLoader.Load(defaultsPath, settingsPath), AppContext.BaseDirectory);
            // 各AIのCLIを探す。見つからないAIは、カードを出さない(1人でもいれば会議できる)
            var (located, installedAis) = CliLocator.Detect(settings);
            settings = located;
            if (installedAis.Count == 0)
                MessageBox.Show("AIのCLI(Claude Code・Antigravity の agy・Codex)が、どれも見つかりませんでした。\n" +
                    "どれか1つを入れてから、起動し直してください。入っているのに見つからないときは、設定画面の「場所」で、CLIの場所を選んでください。",
                    "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Information);
            Models.ThemeCatalog.Apply(settings.Theme);
            settings = settings with { WorkDir = EnsureWorkDir(settings.WorkDir, settingsPath) };
            Views.MarkdownFormatter.StreamRenderInterval = TimeSpan.FromMilliseconds(Math.Max(0, settings.StreamRenderIntervalMs));
            Services.LinkDetector.Probe = settings.LinkProbe ?? new Models.LinkProbeSettings();
            var factory = new ChatSessionFactory(
                settings,
                new ProcessRunner(),
                saveWorkDir: dir => SettingsLoader.SaveWorkDir(settingsPath, dir),
                saveClaude: claude => SettingsLoader.SaveClaudeHarness(settingsPath, claude),
                saveAgy: agy => SettingsLoader.SaveAgyHarness(settingsPath, agy),
                saveModelEffort: (section, modelId, effortId) =>
                    SettingsLoader.SaveModelAndEffort(settingsPath, section, modelId, effortId),
                saveTheme: themeId => SettingsLoader.SaveTheme(settingsPath, themeId),
                saveAppearance: appearance => SettingsLoader.SaveAppearance(settingsPath, appearance),
                saveTimeout: sec => SettingsLoader.SaveTimeout(settingsPath, sec),
                saveAllOrder: order => SettingsLoader.SaveAllOrder(settingsPath, order));
            var voice = settings.Voice ?? new Models.VoiceSettings();
            // 声の準備は約0.4秒かかるので、使うとき(スイッチON・吹き出しの🔊)まで待つ。起動時にONの設定のときだけ、ここで準備する(WBS 11.51)
            // 日本語の声がなければ、読み上げだけ使えない状態になる
            // 読み上げの機能を使わない設定(既定)なら、声の部品を作らない(画面の読み上げの行・🔊・止めるボタンも出ない)
            ISpeechService? speech = null;
            if (voice.Enabled)
            {
                speech = new LazySpeechService(() => new SystemSpeechService(voice), eager: voice.AutoRead);
                // 声の元にVOICEVOXが選ばれていれば、そちらで読む(つながらないときは、上のWindowsの声に戻る)。この機能を外すときは、この2行を消す
                if (string.Equals(voice.Engine, "voicevox", StringComparison.OrdinalIgnoreCase))
                    speech = Services.Voicevox.VoicevoxSpeechService.Create(voice.Voicevox ?? new Models.VoicevoxSettings(), speech);
            }
            // 起動時は1部屋。画面は、いま選ばれている部屋を映す(タブで部屋を切り替えると、映す部屋が変わる)
            var rooms = new ViewModels.MeetingRooms(
                () => new MainViewModel(factory.Create, speech: speech, voice: voice,
                    defaultParticipants: settings.DefaultParticipants, roomTitleMaxChars: settings.RoomTitleMaxChars,
                    installedAis: installedAis),
                settings.MaxRooms);
            var rosterSettings = settings.Roster ?? new Models.RosterSettings();
            var window = new MainWindow
            {
                DataContext = rooms.Current,
                RosterScan = () => RosterScanner.Scan(rosterSettings, settings.WorkDir,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                LoadLocations = () => SettingsLoader.LoadPaths(defaultsPath, settingsPath),
                SaveLocations = paths => SettingsLoader.SavePaths(defaultsPath, settingsPath, paths),
            };
            rooms.CurrentChanged += room => window.DataContext = room;
            // 予約(定期実行)は、保存ファイルの場所が設定されているときだけ使う。時刻は、保存ファイルの間隔で見に行く
            if (!string.IsNullOrWhiteSpace(settings.ScheduleFile))
            {
                var schedule = new ScheduleViewModel(rooms, new ScheduleStore(settings.ScheduleFile), () => DateTime.Now);
                window.Schedule = schedule;
                _scheduleTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(schedule.TickSec),
                };
                _scheduleTimer.Tick += async (_, _) => await TickScheduleAsync(schedule);
                _scheduleTimer.Start();
            }
            window.Show();
            // リモートは、設定で有効にしたときだけ使う(既定はオフ)
            if (settings.Remote is { Enabled: true } remote)
                _ = StartRemoteAsync(remote, settingsPath, rooms, window);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"起動に失敗しました。\n{ex.Message}", "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
