using System.Windows;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Views;

/// <summary>
/// 発言の見た目(ChatTemplates.xaml)のコードビハインド。吹き出しのボタンが押されたときの処理を受け持つ。
/// 吹き出しの中からは、窓の MainViewModel(会議の進行)を使う。
/// </summary>
public partial class ChatTemplates : ResourceDictionary
{
    /// <summary>辞書を読み込む。</summary>
    public ChatTemplates() => InitializeComponent();

    /// <summary>押されたボタンが載っている窓の MainViewModel を探す(見つからなければnull)。</summary>
    /// <param name="sender">押されたボタン</param>
    /// <returns>MainViewModel</returns>
    private static MainViewModel? MainViewModelOf(object sender) =>
        sender is DependencyObject element ? Window.GetWindow(element)?.DataContext as MainViewModel : null;

    /// <summary>吹き出しの「🔊」が押されたとき。その返答を声で読み上げる。</summary>
    private void OnSpeakMessage(object sender, RoutedEventArgs e)
    {
        if (MainViewModelOf(sender) is not { } vm) return;
        if (sender is FrameworkElement { DataContext: MessageViewModel msg }) vm.SpeakMessage(msg);
    }

    /// <summary>吹き出しの「🔍 〇〇に聞く」が押されたとき。そのAIに、この返答についての意見を聞く。</summary>
    private void OnAskOpinion(object sender, RoutedEventArgs e)
    {
        if (MainViewModelOf(sender) is not { } vm) return;
        if (sender is FrameworkElement { DataContext: AskTargetViewModel target })
        {
            _ = vm.AskOpinionAsync(target.Owner, target.Id);
        }
    }
}
