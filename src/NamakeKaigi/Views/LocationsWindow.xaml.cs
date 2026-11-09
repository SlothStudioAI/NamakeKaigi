using System.IO;
using System.Windows;
using System.Windows.Controls;
using NamakeKaigi.Models;

namespace NamakeKaigi.Views;

/// <summary>「場所」の設定ダイアログ(作業フォルダ・会議ログ・予約ファイル・各AIのCLIの場所)。</summary>
public partial class LocationsWindow : Window
{
    private readonly Action<PathSettings> _onSave;

    /// <summary>設定ダイアログを作る。</summary>
    /// <param name="current">いまの場所</param>
    /// <param name="onSave">保存ボタンが押されたときに呼ぶ処理</param>
    public LocationsWindow(PathSettings current, Action<PathSettings> onSave)
    {
        InitializeComponent();
        _onSave = onSave;
        WorkDirBox.Text = current.WorkDir;
        MeetingsDirBox.Text = current.MeetingsDir;
        ScheduleFileBox.Text = current.ScheduleFile;
        ClaudeBox.Text = current.ClaudeExecutable;
        AgyBox.Text = current.AgyExecutable;
        CodexBox.Text = current.CodexExecutable;
    }

    /// <summary>画面の入力値から、新しい場所の一覧を組み立てる。</summary>
    public PathSettings BuildEditedSettings() => new(
        WorkDir: WorkDirBox.Text.Trim(),
        MeetingsDir: MeetingsDirBox.Text.Trim(),
        ScheduleFile: ScheduleFileBox.Text.Trim(),
        ClaudeExecutable: ClaudeBox.Text.Trim(),
        AgyExecutable: AgyBox.Text.Trim(),
        CodexExecutable: CodexBox.Text.Trim());

    /// <summary>入力欄のパスから、選ぶ窓を最初に開くフォルダを決める(なければ空)。</summary>
    /// <param name="text">入力欄のパス</param>
    /// <param name="isFile">ファイルのパスか(ファイルなら、そのフォルダを開く)</param>
    /// <returns>開くフォルダ。決められなければ空</returns>
    private static string StartFolder(string text, bool isFile)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(text.Trim());
            if (expanded.Length == 0 || !Path.IsPathRooted(expanded)) return "";
            // 設定のパスは「/」区切りなので、Windows用の「\」区切りにそろえる(そのままだと、窓が開けずに落ちる)
            var full = Path.GetFullPath(expanded);
            var dir = isFile ? Path.GetDirectoryName(full) ?? "" : full;
            return Directory.Exists(dir) ? dir : "";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    /// <summary>「参照…」(フォルダ): フォルダを選ぶ窓を開き、選ばれたフォルダを入力欄に入れる。</summary>
    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextBox box }) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "フォルダを選ぶ" };
        var start = StartFolder(box.Text, isFile: false);
        if (start.Length > 0) dialog.InitialDirectory = start;
        if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName.Replace('\\', '/');
    }

    /// <summary>「参照…」(実行ファイル): ファイルを選ぶ窓を開き、選ばれたファイルを入力欄に入れる。</summary>
    private void OnBrowseExe(object sender, RoutedEventArgs e) =>
        BrowseFile(sender, "実行ファイル (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|すべてのファイル (*.*)|*.*", checkExists: true);

    /// <summary>「参照…」(予約ファイル): ファイルを選ぶ窓を開く(まだないファイルの名前も入れられる)。</summary>
    private void OnBrowseJson(object sender, RoutedEventArgs e) =>
        BrowseFile(sender, "JSON (*.json)|*.json|すべてのファイル (*.*)|*.*", checkExists: false);

    /// <summary>ファイルを選ぶ窓を開き、選ばれたファイルを入力欄に入れる。</summary>
    /// <param name="sender">押されたボタン(Tag に入力欄)</param>
    /// <param name="filter">ファイルの種類の絞り込み</param>
    /// <param name="checkExists">あるファイルだけを選ばせるか</param>
    private void BrowseFile(object sender, string filter, bool checkExists)
    {
        if (sender is not Button { Tag: TextBox box }) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "ファイルを選ぶ", Filter = filter, CheckFileExists = checkExists };
        var start = StartFolder(box.Text, isFile: true);
        if (start.Length > 0) dialog.InitialDirectory = start;
        if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName.Replace('\\', '/');
    }

    /// <summary>「保存」: 入力値を保存して閉じる。</summary>
    private void OnSave(object sender, RoutedEventArgs e)
    {
        _onSave(BuildEditedSettings());
        DialogResult = true;
        Close();
    }

    /// <summary>「キャンセル」: 保存せずに閉じる。</summary>
    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
