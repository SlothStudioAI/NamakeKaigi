using System.Windows;
using System.Windows.Controls;

namespace NamakeKaigi.Views;

/// <summary>
/// 起動時にAIのCLIツールが1つも見つからなかったときに表示する導入ガイド。
/// コマンドをコピーして導入したあと、「再検出する」でそのまま会議室を開ける。
/// </summary>
public partial class CliSetupGuideWindow : Window
{
    private readonly Func<bool> _retryDetect;

    /// <summary>導入ガイドウィンドウを作る。</summary>
    /// <param name="retryDetect">「再検出する」押下時に実行する検出処理（1つ以上見つかれば true）</param>
    public CliSetupGuideWindow(Func<bool> retryDetect)
    {
        _retryDetect = retryDetect;
        InitializeComponent();
    }

    private void OnCopyCommand(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command }) return;
        Clipboard.SetText(command);
        ShowStatus($"コマンドをコピーしました: {command}");
    }

    private void OnRetryDetect(object sender, RoutedEventArgs e)
    {
        if (_retryDetect())
        {
            DialogResult = true;
            Close();
            return;
        }
        ShowStatus("まだ見つかりませんでした。インストール直後の場合は、一度アプリを開き直すか、設定の「場所」で指定してください。");
    }

    private void OnContinueAnyway(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
