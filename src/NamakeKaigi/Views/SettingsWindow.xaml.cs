using System.Windows;

namespace NamakeKaigi.Views;

/// <summary>設定の画面。上のバーにあった設定(配色テーマ・制限時間・簡易表示モード・読み上げ・表示名・場所)をまとめて置く。</summary>
public partial class SettingsWindow : Window
{
    private readonly Action _openAppearance;
    private readonly Action? _openLocations;

    /// <summary>設定画面を作る。</summary>
    /// <param name="viewModel">メイン画面と同じ画面の状態(変更はすぐ反映され、保存される)</param>
    /// <param name="openAppearance">「名前変更…」が押されたときに、表示名の設定を開く処理</param>
    /// <param name="openLocations">「場所…」が押されたときに、場所の設定を開く処理(省略時は何もしない)</param>
    public SettingsWindow(ViewModels.MainViewModel viewModel, Action openAppearance, Action? openLocations = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _openAppearance = openAppearance;
        _openLocations = openLocations;
    }

    /// <summary>「名前変更…」: 表示名の設定を開く。</summary>
    private void OnOpenAppearance(object sender, RoutedEventArgs e) => _openAppearance();

    /// <summary>「場所…」: 場所(作業フォルダ・会議ログ・予約・CLI)の設定を開く。</summary>
    private void OnOpenLocations(object sender, RoutedEventArgs e) => _openLocations?.Invoke();

    /// <summary>「閉じる」: 設定画面を閉じる。</summary>
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
