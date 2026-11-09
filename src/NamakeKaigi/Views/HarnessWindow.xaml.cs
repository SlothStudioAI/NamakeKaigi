using System.Windows;
using System.Windows.Controls;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Views;

/// <summary>AIごとのハーネス(ツール・許可の範囲)を確認・編集するウィンドウ。</summary>
public partial class HarnessWindow : Window
{
    private const double ToolCheckBoxWidth = 250;

    private readonly ClaudeSettings? _claudeCurrent;
    private readonly Action<ClaudeSettings>? _onClaudeSave;
    private readonly Func<CancellationToken, Task<AgyUpdateResult>>? _checkAgyUpdate;
    private readonly CancellationTokenSource _closing = new(); // 画面を閉じたら、動いている確認を止める
    private bool _connectorsLoaded;

    /// <summary>クローディ(Claude CLI)用のハーネス設定画面を作る。</summary>
    /// <param name="current">いまのClaude設定</param>
    /// <param name="onSave">保存されたときに呼ぶ処理</param>
    /// <param name="loadConnectors">いまある外部連携の一覧を取る処理(取れなければnullを返す。省略時は一覧を出さない)</param>
    public HarnessWindow(ClaudeSettings current, Action<ClaudeSettings> onSave,
        Func<Task<IReadOnlyList<string>?>>? loadConnectors = null)
    {
        InitializeComponent();
        _claudeCurrent = current;
        _onClaudeSave = onSave;

        Title = "ツール・許可の設定: Claude";
        HeaderTitle.Text = "🦥 Claude のツール・許可設定";
        ClaudePanel.Visibility = Visibility.Visible;
        AgyPanel.Visibility = Visibility.Collapsed;

        var writerTools = HarnessInfo.ParseTools(
            current.CommandExtraArgs is { Count: > 0 } ? current.CommandExtraArgs : current.WriterExtraArgs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 書く担当のみの引数と、コマンドありの引数の両方を踏まえ、書く担当時のチェック状態を初期化する
        var writerOnlyTools = HarnessInfo.ParseTools(current.WriterExtraArgs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (value, label) in HarnessInfo.CommonTools)
        {
            var isChecked = string.Equals(value, "PowerShell", StringComparison.OrdinalIgnoreCase)
                ? writerTools.Contains("PowerShell")
                : writerOnlyTools.Contains(value) || writerTools.Contains(value);
            WriterToolsCheckPanel.Children.Add(new CheckBox
            {
                Content = label,
                Tag = value,
                IsChecked = isChecked,
                Width = ToolCheckBoxWidth,
                Margin = new Thickness(0, 0, 8, 6),
            });
        }

        var commonNames = HarnessInfo.CommonTools.Select(t => t.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        WriterExtraToolsTextBox.Text = string.Join(", ", writerOnlyTools.Where(t => !commonNames.Contains(t)));

        var readTools = HarnessInfo.ParseTools(current.ExtraArgs).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (value, label) in HarnessInfo.CommonTools)
        {
            ReadOnlyToolsCheckPanel.Children.Add(new CheckBox
            {
                Content = label,
                Tag = value,
                IsChecked = readTools.Contains(value),
                Width = ToolCheckBoxWidth,
                Margin = new Thickness(0, 0, 8, 6),
            });
        }

        var allowed = HarnessInfo.ParseAllowedCommands(current.CommandExtraArgs);
        AllowedCommandsTextBox.Text = string.Join(Environment.NewLine, allowed);

        if (loadConnectors is null)
        {
            ShowConnectors(null);
        }
        else
        {
            Loaded += async (_, _) => ShowConnectors(await loadConnectors());
        }
    }

    /// <summary>
    /// 外部連携のチェックを並べる。ONにしてあるのに一覧にない連携も、チェックを外したことにならないよう残す。
    /// </summary>
    /// <param name="known">いまある連携の名前(取れなかったらnull)</param>
    public void ShowConnectors(IReadOnlyList<string>? known)
    {
        if (_claudeCurrent is null) return;
        var enabled = _claudeCurrent.EnabledConnectors ?? Array.Empty<string>();
        ConnectorsCheckPanel.Children.Clear();
        foreach (var name in (known ?? Array.Empty<string>()).Concat(enabled).Distinct(StringComparer.Ordinal))
        {
            var missing = known is not null && !known.Contains(name);
            ConnectorsCheckPanel.Children.Add(new CheckBox
            {
                Content = missing ? $"{name} (いまは見つかりません)" : name,
                Tag = name,
                IsChecked = enabled.Contains(name),
                Width = ToolCheckBoxWidth,
                Margin = new Thickness(0, 0, 8, 6),
            });
        }
        ConnectorsStatusText.Text = known is null ? "連携の一覧を取れませんでした(claude mcp list)。保存しても、ONの連携は今のままです。"
            : known.Count == 0 ? "使える連携はありません。"
            : $"{known.Count}件の連携が見つかりました。";
        _connectorsLoaded = known is not null;
    }

    /// <summary>
    /// Gemini (agy) 用の設定画面を作る。道具を1つずつ選べないので、段階の説明と更新の確認だけを出す
    /// (起動引数・添え文は settings.json の値を使い、画面では変えない)。
    /// </summary>
    /// <param name="current">いまのagy設定(Gemini 用の画面であることを示す)</param>
    /// <param name="checkUpdate">agy の更新を確認する処理(省略時は、更新の欄を出さない)</param>
    public HarnessWindow(AgySettings current, Func<CancellationToken, Task<AgyUpdateResult>>? checkUpdate = null)
    {
        InitializeComponent();
        _checkAgyUpdate = checkUpdate;
        AgyUpdateBorder.Visibility = checkUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        Closed += (_, _) => _closing.Cancel();

        Title = "ツール・許可の設定: Gemini";
        HeaderTitle.Text = "🌟 Gemini のツール・許可設定";
        HeaderNote.Text = "Gemini でできることは、カードで選ぶ段階で決まります。ここでは、その違いを確かめられます。";
        ClaudePanel.Visibility = Visibility.Collapsed;
        AgyPanel.Visibility = Visibility.Visible;

        // 画面で変えるものがないので、保存はなく「閉じる」だけにする
        SaveButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = "閉じる";
        CancelButton.Margin = new Thickness(0);
    }

    /// <summary>
    /// ChatGPT (Codex) 用の設定画面を作る。道具を1つずつ選べないので、段階の説明とモデルの選び方だけを出す
    /// (起動引数・添え文は settings.json の値を使い、画面では変えない)。
    /// </summary>
    /// <param name="current">いまのCodex設定(ChatGPT 用の画面であることを示す)</param>
    public HarnessWindow(CodexSettings current)
    {
        InitializeComponent();
        Closed += (_, _) => _closing.Cancel();

        Title = "ツール・許可の設定: ChatGPT";
        HeaderTitle.Text = "💬 ChatGPT のツール・許可設定";
        HeaderNote.Text = "ChatGPT (Codex) でできることは、カードで選ぶ段階で決まります。ここでは、その違いを確かめられます。";
        ClaudePanel.Visibility = Visibility.Collapsed;
        CodexPanel.Visibility = Visibility.Visible;

        // 画面で変えるものがないので、保存はなく「閉じる」だけにする
        SaveButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = "閉じる";
        CancelButton.Margin = new Thickness(0);
    }

    /// <summary>現在の画面入力から、保存後の ClaudeSettings を組み立てる(試験からも呼べる)。</summary>
    public ClaudeSettings BuildEditedClaudeSettings()
    {
        if (_claudeCurrent is null) throw new InvalidOperationException("Claude用の画面ではありません。");
        var readTools = ReadOnlyToolsCheckPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .ToList();

        var writerTools = WriterToolsCheckPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .Concat(WriterExtraToolsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        var allowedCommands = AllowedCommandsTextBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var edited = HarnessInfo.BuildClaudeSettings(_claudeCurrent, readTools, writerTools, allowedCommands);
        if (!_connectorsLoaded) return edited; // 一覧が出ていないときは、ONの連携を今のまま残す

        var connectors = ConnectorsCheckPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .ToList();
        return edited with { EnabledConnectors = connectors };
    }

    /// <summary>「更新を確認」: agy の更新係を起動して、結果を横に出す(終わるまでボタンを押せなくする)。</summary>
    private async void OnCheckAgyUpdate(object sender, RoutedEventArgs e)
    {
        if (_checkAgyUpdate is null) return;
        AgyUpdateButton.IsEnabled = false;
        AgyUpdateStatusText.Text = "確認しています…";
        try
        {
            AgyUpdateStatusText.Text = AgyUpdater.Describe(await _checkAgyUpdate(_closing.Token));
        }
        catch (OperationCanceledException)
        {
            // 画面を閉じて止めたときは、何も出さない
        }
        catch (Exception ex)
        {
            AgyUpdateStatusText.Text = $"確認できませんでした: {ex.Message}";
        }
        finally
        {
            AgyUpdateButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_claudeCurrent is not null && _onClaudeSave is not null)
        {
            _onClaudeSave(BuildEditedClaudeSettings());
        }
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
