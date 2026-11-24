using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NamakeKaigi.Models;

namespace NamakeKaigi.Views;

/// <summary>過去の会議の一覧から1つ選んで開き直すダイアログ。</summary>
public partial class PastMeetingsWindow : Window
{
    /// <summary>初期状態で一覧に表示する最大件数。</summary>
    public const int DefaultDisplayLimit = 20;

    private readonly IReadOnlyList<SavedMeetingSummary> _allMeetings;
    private readonly Action<SavedMeetingSummary> _onSelected;
    private readonly Func<SavedMeetingSummary, Task>? _onOpenWithSummary;
    private readonly bool _canSummarize;
    private readonly Dictionary<string, string> _searchTextCache = new();
    private bool _isBusy;
    private bool _showAll;

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

    /// <summary>一覧に表示する会議リストと非表示件数を計算する(通常は1世代目・最大20件、検索時は全対象から絞り込む)。</summary>
    public static (IReadOnlyList<SavedMeetingSummary> Visible, int HiddenCount) FilterForDisplay(
        IReadOnlyList<SavedMeetingSummary> all,
        bool hideScheduled,
        bool showAll,
        int limit = DefaultDisplayLimit,
        string? query = null,
        Func<SavedMeetingSummary, string>? textProvider = null)
    {
        var baseList = hideScheduled ? all.Where(m => !m.IsScheduled).ToList() : all.ToList();
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0)
        {
            var getText = textProvider ?? Services.MeetingStore.ReadSearchableText;
            var matched = baseList.Where(m =>
            {
                var text = getText(m);
                return terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
            }).ToList();
            return (matched, 0);
        }

        if (showAll) return (baseList, 0);

        var latestGen = baseList.Where(m => !m.IsHandedOver).Take(Math.Max(1, limit)).ToList();
        return (latestGen, Math.Max(0, baseList.Count - latestGen.Count));
    }

    private string GetCachedSearchableText(SavedMeetingSummary item)
    {
        if (!_searchTextCache.TryGetValue(item.FolderPath, out var text))
        {
            text = Services.MeetingStore.ReadSearchableText(item);
            _searchTextCache[item.FolderPath] = text;
        }
        return text;
    }

    private void ApplyFilter()
    {
        if (_allMeetings is null || MeetingsList is null) return;
        var hideScheduled = HideScheduledCheckBox.IsChecked == true;
        var query = SearchBox?.Text ?? "";
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        if (SearchPlaceholder is not null)
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        if (ClearSearchButton is not null)
            ClearSearchButton.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

        var (visible, hiddenCount) = FilterForDisplay(_allMeetings, hideScheduled, _showAll, DefaultDisplayLimit, query, GetCachedSearchableText);
        MeetingsList.ItemsSource = visible;

        if (ShowMoreButton is not null)
        {
            ShowMoreButton.Visibility = !hasQuery && hiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowMoreButton.Content = $"もっと見る（他 {hiddenCount}件）";
        }

        if (visible.Count == 0)
        {
            EmptyHint.Text = hasQuery ? "一致する過去の会議が見つかりませんでした。" : "保存されている過去の会議がまだありません。";
            EmptyHint.Visibility = Visibility.Visible;
            UpdateButtons();
        }
        else
        {
            EmptyHint.Visibility = Visibility.Collapsed;
            MeetingsList.SelectedIndex = 0;
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        if (SearchBox is null) return;
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void OnShowMore(object sender, RoutedEventArgs e)
    {
        _showAll = true;
        ApplyFilter();
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
