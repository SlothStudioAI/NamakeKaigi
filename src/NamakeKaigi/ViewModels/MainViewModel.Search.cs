using System.Windows.Input;

namespace NamakeKaigi.ViewModels;

/// <summary>メイン画面の状態とコマンド(partial): いま開いている会議内のテキスト検索。</summary>
public sealed partial class MainViewModel
{
    private readonly List<MessageViewModel> _searchHits = new();
    private bool _isChatSearchOpen;
    private string _chatSearchQuery = "";
    private int _searchHitIndex = -1;

    private ICommand? _nextSearchCommand;
    private ICommand? _prevSearchCommand;
    private ICommand? _closeSearchCommand;

    /// <summary>検索で選んだ発言まで画面をスクロールするよう要求する。</summary>
    public event Action<MessageViewModel>? ScrollToMessageRequested;

    /// <summary>会議内検索バーを表示しているか。</summary>
    public bool IsChatSearchOpen
    {
        get => _isChatSearchOpen;
        private set => Set(ref _isChatSearchOpen, value);
    }

    /// <summary>会議内検索のキーワード。</summary>
    public string ChatSearchQuery
    {
        get => _chatSearchQuery;
        set
        {
            if (!Set(ref _chatSearchQuery, value ?? "")) return;
            RefreshChatSearch(jumpToFirst: true);
        }
    }

    /// <summary>検索ヒット件数の表示（例: "2 / 5"）。</summary>
    public string ChatSearchStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_chatSearchQuery)) return "";
            if (_searchHits.Count == 0) return "0件";
            return $"{_searchHitIndex + 1} / {_searchHits.Count}";
        }
    }

    /// <summary>次の検索結果へ移るコマンド。</summary>
    public ICommand NextSearchCommand => _nextSearchCommand ??= new RelayCommand(NextSearchMatch, () => _searchHits.Count > 0);

    /// <summary>前の検索結果へ移るコマンド。</summary>
    public ICommand PrevSearchCommand => _prevSearchCommand ??= new RelayCommand(PrevSearchMatch, () => _searchHits.Count > 0);

    /// <summary>会議内検索バーを閉じるコマンド。</summary>
    public ICommand CloseSearchCommand => _closeSearchCommand ??= new RelayCommand(CloseChatSearch);

    /// <summary>会議内検索バーを開く。</summary>
    public void OpenChatSearch()
    {
        IsChatSearchOpen = true;
        if (!string.IsNullOrWhiteSpace(_chatSearchQuery))
            RefreshChatSearch(jumpToFirst: false);
    }

    /// <summary>会議内検索バーを閉じ、ハイライトを消す。</summary>
    public void CloseChatSearch()
    {
        IsChatSearchOpen = false;
        ResetChatSearch();
    }

    /// <summary>検索バーの入力をクリアしてハイライトを消す。</summary>
    public void ResetChatSearch()
    {
        _chatSearchQuery = "";
        Raise(nameof(ChatSearchQuery));
        ClearSearchHighlights();
        _searchHits.Clear();
        _searchHitIndex = -1;
        RaiseSearchStatus();
    }

    /// <summary>次のヒット発言へ移動する。</summary>
    public void NextSearchMatch()
    {
        if (_searchHits.Count == 0) return;
        _searchHitIndex = (_searchHitIndex + 1) % _searchHits.Count;
        ApplyCurrentSearchHighlight();
        ScrollToMessageRequested?.Invoke(_searchHits[_searchHitIndex]);
    }

    /// <summary>前のヒット発言へ移動する。</summary>
    public void PrevSearchMatch()
    {
        if (_searchHits.Count == 0) return;
        _searchHitIndex = (_searchHitIndex - 1 + _searchHits.Count) % _searchHits.Count;
        ApplyCurrentSearchHighlight();
        ScrollToMessageRequested?.Invoke(_searchHits[_searchHitIndex]);
    }

    private void RefreshChatSearch(bool jumpToFirst)
    {
        ClearSearchHighlights();
        _searchHits.Clear();
        _searchHitIndex = -1;

        var q = _chatSearchQuery.Trim();
        if (q.Length > 0)
        {
            foreach (var msg in Messages)
            {
                if (msg.Text.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || msg.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    msg.IsSearchMatch = true;
                    _searchHits.Add(msg);
                }
            }
        }

        if (_searchHits.Count > 0)
        {
            _searchHitIndex = jumpToFirst ? _searchHits.Count - 1 : Math.Clamp(_searchHitIndex, 0, _searchHits.Count - 1);
            ApplyCurrentSearchHighlight();
            if (jumpToFirst) ScrollToMessageRequested?.Invoke(_searchHits[_searchHitIndex]);
        }
        else
        {
            RaiseSearchStatus();
        }
    }

    private void ClearSearchHighlights()
    {
        foreach (var msg in Messages)
        {
            if (msg.IsSearchMatch) msg.IsSearchMatch = false;
            if (msg.IsCurrentSearchMatch) msg.IsCurrentSearchMatch = false;
        }
    }

    private void ApplyCurrentSearchHighlight()
    {
        for (var i = 0; i < _searchHits.Count; i++)
            _searchHits[i].IsCurrentSearchMatch = i == _searchHitIndex;
        RaiseSearchStatus();
    }

    private void RaiseSearchStatus()
    {
        Raise(nameof(ChatSearchStatus));
        ((RelayCommand)NextSearchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PrevSearchCommand).RaiseCanExecuteChanged();
    }
}
