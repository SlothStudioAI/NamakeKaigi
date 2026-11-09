using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>画面用の1本のSTAスレッド(WPFのApplicationは1つだけ作れるため、全試験で共用する)。</summary>
internal static class UiThread
{
    private static readonly Dispatcher Dispatcher;

    static UiThread()
    {
        Dispatcher? dispatcher = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            // 森テーマだけを読み込む。本物の App を作ると、WPFが起動処理(App.OnStartup)を動かし、
            // 本物の設定で会議ログ(空の .md)と窓を作ってしまうので、ふつうの Application を使う
            // 既定の「最後の窓が閉じたら終了」だと、窓を開いて閉じる試験が並行して走ったとき、窓が0個になった一瞬にアプリが終了し、
            // 以降の画面の試験が「アプリケーション オブジェクトはシャットダウンされています」で落ちる。試験の間は終了させない
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/NamakeKaigi;component/Themes/ForestTheme.xaml"),
            });
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        Dispatcher = dispatcher!;
    }

    /// <summary>
    /// 画面スレッドの中で、指定のミリ秒だけ待つ(待つあいだも、タイマーなどの処理は動く)。
    /// <see cref="Run"/> の処理の中から呼ぶ。
    /// </summary>
    /// <param name="ms">待つ時間(ミリ秒)</param>
    public static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>画面スレッドで処理を実行し、例外はそのまま呼び出し元へ返す。</summary>
    /// <param name="action">実行する処理</param>
    public static void Run(Action action)
    {
        Exception? error = null;
        Dispatcher.Invoke(() =>
        {
            try { action(); } catch (Exception ex) { error = ex; }
        });
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}

public class MainWindowRenderTests
{
    /// <summary>画面を表示せずに、指定の見せ方で画像へ描く。XAMLの読み込みエラーもここで見つかる。</summary>
    internal static string Render(MainViewModel vm, string fileName) =>
        RenderWindow(() => new MainWindow { DataContext = vm }, new Size(1040, 720), fileName);

    /// <summary>任意のWindowを表示せずに画像へ描く。</summary>
    internal static string RenderWindow(Func<Window> createWindow, Size size, string fileName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "screens");
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, fileName);

        UiThread.Run(() =>
        {
            var window = createWindow();
            var root = (FrameworkElement)window.Content;
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded); // 画像の読み込みを待つ
            root.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle((Brush)Application.Current.Resources["MainBackgroundBrush"], null, new Rect(size));
            bitmap.Render(background);
            bitmap.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(png);
            encoder.Save(stream);

            // 描き終わったら画面の中身を外す(ラジオボタンの選択が、次に作る画面へ影響しないように)
            window.Content = null;
        });
        return png;
    }

    /// <summary>見た目の確認用の会議(返答が流れている途中・ファイルを読んでいる途中を含む)を作る。</summary>
    private static async Task<MainViewModel> SampleMeeting()
    {
        var session = new FakeSession
        {
            Script = _ => new[]
            {
                new ChatProgress(Participants.ClaudeId, ChatProgressKind.Model, "claude-sonnet-5-5"),
                new ChatProgress(Participants.ClaudeId, ChatProgressKind.Usage,
                    new UsageQuota(12, DateTimeOffset.Now.AddHours(3), 24, DateTimeOffset.Now.AddDays(4)).Encode()),
                new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "## バジルがおすすめです\n- **発芽が早い**（数日で芽が出る）\n- 週末に `容器1つ` で仕込める"),
                new ChatProgress(Participants.GeminiId, ChatProgressKind.Done, "**リーフレタス**です。Claudeさんのバジルと同じ容器で育てやすいです。\n```text\n水耕栽培スポンジ + 液肥500倍\n```"),
            },
        };
        var vm = new MainViewModel(() => session) { InputText = "週末に水耕栽培で育てる野菜を提案して" };
        await vm.SendAsync();

        // 続きの発言: ジェミリーは返答が流れている途中、クローディはファイルを読んでいる途中にする
        vm.InputText = "どれから始めるか、記憶を読んで決めて";
        session.Script = null;
        var sending = vm.SendAsync();
        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Activity, "AI_MEMORY.md を読んでいます"));
        session.Push(new ChatProgress(Participants.GeminiId, ChatProgressKind.Delta, "まずは、育てやすいリーフレタスから"));
        _ = sending; // 完了させない(途中の見た目を撮るため)

        // 入力欄の上に添付チップが見える状態にする
        vm.AddAttachments(new[] { Path.Combine(AppContext.BaseDirectory, "settings.json") });
        return vm;
    }

    [Fact]
    public async Task まとめて見る画面を読み込み_画像に描ける()
    {
        var vm = await SampleMeeting();

        var png = Render(vm, "main_window.png");

        Assert.True(new FileInfo(png).Length > 10_000);
    }

    [Fact]
    public void 会議室のタブの行を持つ画面を読み込み_画像に描ける()
    {
        var rooms = new MeetingRooms(() => new MainViewModel(() => new FakeSession()), maxRooms: 3);
        rooms.AddRoom();
        rooms.Items[0].InputText = "1つ目の部屋";

        var png = Render(rooms.Current, "main_window_rooms.png");

        Assert.True(new FileInfo(png).Length > 10_000);
    }

    [Fact]
    public async Task 別々に見る画面を読み込み_画像に描ける()
    {
        var vm = await SampleMeeting();
        vm.IsSplitView = true;

        var png = Render(vm, "main_window_split.png");

        Assert.True(new FileInfo(png).Length > 10_000);
    }

    [Fact]
    public async Task ダークモードと緑バリエーションに切り替え_画像に描ける()
    {
        var vm = await SampleMeeting();
        try
        {
            vm.SelectedThemeId = "dark-forest";
            var darkPng = Render(vm, "main_window_dark_forest.png");
            Assert.True(new FileInfo(darkPng).Length > 10_000);

            vm.SelectedThemeId = "mint";
            var mintPng = Render(vm, "main_window_mint.png");
            Assert.True(new FileInfo(mintPng).Length > 10_000);

            // 簡易表示モードON ＋ オフィス・ライト配色
            vm.IsBusinessMode = true;
            vm.SelectedThemeId = "office-light";
            var bizPng = Render(vm, "main_window_business_office.png");
            Assert.True(new FileInfo(bizPng).Length > 10_000);

            var appearancePng = RenderWindow(
                () => new AppearanceWindow(vm.Appearance, _ => { }),
                new Size(540, 470),
                "appearance_window.png");
            Assert.True(new FileInfo(appearancePng).Length > 10_000);
        }
        finally
        {
            // 他の描画テストのために標準テーマへ戻す
            UiThread.Run(() => ThemeCatalog.Apply(ThemeCatalog.DefaultThemeId));
        }
    }

    [Fact]
    public void クローディとジェミリーのハーネス設定画面を読み込み_画像に描け_設定を組み立てられる()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));

        var claudePng = RenderWindow(
            () => new HarnessWindow(settings.Claude, _ => { }),
            new Size(640, 640),
            "harness_window_claude.png");
        Assert.True(new FileInfo(claudePng).Length > 10_000);

        var geminiPng = RenderWindow(
            () => new HarnessWindow(settings.Agy),
            new Size(640, 560),
            "harness_window_gemini.png");
        Assert.True(new FileInfo(geminiPng).Length > 10_000);

        UiThread.Run(() =>
        {
            var dlg = new HarnessWindow(settings.Claude, _ => { });
            var edited = dlg.BuildEditedClaudeSettings();
            var writerTools = HarnessInfo.ParseTools(edited.WriterExtraArgs);
            Assert.DoesNotContain("Agent", writerTools); // 読み書きの道具は固定(サブエージェントは入れない)
            Assert.Contains("Read", writerTools);
            Assert.Contains("Edit", writerTools);
        });
    }

    /// <summary>見た目の木から、条件に合う部品をすべて探す。</summary>
    private static List<T> FindVisuals<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var list = new List<T>();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) list.Add(t);
            list.AddRange(FindVisuals(child, match));
        }
        return list;
    }

    [Fact]
    public async Task 仕切りが2つあり_吹き出しはまとめて見ると広く_別々に見ると元の幅になる()
    {
        var vm = await SampleMeeting();

        UiThread.Run(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content;
            var size = new Size(1400, 760);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();

            // サイドバーとチャット欄の仕切り、2列の仕切り
            Assert.NotNull(window.FindName("SidebarSplitter") as GridSplitter);
            Assert.NotNull(window.FindName("ColumnSplitter") as GridSplitter);

            static List<double> BubbleWidths(DependencyObject scope) =>
                FindVisuals<Border>(scope, b => b.Name == "bubble").Select(b => b.MaxWidth).Distinct().ToList();

            var timeline = (ItemsControl)window.FindName("ChatList");
            Assert.Equal(new[] { 960.0 }, BubbleWidths(timeline));

            vm.IsSplitView = true;
            root.UpdateLayout();
            var claudeColumn = (ItemsControl)window.FindName("ClaudeList");
            Assert.Equal(new[] { 560.0 }, BubbleWidths(claudeColumn));
        });

        vm.IsSplitView = false;
        var png = RenderWindow(() => new MainWindow { DataContext = vm }, new Size(1400, 760), "main_window_wide.png");
        Assert.True(new FileInfo(png).Length > 10_000);
    }

    [Fact]
    public void チャッピーのカードに_任せる段階の欄が出て_一部できるにすると注意が出る()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        using var dir = new TempDir();
        var session = new ChatSessionFactory(settings with { MeetingsDir = dir.Path }, new FakeRunner()).Create();
        var vm = new MainViewModel(() => session);
        vm.ChappyMember.SelectedLevelId = "partial";

        // 縦に長く描いて、サイドバーの一番下のチャッピーのカードまで見えるようにする
        var png = RenderWindow(() => new MainWindow { DataContext = vm }, new Size(1040, 1100), "main_window_chappy_level.png");
        Assert.True(new FileInfo(png).Length > 10_000);
        Assert.True(vm.ChappyMember.ShowWriterWarning);
        Assert.Contains("作業フォルダの中", vm.ChappyMember.DisplayWarning);
    }

    [Fact]
    public void 上のバーは設定ボタンだけで_設定画面に配色と制限時間の選択欄がある()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        using var dir = new TempDir();
        var session = new ChatSessionFactory(settings with { MeetingsDir = dir.Path }, new FakeRunner()).Create();
        var vm = new MainViewModel(() => session);

        UiThread.Run(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content;
            var size = new Size(1040, 720);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            Assert.Single(FindVisuals<Button>(root, b => Equals(b.Content, "設定") && b.Visibility == Visibility.Visible));
            Assert.Empty(FindVisuals<Button>(root, b => Equals(b.Content, "✏️ 名前変更…")));
            Assert.Empty(FindVisuals<Button>(root, b => b.Name == "ReadAloudButton")); // 読み上げの入り切りは、設定画面へ移した
            window.Content = null;

            var dlg = new SettingsWindow(vm, () => { });
            var dlgRoot = (FrameworkElement)dlg.Content;
            dlgRoot.Measure(new Size(440, 400));
            dlgRoot.Arrange(new Rect(new Size(440, 400)));
            dlgRoot.UpdateLayout();
            Assert.Equal(2, FindVisuals<ComboBox>(dlgRoot, c => c.Visibility == Visibility.Visible).Count);
            Assert.Equal(1, FindVisuals<CheckBox>(dlgRoot, c => c.Visibility == Visibility.Visible).Count); // 簡易表示モードだけ(読み上げは、機能を使わない設定なので出ない)
            dlg.Content = null;
        });
    }

    [Fact]
    public void AIカードの色は_任せる段階で変わる()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        using var dir = new TempDir();
        var session = new ChatSessionFactory(settings with { MeetingsDir = dir.Path }, new FakeRunner()).Create();
        var vm = new MainViewModel(() => session);

        UiThread.Run(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content;
            var size = new Size(1040, 720);

            string CardBorderColors()
            {
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();
                var cards = FindVisuals<Border>(root, b => b.Name == "card");
                return string.Join(",", cards.Select(c => (c.BorderBrush as SolidColorBrush)?.Color.ToString() ?? "-"));
            }

            var before = CardBorderColors();
            vm.ClaudeMember.SelectedLevelId = PermissionLevelOption.PartialId; // 一部できる: 黄
            var writer = CardBorderColors();
            vm.ClaudeMember.CanFull = true;
            vm.ClaudeMember.SelectedLevelId = PermissionLevelOption.FullId; // 何でもできる: 赤(黄より優先)
            var full = CardBorderColors();

            Assert.DoesNotContain("#FFE0A030", before);
            Assert.Contains("#FFE0A030", writer);
            Assert.DoesNotContain("#FFE0453F", writer);
            Assert.Contains("#FFE0453F", full);
            Assert.DoesNotContain("#FFE0A030", full);
            window.Content = null;
        });
    }

    [Fact]
    public void 自分のカードはサイドバーから消え_入力欄の横に小さなアバターが出る()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        using var dir = new TempDir();
        var session = new ChatSessionFactory(settings with { MeetingsDir = dir.Path }, new FakeRunner()).Create();
        var vm = new MainViewModel(() => session);

        UiThread.Run(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content;
            var size = new Size(1040, 720);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();

            // サイドバーのカードは、AIの3枚だけが見える(自分のカードは隠れる)
            var visibleCards = FindVisuals<Border>(root, b => b.Name == "card" && b.Visibility == Visibility.Visible);
            Assert.Equal(3, visibleCards.Count);

            // 入力欄の横に、自分の小さなアバター(28px)がある
            var avatar = FindVisuals<Grid>(root, g => g.Width == 28 && g.Height == 28 && g.DataContext == vm.UserMember);
            Assert.Single(avatar);
            Assert.Equal(vm.UserMember.DisplayName, avatar[0].ToolTip as string);
            window.Content = null;
        });
    }

    [Fact]
    public void サイドバーのAIカードだけに_ツール許可の設定ボタンが見える()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        using var dir = new TempDir();
        var session = new ChatSessionFactory(settings with { MeetingsDir = dir.Path }, new FakeRunner()).Create();
        var vm = new MainViewModel(() => session);

        UiThread.Run(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content;
            var size = new Size(1040, 720);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();

            var buttons = FindVisuals<Button>(root, b => Equals(b.Content, "")); // カードの歯車(ツール・許可の設定)
            var visible = buttons.Where(b => b.Visibility == Visibility.Visible).ToList();
            Assert.Equal(3, visible.Count); // AIの3人(クローディ・ジェミリー・チャッピー)だけ
            Assert.All(visible, b => Assert.False(string.IsNullOrWhiteSpace(b.ToolTip as string)));

            // 入力欄横の添付ボタン(📎)と音声入力ボタン(🎤)が見えることを確認する
            Assert.Single(FindVisuals<Button>(root, b => Equals(b.Content, "📎") && b.Visibility == Visibility.Visible));
            Assert.Single(FindVisuals<Button>(root, b => Equals(b.Content, "🎤") && b.Visibility == Visibility.Visible));

            // サイドバーに任せる段階のボタンの列が3つ(クローディ・ジェミリー・チャッピー)見え、
            // ComboBoxは宛先「全員」の横の答える順番の選択欄だけであることを確認する
            // (モデル・労力の選択欄は、カードの「モデル · 労力」の行を押したときだけ開く。制限時間・テーマ選択欄は、設定画面へ移した)
            static bool Shown(UIElement e) =>
                e.Visibility == Visibility.Visible && VisualTreeHelper.GetParent(e) is UIElement { Visibility: Visibility.Visible };
            var levels = FindVisuals<ListBox>(root, l => Shown(l) && l.ItemsSource is IEnumerable<PermissionLevelOption>);
            Assert.Equal(3, levels.Count);
            Assert.Contains(levels, l => l.Items.Cast<PermissionLevelOption>().Select(o => o.Label)
                .SequenceEqual(new[] { "読む", "読み書き", "全部" }));
            var combos = FindVisuals<ComboBox>(root, c => Shown(c));
            Assert.Single(combos);

            // 左下に「過去の会議」ボタンが見えることを確認する
            Assert.Single(FindVisuals<Button>(root, b => Equals(b.Content, "過去の会議") && b.Visibility == Visibility.Visible));
            window.Content = null;
        });

        // 過去の会議一覧ダイアログも画像に描けることを確認する
        var sampleMeetings = new[]
        {
            new SavedMeetingSummary("20261025_013000", "C:/meetings/20261025_013000", "C:/meetings/20261025_013000/log.md",
                new DateTime(2026, 10, 25, 1, 30, 0), "ナマケ会議のハーネス設定とファイル添付について相談", 8, true),
            new SavedMeetingSummary("20261024_211500", "C:/meetings/20261024_211500", "C:/meetings/20261024_211500/log.md",
                new DateTime(2026, 10, 24, 21, 15, 0), "週末に水耕栽培で育てる野菜を提案して", 5, false),
        };
        var pastPng = RenderWindow(
            () => new PastMeetingsWindow(sampleMeetings, _ => { }),
            new Size(620, 480),
            "past_meetings_window.png");
        Assert.True(new FileInfo(pastPng).Length > 10_000);
    }

    [Fact]
    public void スマホでつなぐQRコードの窓を_画像に描ける()
    {
        var png = RenderWindow(
            () => new RemoteQrWindow("http://100.64.0.1:8765/?t=dummy-token-for-test"),
            new Size(380, 520),
            "remote_qr_window.png");
        Assert.True(new FileInfo(png).Length > 10_000);
    }

    [Fact]
    public void Geminiの設定画面は_説明と更新の確認だけで_閉じるボタンだけがある()
    {
        var settings = SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));

        UiThread.Run(() =>
        {
            var dlg = new HarnessWindow(settings.Agy);
            Assert.Equal("🌟 Gemini のツール・許可設定", ((TextBlock)dlg.FindName("HeaderTitle")).Text);
            Assert.Null(dlg.FindName("AgyReadOnlyNoteTextBox")); // 起動引数・添え文の欄はない
            Assert.Equal(Visibility.Collapsed, ((Button)dlg.FindName("SaveButton")).Visibility);
            Assert.Equal("閉じる", ((Button)dlg.FindName("CancelButton")).Content);
        });
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control, true)]
    [InlineData(System.Windows.Input.Key.Insert, System.Windows.Input.ModifierKeys.Shift, true)]
    [InlineData(System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.None, false)]
    [InlineData(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control, false)]
    [InlineData(System.Windows.Input.Key.V, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt, false)]
    public void 貼り付けのキーだけを_画像添付の対象にする(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsPasteGesture(key, modifiers));
    }
}
