using System.Collections.ObjectModel;
using System.Windows.Input;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): 見た目・テーマ・制限時間・見方(まとめて/別々)。</summary>
public sealed partial class MainViewModel
{
    private bool _isSplitView;

    private string _selectedThemeId = ThemeCatalog.DefaultThemeId;

    private int _selectedTimeoutSec;

    // 制限時間の選択肢(秒)。会話だけなら短く、開発作業なら長く選ぶ(承認ボタンの返事を待つ間は数えない)。
    // 保存済みの値が一覧にないとき(以前の3分・20分など)は、その値も一覧に足して出す
    private static readonly int[] TimeoutChoicesSec = { 300, 600, 1800, 3600, 7200 };

    /// <summary>いまの作業フォルダ(ツールチップや変更処理に使うフルパス)。</summary>
    public string WorkDir => _session.WorkDir;

    /// <summary>画面の上に出す作業フォルダ(簡易表示モード時は末尾のフォルダ名だけに省略)。</summary>
    public string WorkDirDisplay => Appearance.FormatWorkDir(WorkDir);

    /// <summary>作業フォルダの末尾の名前(見出しの札に出す。モードに関係なく短くする。フルパスは説明に出す)。</summary>
    public string WorkDirName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WorkDir)) return WorkDir;
            var name = System.IO.Path.GetFileName(WorkDir.TrimEnd('/', '\\'));
            return string.IsNullOrEmpty(name) ? WorkDir : name;
        }
    }

    /// <summary>いまの簡易表示モード・表示名の設定。</summary>
    public AppearanceSettings Appearance => _session.Appearance;

    /// <summary>簡易表示モード(キャラ絵を隠して頭文字バッジにし、パスを省略表示する)ならtrue。</summary>
    public bool IsBusinessMode
    {
        get => Appearance.IsBusinessMode;
        set
        {
            if (Appearance.IsBusinessMode == value) return;
            UpdateAppearance(Appearance with { IsBusinessMode = value });
        }
    }

    /// <summary>アプリの名前(ウィンドウの Title=タスクバー・Alt+Tab に出す。固定)。</summary>
    public string AppTitle => AppearanceSettings.AppName;

    /// <summary>タイトルバーに出す見出し(アプリ名と MDD。固定)。</summary>
    public string WindowHeaderTitle => AppearanceSettings.WindowHeaderTitle;

    /// <summary>タイトルバーの看板の右に、小さく薄く出すバージョン(例: 「v0.9.0」)。</summary>
    public string WindowHeaderVersion => AppVersion.Label;

    /// <summary>宛先ピルに出すClaude側の表示名。</summary>
    public string ClaudeDisplayName => ClaudeMember.DisplayName;

    /// <summary>宛先ピルに出すGemini側の表示名。</summary>
    public string GeminiDisplayName => GeminiMember.DisplayName;

    /// <summary>宛先のボタンに出す、チャッピーの名前。</summary>
    public string ChappyDisplayName => ChappyMember.DisplayName;

    /// <summary>簡易表示モードや表示名の設定を更新し、画面全体と settings.json に反映する。</summary>
    public void UpdateAppearance(AppearanceSettings appearance)
    {
        _session.SetAppearance(appearance);
        ApplyAppearanceToAll();
        AppearanceUpdated?.Invoke(this, appearance);
    }

    private void ApplyAppearanceToAll()
    {
        var appearance = Appearance;
        foreach (var m in Members) m.ApplyAppearance(appearance);
        foreach (var msg in Messages) msg.ApplyAppearance(appearance);
        Raise(nameof(Appearance));
        Raise(nameof(IsBusinessMode));
        Raise(nameof(ClaudeDisplayName));
        Raise(nameof(GeminiDisplayName));
        Raise(nameof(ChappyDisplayName));
        RefreshAllOrderChoice();
        Raise(nameof(LogPathDisplay));
        Raise(nameof(WorkDirDisplay));
    }

    /// <summary>選べる配色テーマの一覧(緑のバリエーション3種＋ダークモード2種＋オフィス2種)。</summary>
    public IReadOnlyList<ThemePreset> ThemeOptions => ThemeCatalog.All;

    /// <summary>いま選ばれている配色テーマのID。選ぶとその場で全画面の色が切り替わり、settings.json にも保存される。</summary>
    public string SelectedThemeId
    {
        get => _selectedThemeId;
        set
        {
            var normalized = ThemeCatalog.GetOrDefault(value).Id;
            if (!Set(ref _selectedThemeId, normalized)) return;
            _session.SetTheme(normalized);
            ThemeCatalog.Apply(normalized);
        }
    }

    /// <summary>選べる制限時間の一覧。設定ファイルの値が選択肢にないときは、それも並べる。</summary>
    public IReadOnlyList<TimeoutOption> TimeoutOptions =>
        TimeoutChoicesSec.Append(_selectedTimeoutSec).Where(s => s > 0).Distinct().Order()
            .Select(s => new TimeoutOption(s)).ToList();

    /// <summary>いま選ばれている制限時間(秒)。選ぶと両方のAIの次の発言から効き、settings.json にも保存される。</summary>
    public int SelectedTimeoutSec
    {
        get => _selectedTimeoutSec;
        set
        {
            if (value <= 0 || !Set(ref _selectedTimeoutSec, value)) return;
            _session.SetTimeout(value);
        }
    }

    /// <summary>別々に見る画面ならtrue(falseなら、まとめて見る画面)。会議ログは変わらず、見せ方だけが変わる。</summary>
    public bool IsSplitView
    {
        get => _isSplitView;
        set
        {
            if (!Set(ref _isSplitView, value)) return;
            Raise(nameof(IsTimelineView));
        }
    }

    // ラジオボタン(画面の切り替え)と結びつける真偽値
    public bool IsTimelineView { get => !_isSplitView; set { if (value) IsSplitView = false; } }
}
