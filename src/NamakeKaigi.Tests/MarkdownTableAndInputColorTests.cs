using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NamakeKaigi.Services;
using NamakeKaigi.Models;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>吹き出しの表・下線の描画と、ダークテーマでの入力欄の文字色・カーソル色(WBS 11.13)の試験。</summary>
public class MarkdownTableAndInputColorTests
{
    private const string SampleTable =
        "## 段取り\n" +
        "| # | 担当 | やること |\n" +
        "|---|:---:|---|\n" +
        "| 1 | クローディ | **原因**を調べる |\n" +
        "| 2 | 修正完了 | 600秒 |\n" +
        "| 3 | ジェミリー | `git show` で査読 \\| 指摘 |\n" +
        "\n" +
        "---\n" +
        "表のあとの文";

    [Theory]
    [InlineData("| a | b |", new[] { "a", "b" })]
    [InlineData("|a|b", new[] { "a", "b" })]
    [InlineData("| x \\| y | z |", new[] { "x | y", "z" })]
    [InlineData("| 末尾が文字 \\|", new[] { "末尾が文字 |" })]
    [InlineData("| | 空 |", new[] { "", "空" })]
    public void 表の1行をセルに分け_エスケープした縦棒は文字として残す(string line, string[] expected)
    {
        Assert.Equal(expected, MarkdownSyntax.SplitTableRow(line));
    }

    [Theory]
    [InlineData("|---|---|", true)]
    [InlineData("| :--- | :---: | ---: |", true)]
    [InlineData("| a | b |", false)]
    [InlineData("|", false)]
    [InlineData("| : |", false)]
    [InlineData("---", false)]
    public void 見出しと本文の区切りの行だけを_区切りと判定する(string line, bool expected)
    {
        Assert.Equal(expected, MarkdownSyntax.IsTableSeparator(line));
    }

    [Fact]
    public void 区切りの行がない縦棒の行は_表にしない()
    {
        Assert.False(MarkdownSyntax.ContainsMarkdownSyntax("| ただの縦棒 |"));
        Assert.True(MarkdownSyntax.ContainsMarkdownSyntax("| a |\n|---|\n| 1 |"));
    }

    [Theory]
    [InlineData("---", true)]
    [InlineData("***", true)]
    [InlineData("- - -", true)]
    [InlineData("___", true)]
    [InlineData("--", false)]
    [InlineData("-*-", false)]
    [InlineData("- 箇条書き", false)]
    public void 区切り線の行だけを_区切り線と判定する(string line, bool expected)
    {
        Assert.Equal(expected, MarkdownSyntax.IsHorizontalRule(line));
    }

    [Theory]
    [InlineData("abc", 3)]
    [InlineData("修正完了", 8)]
    [InlineData("600秒", 5)]
    [InlineData("**太字**", 4)]
    [InlineData("`git`", 3)]
    [InlineData("ｶﾅ", 2)]
    public void 列幅の文字数は_全角を2文字分として数え_記号は数えない(string text, int expected)
    {
        Assert.Equal(expected, MarkdownSyntax.DisplayWidth(text));
    }

    /// <summary>Markdownの本文を組み立てる(MarkdownTextBox になる)。</summary>
    private static MarkdownTextBox Build(string markdown) => (MarkdownTextBox)MarkdownBlockBuilder.BuildElement(markdown);

    /// <summary>文書の部品の文字を取り出す。</summary>
    private static string TextOf(TextElement element) => new TextRange(element.ContentStart, element.ContentEnd).Text;

    [Fact]
    public void 表は見出しと本文の行数と列数どおりの表になり_前後の文も残る()
    {
        UiThread.Run(() =>
        {
            var box = Build(SampleTable);
            var table = box.Document.Blocks.OfType<Table>().Single();
            var rows = table.RowGroups.Single().Rows;

            Assert.Equal(3, table.Columns.Count);
            Assert.Equal(4, rows.Count); // 見出し1行＋本文3行(区切りの行は入らない)
            Assert.Equal(FontWeights.Bold, rows[0].FontWeight); // 見出しは太字
            Assert.Contains("|", TextOf(rows[^1].Cells[^1]));

            // 表のあとの文も、表の中に吸い込まれずに残る
            Assert.Contains(box.Document.Blocks.OfType<Paragraph>(), p => TextOf(p) == "表のあとの文");
        });
    }

    [Fact]
    public void 短い列は中身の幅に合わせ_長い列は残りの幅を分け合う()
    {
        UiThread.Run(() =>
        {
            const string table =
                "| 機能・項目 | 状態 | 実機での確認ポイント |\n" +
                "|---|---|---|\n" +
                "| **入力欄の文字・カーソル** | 修正完了 | 右上で「夜の森」に切り替えたとき、文字とカーソルが見えるか |\n" +
                "| **タイムアウト時間** | 600秒 | 10分設定で長時間の開発作業も止まらないか |";
            var box = Build(table);
            box.Measure(new Size(600, double.PositiveInfinity));
            var columns = box.Document.Blocks.OfType<Table>().Single().Columns;
            var widths = columns.Select(c => c.Width.Value).ToList();

            Assert.All(columns, c => Assert.True(c.Width.IsAbsolute)); // すべて測ったpxで決める
            Assert.InRange(widths[1], 40, 120);    // 「状態」は、中身の幅(折り返さない)
            Assert.True(widths[2] > widths[0]);    // 長い列ほど広い
            Assert.True(widths.Sum() <= 600, $"合計 {widths.Sum()}"); // 使える幅に収まる
        });
    }

    [Fact]
    public void 区切り線は文字ではなく_線として描く()
    {
        UiThread.Run(() =>
        {
            var box = Build("上の文\n\n---\n\n下の文");
            Assert.DoesNotContain(box.Document.Blocks.OfType<Paragraph>(), p => TextOf(p).Contains("---"));
            var rule = box.Document.Blocks.OfType<BlockUIContainer>().Single();
            Assert.Equal(1, ((Border)rule.Child).Height);
        });
    }

    [Fact]
    public void 下線のタグは下線になり_タグの文字は残らない()
    {
        UiThread.Run(() =>
        {
            Assert.True(MarkdownSyntax.ContainsMarkdownSyntax("これは<u>大事</u>です"));
            var paragraph = Build("これは<u>**大事**</u>です").Document.Blocks.OfType<Paragraph>().Single();

            Assert.Equal("これは大事です", TextOf(paragraph));
            var underline = paragraph.Inlines.OfType<Underline>().Single();
            Assert.IsType<Bold>(underline.Inlines.Single()); // 下線の中の太字も効く
        });
    }

    [Fact]
    public void 閉じのない下線のタグは_そのままの文字で出す()
    {
        UiThread.Run(() =>
        {
            var paragraph = Build("**注意** <u>閉じ忘れ").Document.Blocks.OfType<Paragraph>().Single();
            Assert.Equal("注意 <u>閉じ忘れ", TextOf(paragraph));
        });
    }

    [Theory]
    [InlineData("- 箇条", 0, null, "箇条")]
    [InlineData("* 箇条", 0, null, "箇条")]
    [InlineData("1. **外部連携**: 説明", 0, 1, "**外部連携**: 説明")]
    [InlineData("12) 十二", 0, 12, "十二")]
    [InlineData("   - 3つ下げ", 1, null, "3つ下げ")]
    [InlineData("    1. 4つ下げ", 2, 1, "4つ下げ")]
    public void 箇条書きと番号つきの行を_深さと番号と中身に分ける(string line, int level, int? number, string content)
    {
        Assert.True(MarkdownSyntax.TryParseListItem(line, out var actualLevel, out var actualNumber, out var actualContent));
        Assert.Equal(level, actualLevel);
        Assert.Equal(number, actualNumber);
        Assert.Equal(content, actualContent);
    }

    [Theory]
    [InlineData("1.5倍にする")]
    [InlineData("2026. 年号")]
    [InlineData("**太字**で始まる")]
    [InlineData("-ハイフンのみ")]
    [InlineData("1.")]
    public void 箇条書きでも番号つきでもない行は_そのまま文として扱う(string line)
    {
        Assert.False(MarkdownSyntax.TryParseListItem(line, out _, out _, out _));
    }

    [Fact]
    public void 番号つきの行は番号つきの箇条書きになり_続く行は同じ入れ物に入る()
    {
        UiThread.Run(() =>
        {
            Assert.True(MarkdownSyntax.ContainsMarkdownSyntax("1. 一つ目\n2. 二つ目"));
            var box = Build("残りは次の2つです。\n\n1. **外部連携の切り替え**: とても長い説明が続いて、折り返す\n2. 承認ボタン\n   - 下げた箇条\n3. 三つ目");
            var lists = box.Document.Blocks.OfType<List>().ToList();

            Assert.Equal(3, lists.Count); // 1〜2、下げた箇条、3
            Assert.Equal(TextMarkerStyle.Decimal, lists[0].MarkerStyle);
            Assert.Equal(1, lists[0].StartIndex);
            Assert.Equal(2, lists[0].ListItems.Count);
            // コピーした文字にも、番号が残る
            Assert.Equal("1.\t外部連携の切り替え: とても長い説明が続いて、折り返す", TextOf(lists[0].ListItems.FirstListItem.Blocks.FirstBlock));
            Assert.Equal(TextMarkerStyle.Disc, lists[1].MarkerStyle);
            Assert.True(lists[1].Margin.Left > lists[0].Margin.Left); // 下げた箇条は右にずらす
            Assert.Equal(3, lists[2].StartIndex); // 間に箇条が入っても、番号は元の数字のまま
        });
    }

    [Fact]
    public void Markdownの本文は_選択とコピーができる読み取り専用の欄になる()
    {
        UiThread.Run(() =>
        {
            var box = Build("**太字**のある返答");
            Assert.True(box.IsReadOnly);
            Assert.False(box.IsReadOnlyCaretVisible);
            var headers = box.ContextMenu.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToList();
            Assert.Equal(new[] { "コピー", "全文をコピー" }, headers);

            // 記号のない本文は、今までどおりの読み取り専用のTextBox
            var plain = Assert.IsType<TextBox>(MarkdownBlockBuilder.BuildElement("ただの文"));
            Assert.True(plain.IsReadOnly);
        });
    }

    [Fact]
    public void 短い返答は中身の幅に縮み_1行に収まる()
    {
        UiThread.Run(() =>
        {
            MarkdownTextBox Create()
            {
                var b = Build("**はい**、了解です。`git` を使います");
                b.FontSize = 14;
                b.FontFamily = new FontFamily("Yu Gothic UI");
                return b;
            }

            // 比べる相手: 幅を固定して、十分に広い所で並べたときの高さ(1行)
            var wide = Create();
            wide.Width = 2000;
            wide.Measure(new Size(2000, double.PositiveInfinity));

            var box = Create();
            box.Measure(new Size(900, double.PositiveInfinity));

            Assert.True(box.DesiredSize.Width < 400, $"幅 {box.DesiredSize.Width}");
            Assert.Equal(wide.DesiredSize.Height, box.DesiredSize.Height); // 縮めても折り返していない
        });
    }

    [Fact]
    public void 長い返答は_使える幅いっぱいで折り返す()
    {
        UiThread.Run(() =>
        {
            var box = Build("**長い**" + string.Concat(Enumerable.Repeat("とても長い文が続きます。", 30)));
            box.Measure(new Size(400, double.PositiveInfinity));
            Assert.Equal(400, box.DesiredSize.Width);
        });
    }

    [Fact]
    public void 本文の上のホイールは_外側へ渡す()
    {
        UiThread.Run(() =>
        {
            var box = Build("**太字**のある返答");
            var outer = new Border { Child = box };
            var received = 0;
            outer.MouseWheel += (_, _) => received++;

            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            box.RaiseEvent(args);

            Assert.True(args.Handled);
            Assert.Equal(1, received);
        });
    }

    [Fact]
    public void 箇条書きと番号つきの返答を_画像に描ける()
    {
        var png = Path.Combine(AppContext.BaseDirectory, "screens", "markdown_list.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        UiThread.Run(() =>
        {
            var size = new Size(560, 260);
            var root = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = MarkdownBlockBuilder.BuildElement(
                    "## 残り\n" +
                    "1. **外部連携の切り替え**: 今は「全部」にすると、Gmail・カレンダー・Driveもまとめて使える状態になります。これを別々にON/OFFできるようにする\n" +
                    "2. **承認ボタン**: 短い\n" +
                    "   - 下げた箇条で、こちらも長めに書いて折り返すかを確かめるための文です\n" +
                    "- 普通の箇条\n\n" +
                    "```\ngit --version\n```"),
            };
            root.Child.SetValue(TextElement.FontSizeProperty, 14.0);
            root.Child.SetValue(TextElement.FontFamilyProperty, new FontFamily("Yu Gothic UI"));
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(png);
            encoder.Save(stream);
        });
        Assert.True(new FileInfo(png).Length > 5_000);
    }

    [Fact]
    public void ダークテーマで表を描くと_画像に描ける()
    {
        var png = Path.Combine(AppContext.BaseDirectory, "screens", "markdown_table_dark.png");
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        try
        {
            UiThread.Run(() =>
            {
                ThemeCatalog.Apply("dark-forest");
                var size = new Size(520, 220);
                var root = new Border
                {
                    Background = (Brush)Application.Current.Resources["PanelBackgroundBrush"],
                    Padding = new Thickness(12),
                    Child = MarkdownBlockBuilder.BuildElement(SampleTable),
                };
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();

                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(png);
                encoder.Save(stream);
            });
            Assert.True(new FileInfo(png).Length > 5_000);
        }
        finally
        {
            UiThread.Run(() => ThemeCatalog.Apply(ThemeCatalog.DefaultThemeId));
        }
    }

    [Theory]
    [InlineData("dark-forest")]
    [InlineData("dark-slate")]
    [InlineData("office-dark")]
    public void ダークテーマの設定画面と入力欄で_文字とカーソルがテーマの文字色になる(string themeId)
    {
        var settings = Services.SettingsLoader.Load(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        try
        {
            UiThread.Run(() =>
            {
                ThemeCatalog.Apply(themeId);
                var primary = ((SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"]).Color;

                var harness = new HarnessWindow(settings.Claude, _ => { });
                var note = (TextBox)harness.FindName("AllowedCommandsTextBox");
                Assert.Equal(primary, ((SolidColorBrush)note.Foreground).Color);
                Assert.Equal(primary, ((SolidColorBrush)note.CaretBrush).Color);

                // クローディの設定画面のチェックボックス(道具・外部連携)も、テーマの文字色になる
                var claudeHarness = new HarnessWindow(settings.Claude, _ => { });
                claudeHarness.ShowConnectors(new[] { "claude.ai Gmail" });
                var boxes = new[] { "ReadOnlyToolsCheckPanel", "ConnectorsCheckPanel" }
                    .SelectMany(n => ((WrapPanel)claudeHarness.FindName(n)).Children.OfType<CheckBox>()).ToList();
                Assert.NotEmpty(boxes);
                Assert.All(boxes, b => Assert.Equal(primary, ((SolidColorBrush)b.Foreground).Color));

                var main = new MainWindow();
                var input = (TextBox)main.FindName("Input");
                Assert.Equal(primary, ((SolidColorBrush)input.CaretBrush).Color);
                main.Content = null;
            });
        }
        finally
        {
            UiThread.Run(() => ThemeCatalog.Apply(ThemeCatalog.DefaultThemeId));
        }

        // 目で確かめるため、ダークの設定画面を画像に残す
        if (themeId == "dark-forest")
        {
            try
            {
                // テーマの切り替えと描画を1回の画面スレッド処理で行う(並列の試験がテーマを戻しても混ざらないように)
                var png = MainWindowRenderTests.RenderWindow(
                    () =>
                    {
                        ThemeCatalog.Apply(themeId);
                        return new HarnessWindow(settings.Agy);
                    },
                    new Size(640, 560), "harness_window_gemini_dark.png");
                Assert.True(new FileInfo(png).Length > 10_000);
            }
            finally
            {
                UiThread.Run(() => ThemeCatalog.Apply(ThemeCatalog.DefaultThemeId));
            }
        }
    }
}
