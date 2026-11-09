using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>テーマを変えたら、再起動なしでメイン画面と吹き出しの本文の色が変わるかの試験(WBS 11.27)。</summary>
public class ThemeLiveSwitchTests
{
    private static Color ColorOf(string themeId, string key) =>
        (Color)ColorConverter.ConvertFromString(ThemeCatalog.GetOrDefault(themeId).Colors[key]);

    /// <summary>画面の中から、条件に合う部品をすべて集める。</summary>
    private static List<T> FindAll<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var found = new List<T>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T t && match(t)) found.Add(t);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
        return found;
    }

    private static Color? Solid(Brush? brush) => (brush as SolidColorBrush)?.Color;

    /// <summary>部品の背景の色(背景を持たない部品ならnull)。</summary>
    private static Color? BackgroundOf(FrameworkElement e) => Solid(e switch
    {
        Border b => b.Background,
        Panel p => p.Background,
        Control c => c.Background,
        _ => null,
    });

    [Fact]
    public async Task テーマを変えると_開いているメイン画面と吹き出しの本文が_すぐ新しい色になる()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session) { InputText = "こんにちは", TargetClaude = true };
        var sending = vm.SendAsync();
        session.Finish(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Done, "**太字**の返答です"));
        await sending;

        var size = new Size(1040, 720);
        UiThread.Run(() =>
        {
            try
            {
                ThemeCatalog.Apply("forest");
                var window = new MainWindow { DataContext = vm };
                var root = (FrameworkElement)window.Content;
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();

                // サイドバー・画面の地の色の部品と、吹き出しの本文(Markdown)を覚えておく
                // (森の緑では、サイドバーとアクセントの緑が同じ色なので、その色だった名前の、ダーク側のどれかの色になればよい)
                var checks = new[] { "SidebarBackgroundBrush", "PanelBackgroundBrush" }.Select(key =>
                {
                    var forest = ColorOf("forest", key);
                    var sameKeys = ThemeCatalog.GetOrDefault("forest").Colors.Keys.Where(k => ColorOf("forest", k) == forest);
                    var expected = sameKeys.Select(k => ColorOf("dark-forest", k)).ToHashSet();
                    return (Key: key, Borders: FindAll<FrameworkElement>(root, b => BackgroundOf(b) == forest), Expected: expected);
                }).ToList();
                var bodies = FindAll<ContentControl>(root, c => c.Content is MarkdownTextBox);
                Assert.All(checks, c => Assert.NotEmpty(c.Borders));
                Assert.NotEmpty(bodies);
                Assert.Equal(ColorOf("forest", "TextPrimaryBrush"), Solid(((MarkdownTextBox)bodies[0].Content).Foreground));

                ThemeCatalog.Apply("dark-forest");
                root.UpdateLayout();

                Assert.Equal(ColorOf("dark-forest", "MainBackgroundBrush"), Solid(window.Background)); // 画面の地の色(窓そのもの)
                foreach (var c in checks)
                    Assert.All(c.Borders, b => Assert.Contains(BackgroundOf(b)!.Value, c.Expected));
                Assert.Equal(ColorOf("dark-forest", "TextPrimaryBrush"), Solid(((MarkdownTextBox)bodies[0].Content).Foreground));
                window.Close();
            }
            finally
            {
                ThemeCatalog.Apply("forest"); // ほかの試験は森の緑の前提なので戻す
            }
        });
    }
}
