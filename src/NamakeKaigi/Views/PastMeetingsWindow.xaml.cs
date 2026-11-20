using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NamakeKaigi.Models;

namespace NamakeKaigi.Views;

/// <summary>過去の会議の一覧から1つ選んで開き直すダイアログ。</summary>
public partial class PastMeetingsWindow : Window
{
    private readonly IReadOnlyList<SavedMeetingSummary> _allMeetings;
    private readonly Action<SavedMeetingSummary> _onSelected;
    private readonly Func<SavedMeetingSummary, Task>? _onOpenWithSummary;
    private readonly bool _canSummarize;
    private bool _isBusy;

    /// <summary>過去の会議一覧ダイアログを作る。</summary>
    /// <param name="meetings">過去の会議一覧(新しい順)</param>
    /// <param name="onSelected">「そのまま開く」が選ばれたときに呼ぶ処理</param>
    /// <param name="onOpenWithSummary">「要約を引き継いで新しく開く」が選ばれたときに呼ぶ処理(省略可)</param>
    /// <param name="canSummarize">要約生成機能が有効か</param>
    public PastMeetingsWindow(
        IReadOnlyList<SavedMeetingSummary> meetings,
        Action<SavedMeetingSummary> onSelected,
        Func<SavedMeetingSummary, Task>? onOpenWithSummary = null,
        bool canSummarize = true)
    {
        _allMeetings = meetings;
        _onSelected = onSelected;
        _onOpenWithSummary = onOpenWithSummary;
        _canSummarize = canSummarize;
        InitializeComponent();
        if (_onOpenWithSummary is null) OpenWithSummaryButton.Visibility = Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_allMeetings is null || MeetingsList is null) return;
        var hideScheduled = HideScheduledCheckBox.IsChecked == true;
        var filtered = hideScheduled ? _allMeetings.Where(m => !m.IsScheduled).ToList() : _allMeetings.ToList();
        MeetingsList.ItemsSource = filtered;
        if (filtered.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
            UpdateButtons();
        }
        else
        {
            EmptyHint.Visibility = Visibility.Collapsed;
            MeetingsList.SelectedIndex = 0;
        }
    }

    private void UpdateButtons()
    {
        var selected = MeetingsList.SelectedItem as SavedMeetingSummary;
        OpenButton.IsEnabled = !_isBusy && selected is not null;
        OpenWithSummaryButton.IsEnabled = !_isBusy
            && _onOpenWithSummary is not null
            && selected is not null
            && (selected.HasSummary || _canSummarize);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => ChooseSelected();

    private void OnOpen(object sender, RoutedEventArgs e) => ChooseSelected();

    private void ChooseSelected()
    {
        if (_isBusy || MeetingsList.SelectedItem is not SavedMeetingSummary item) return;
        _onSelected(item);
        DialogResult = true;
    }

    private async void OnOpenWithSummary(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _onOpenWithSummary is null || MeetingsList.SelectedItem is not SavedMeetingSummary item) return;
        _isBusy = true;
        OpenWithSummaryButton.Content = "要約を準備中…";
        CancelButton.IsEnabled = false;
        MeetingsList.IsEnabled = false;
        UpdateButtons();
        try
        {
            await _onOpenWithSummary(item);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"要約の引き継ぎに失敗しました。\n{ex.Message}", "ナマケ会議", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isBusy = false;
            OpenWithSummaryButton.Content = "要約を引き継いで新しく開く";
            CancelButton.IsEnabled = true;
            MeetingsList.IsEnabled = true;
            UpdateButtons();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!_isBusy) DialogResult = false;
    }
}
