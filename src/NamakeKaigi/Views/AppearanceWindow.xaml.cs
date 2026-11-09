using System.Windows;
using NamakeKaigi.Models;

namespace NamakeKaigi.Views;

/// <summary>参加者の表示名の変更ダイアログ。イラストの切り替えは設定画面、アプリ名は固定。</summary>
public partial class AppearanceWindow : Window
{
    private readonly AppearanceSettings _current;
    private readonly Action<AppearanceSettings> _onSave;

    /// <summary>設定ダイアログを作る。</summary>
    /// <param name="current">いまの設定</param>
    /// <param name="onSave">保存ボタンが押されたときに呼ぶ処理</param>
    public AppearanceWindow(AppearanceSettings current, Action<AppearanceSettings> onSave)
    {
        InitializeComponent();
        _current = current;
        _onSave = onSave;
        UserNameBox.Text = current.UserName ?? "";
        ClaudeNameBox.Text = current.ClaudeName ?? "";
        GeminiNameBox.Text = current.GeminiName ?? "";
    }

    /// <summary>画面の入力値から新しい設定を組み立てる(イラストの切り替えは、いまの値のまま)。</summary>
    public AppearanceSettings BuildEditedSettings() => _current with
    {
        UserName = UserNameBox.Text.Trim(),
        ClaudeName = ClaudeNameBox.Text.Trim(),
        GeminiName = GeminiNameBox.Text.Trim(),
    };

    private void OnResetDefaultPreset(object sender, RoutedEventArgs e)
    {
        UserNameBox.Text = "";
        ClaudeNameBox.Text = "";
        GeminiNameBox.Text = "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _onSave(BuildEditedSettings());
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
