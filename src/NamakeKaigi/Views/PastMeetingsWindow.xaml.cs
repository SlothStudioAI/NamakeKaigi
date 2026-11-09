using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NamakeKaigi.Models;

namespace NamakeKaigi.Views;

/// <summary>過去の会議の一覧から1つ選んで開き直すダイアログ。</summary>
public partial class PastMeetingsWindow : Window
{
    private readonly Action<SavedMeetingSummary> _onSelected;

    /// <summary>過去の会議一覧ダイアログを作る。</summary>
    /// <param name="meetings">過去の会議一覧(新しい順)</param>
    /// <param name="onSelected">会議が選ばれたときに呼ぶ処理</param>
    public PastMeetingsWindow(IReadOnlyList<SavedMeetingSummary> meetings, Action<SavedMeetingSummary> onSelected)
    {
        InitializeComponent();
        _onSelected = onSelected;
        MeetingsList.ItemsSource = meetings;
        if (meetings.Count == 0)
        {
            EmptyHint.Visibility = Visibility.Visible;
        }
        else
        {
            MeetingsList.SelectedIndex = 0;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenButton.IsEnabled = MeetingsList.SelectedItem is SavedMeetingSummary;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => ChooseSelected();

    private void OnOpen(object sender, RoutedEventArgs e) => ChooseSelected();

    private void ChooseSelected()
    {
        if (MeetingsList.SelectedItem is not SavedMeetingSummary item) return;
        _onSelected(item);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
