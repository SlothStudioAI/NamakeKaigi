using System.Windows;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Views;

/// <summary>クロ・ジェミ・チャッピーのメンバーを一覧にして、選んだメンバーの行動ルールを見せる窓。会議には参加させない。</summary>
public partial class RosterWindow : Window
{
    private readonly RosterViewModel _viewModel;

    /// <summary>名簿の窓を作る。開いたあとに、メンバーを探しはじめる。</summary>
    /// <param name="viewModel">名簿の状態</param>
    public RosterWindow(RosterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    /// <summary>一覧の選択が変わったとき。選ばれたものを ViewModel に伝える(TreeView の選択は直接バインドできないため)。</summary>
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _viewModel.Select(e.NewValue);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
