using System.Windows;
using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Views;

/// <summary>予約(定期実行)の一覧・ON/OFF・今すぐ実行・実行の履歴を見せる窓。</summary>
public partial class ScheduleWindow : Window
{
    private readonly ScheduleViewModel _viewModel;

    /// <summary>自動実行の窓を作る。</summary>
    /// <param name="viewModel">予約の状態</param>
    public ScheduleWindow(ScheduleViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>「＋ 予約を追加」が押されたとき。</summary>
    private void OnAdd(object sender, RoutedEventArgs e) => ShowEditor(null);

    /// <summary>行の「編集」が押されたとき。</summary>
    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ScheduleItemViewModel item) ShowEditor(item.Job);
    }

    /// <summary>行の「削除」が押されたとき。確認してから消す。</summary>
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ScheduleItemViewModel item) return;
        var answer = MessageBox.Show(this, $"予約「{item.Name}」を削除します。よろしいですか?" + Environment.NewLine + "(過去の実行の履歴は残ります)",
            "予約の削除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) _viewModel.Delete(item.Job.Id);
    }

    /// <summary>追加・編集の窓を開き、保存されたら予約に反映する。</summary>
    /// <param name="job">編集する予約(新しく足すならnull)</param>
    private void ShowEditor(ScheduledJob? job)
    {
        var dialog = new ScheduleEditWindow(_viewModel.CreateEditor(job)) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } saved) _viewModel.AddOrUpdate(saved);
    }
}
