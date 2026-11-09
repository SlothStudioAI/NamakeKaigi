using System.Windows;
using System.Windows.Documents;
using NamakeKaigi.Services;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>吹き出しの「####」以降の見出しと、「>」の引用の描画(WBS 11.24)の試験。</summary>
public class MarkdownHeadingAndQuoteTests
{
    /// <summary>Markdownの本文を組み立てる(MarkdownTextBox になる)。</summary>
    private static MarkdownTextBox Build(string markdown) => (MarkdownTextBox)MarkdownBlockBuilder.BuildElement(markdown);

    /// <summary>文書の部品の文字を取り出す。</summary>
    private static string TextOf(TextElement element) => new TextRange(element.ContentStart, element.ContentEnd).Text;

    [Theory]
    [InlineData("# 大見出し", 1, "大見出し")]
    [InlineData("### 小見出し", 3, "小見出し")]
    [InlineData("#### 4つ", 4, "4つ")]
    [InlineData("###### 6つ", 6, "6つ")]
    public void 見出しの行を_深さと中身に分ける(string line, int level, string content)
    {
        Assert.True(MarkdownSyntax.TryParseHeading(line, out var actualLevel, out var actualContent));
        Assert.Equal(level, actualLevel);
        Assert.Equal(content, actualContent);
    }

    [Theory]
    [InlineData("####### 7つは見出しにしない")]
    [InlineData("#空白なし")]
    [InlineData("#")]
    [InlineData("C# の話")]
    public void 見出しでない行は_そのまま文として扱う(string line)
    {
        Assert.False(MarkdownSyntax.TryParseHeading(line, out _, out _));
    }

    [Fact]
    public void 四つ以上の見出しも記号を残さず_見出しの段落になる()
    {
        UiThread.Run(() =>
        {
            Assert.True(MarkdownSyntax.ContainsMarkdownSyntax("#### 結論"));
            var paragraphs = Build("### 三つ\n#### 四つ\n##### 五つ\n本文").Document.Blocks.OfType<Paragraph>().ToList();

            Assert.Equal(["三つ", "四つ", "五つ", "本文"], paragraphs.Select(TextOf));
            Assert.Equal(FontWeights.Bold, paragraphs[1].FontWeight);
            Assert.True(paragraphs[1].FontSize < paragraphs[0].FontSize); // ####は###より小さい
            Assert.Equal(paragraphs[1].FontSize, paragraphs[2].FontSize); // ####以降は同じ大きさ
        });
    }

    [Theory]
    [InlineData("> 引用", 0, "引用")]
    [InlineData(">詰めた引用", 0, "詰めた引用")]
    [InlineData("> > 重ねた引用", 0, "重ねた引用")]
    [InlineData("   > 箇条書きの下の引用", 1, "箇条書きの下の引用")]
    [InlineData(">", 0, "")]
    public void 引用の行を_字下げの深さと中身に分ける(string line, int level, string content)
    {
        Assert.True(MarkdownSyntax.TryParseQuote(line, out var actualLevel, out var actualContent));
        Assert.Equal(level, actualLevel);
        Assert.Equal(content, actualContent);
    }

    [Fact]
    public void 続く引用の行は1つの段落にまとまり_左に線がつき_記号は残らない()
    {
        UiThread.Run(() =>
        {
            Assert.True(MarkdownSyntax.ContainsMarkdownSyntax("> 注意"));
            var box = Build("前の文\n> 一行目 **太字**\n> 二行目\n後の文");
            var paragraphs = box.Document.Blocks.OfType<Paragraph>().ToList();

            Assert.Equal(3, paragraphs.Count);
            var quote = paragraphs[1];
            Assert.Equal("一行目 太字\r\n二行目", TextOf(quote));
            Assert.Equal(3, quote.BorderThickness.Left);
            Assert.Contains(quote.Inlines, i => i is Bold); // 引用の中の太字も効く
            Assert.Equal("後の文", TextOf(paragraphs[2]));
        });
    }

    [Fact]
    public void 箇条書きの下に字下げされた引用は_箇条書きと同じ深さだけ右にずれる()
    {
        UiThread.Run(() =>
        {
            var box = Build("- 項目\n  > 項目への補足\n- 次の項目");
            var blocks = box.Document.Blocks.ToList();

            Assert.Equal(3, blocks.Count); // 箇条書き、引用、箇条書き
            var list = Assert.IsType<List>(blocks[0]);
            var quote = Assert.IsType<Paragraph>(blocks[1]);
            Assert.Equal("項目への補足", TextOf(quote));
            Assert.True(quote.Margin.Left > list.Margin.Left);
        });
    }

    [Theory]
    [InlineData("項目への補足の引用です**x**")]
    [InlineData("   > 項目への補足の引用")]
    [InlineData("   - 項目への補足の引用です")]
    public void 一行の返答は_吹き出しを縮めても最後の1文字が折り返さない(string markdown)
    {
        // RichTextBoxが文書の左右の余白を5pxずつに戻すため、その分を幅に足さないと最後の1文字が次の行に落ちていた
        UiThread.Run(() =>
        {
            var box = Build(markdown);
            var window = new Window
            {
                FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Segoe UI, Meiryo"),
                FontSize = 14,
                Content = new System.Windows.Controls.Border { HorizontalAlignment = HorizontalAlignment.Left, Child = box },
            };
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(400, 200));
            root.Arrange(new Rect(new Size(400, 200)));
            root.UpdateLayout();

            var first = box.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward);
            var last = box.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward).GetCharacterRect(LogicalDirection.Backward);
            Assert.True(box.ActualWidth < 400, $"幅 {box.ActualWidth}"); // 中身の幅まで縮んでいる
            Assert.Equal(first.Y, last.Y, 1); // 最初と最後の文字が同じ行にある
        });
    }

    [Fact]
    public void 見出しと引用の見本を_画像に残す()
    {
        // 目で確かめるため、ジェミリーの返答に近い見本を画像に残す
        const string sample =
            "### 三つの見出し\n" +
            "#### 四つの見出し\n" +
            "本文の文です。\n" +
            "> 引用の一行目 **太字**\n" +
            "> 引用の二行目\n" +
            "\n" +
            "1. **項目**: 説明\n" +
            "   > 項目への補足の引用\n" +
            "2. 次の項目";
        var png = MainWindowRenderTests.RenderWindow(
            () => new Window { Content = new System.Windows.Controls.Border { Padding = new Thickness(12), Child = Build(sample) } },
            new Size(480, 320), "markdown_heading_quote.png");
        Assert.True(new System.IO.FileInfo(png).Length > 5_000);
    }
}
