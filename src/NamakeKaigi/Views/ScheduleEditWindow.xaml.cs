using System.ComponentModel;
using System.Windows;
using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Views;

/// <summary>予約の追加・編集の窓。保存を押すと、入力から作った予約を Result に入れて閉じる。</summary>
public partial class ScheduleEditWindow : Window
{
    private readonly ScheduleEditViewModel _viewModel;

    /// <summary>編集の窓を作る。</summary>
    /// <param name="viewModel">編集の状態</param>
    public ScheduleEditWindow(ScheduleEditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        UpdateSaveButton();
    }

    /// <summary>保存された予約(キャンセルならnull)。</summary>
    public ScheduledJob? Result { get; private set; }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScheduleEditViewModel.ValidationError)) UpdateSaveButton();
    }

    /// <summary>入力に誤りがある間は、保存を押せなくする。</summary>
    private void UpdateSaveButton() => SaveButton.IsEnabled = _viewModel.ValidationError.Length == 0;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.Build();
        DialogResult = true;
    }
}
