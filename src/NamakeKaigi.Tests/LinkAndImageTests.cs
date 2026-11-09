using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NamakeKaigi.Services;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>返答の中のURL・パスのリンクと、画像の表示(WBS 11.31・11.32)の試験。</summary>
public sealed class LinkAndImageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NamakeKaigiLinkTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _png;
    private readonly string _text;
    private readonly string _exe;
    private readonly string _folder;

    /// <summary>試験用のフォルダに、画像・テキスト・実行ファイル・日本語の名前のフォルダを作る。</summary>
    public LinkAndImageTests()
    {
        _folder = Path.Combine(_dir, "資料 フォルダ");
        Directory.CreateDirectory(_folder);
        _png = Path.Combine(_dir, "写真.png");
        _text = Path.Combine(_dir, "memo.txt");
        _exe = Path.Combine(_dir, "run.exe");
        File.WriteAllText(_text, "メモ");
        File.WriteAllText(_exe, "dummy");
        UiThread.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(
                4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16)));
            using var stream = File.Create(_png);
            encoder.Save(stream);
        });
    }

    /// <summary>試験用のフォルダを消す。</summary>
    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* 読み込み中の画像が残っても、試験の結果には関係しない */ }
    }

    private static MarkdownTextBox Build(string markdown) => (MarkdownTextBox)MarkdownBlockBuilder.BuildElement(markdown);

    private static IEnumerable<T> Descendants<T>(MarkdownTextBox box) where T : TextElement
    {
        return Walk(box.Document.Blocks).OfType<T>();

        static IEnumerable<TextElement> Walk(IEnumerable<TextElement> elements)
        {
            foreach (var element in elements)
            {
                yield return element;
                IEnumerable<TextElement> children = element switch
                {
                    Paragraph p => p.Inlines,
                    Span s => s.Inlines,
                    Section s => s.Blocks,
                    List l => l.ListItems,
                    ListItem li => li.Blocks,
                    _ => [],
                };
                foreach (var child in Walk(children)) yield return child;
            }
        }
    }

    [Theory]
    [InlineData("https://example.com/a?b=1", LinkKind.Web)]
    [InlineData("http://example.com", LinkKind.Web)]
    [InlineData("javascript:alert(1)", LinkKind.None)]
    [InlineData("file:///C:/Windows", LinkKind.None)]
    [InlineData("相対/パス.png", LinkKind.None)]
    [InlineData("", LinkKind.None)]
    public void 開く先の種類を分ける_URLとそれ以外(string target, LinkKind expected)
    {
        Assert.Equal(expected, LinkDetector.Classify(target));
    }

    [Fact]
    public void 実在するパスは_種類を分け_実行ファイルは場所を示すだけにする()
    {
        Assert.Equal(LinkKind.File, LinkDetector.Classify(_png));
        Assert.Equal(LinkKind.File, LinkDetector.Classify(_text));
        Assert.Equal(LinkKind.Folder, LinkDetector.Classify(_folder));
        Assert.Equal(LinkKind.RevealFile, LinkDetector.Classify(_exe));
        Assert.Equal(LinkKind.None, LinkDetector.Classify(Path.Combine(_dir, "ない.png")));
    }

    [Fact]
    public void スクリプト類も_クリックでは実行せず場所を示すだけにする()
    {
        foreach (var name in new[] { "tool.py", "tool.PYW", "tool.sh", "tool.rb" })
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "print(1)");
            Assert.Equal(LinkKind.RevealFile, LinkDetector.Classify(path));
        }
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(" ")]
    public void 実行ファイル名の末尾に_点や空白を付けても_実行せず場所を示すだけにする(string tail)
    {
        // Windowsは末尾の「.」と空白を無視して開くため、拡張子の判定をすり抜けられてはいけない
        Assert.Equal(LinkKind.RevealFile, LinkDetector.Classify(_exe + tail));
    }

    [Theory]
    [InlineData(@"\\evil.example.com\share\a.txt")]
    [InlineData("//evil.example.com/share/a.txt")]
    [InlineData(@"\\?\C:\Windows\notepad.exe")]
    [InlineData(@"\\.\pipe\x")]
    public void ネットワークの共有とデバイスのパスは_調べずにリンクにしない(string path)
    {
        Assert.Equal(LinkKind.None, LinkDetector.Classify(path));
        Assert.Null(LinkDetector.ResolveTarget(path));
    }

    [Fact]
    public void 文の中のURLとパスを見つけ_句読点と後ろの日本語は含めない()
    {
        var text = $"詳しくは https://example.com/page. を見て。{_png}を開いてください。";
        var links = LinkDetector.FindLinks(text);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/page", links[0].Target);
        Assert.Equal(LinkKind.Web, links[0].Kind);
        Assert.Equal(_png, links[1].Target); // 「を開いてください」までは含めない
        Assert.Equal(text.IndexOf(_png, StringComparison.Ordinal), links[1].Start);
    }

    [Fact]
    public void 存在しないパスと_ドライブ直下だけは_リンクにしない()
    {
        Assert.Empty(LinkDetector.FindLinks($"{Path.Combine(_dir, "ない.png")} は消えた"));
        Assert.Empty(LinkDetector.FindLinks("C:\\ のことです"));
    }

    [Fact]
    public void URLの終わりの閉じ括弧は_対応がなければ外す()
    {
        Assert.Equal("https://example.com/a", LinkDetector.FindLinks("(https://example.com/a)")[0].Target);
        Assert.Equal("https://example.com/a(b)", LinkDetector.FindLinks("https://example.com/a(b)")[0].Target);
    }

    [Theory]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.exe", false)]
    public void 画像として見せる拡張子を判定する(string path, bool expected)
    {
        Assert.Equal(expected, LinkDetector.IsImageFile(path));
    }

    [Fact]
    public void URLの本文は_リンクになり_ふつうの文字のままではない()
    {
        UiThread.Run(() =>
        {
            Assert.True(MarkdownSyntax.ContainsMarkdownSyntax("見て https://example.com"));
            var link = Assert.Single(Descendants<Hyperlink>(Build("見て https://example.com です")));
            Assert.Equal("https://example.com", link.ToolTip);
            Assert.Equal("https://example.com", new TextRange(link.ContentStart, link.ContentEnd).Text);
        });
    }

    [Fact]
    public void 画像のパスは_リンクになり_画像が吹き出しの中に出る()
    {
        UiThread.Run(() =>
        {
            var box = Build($"保存しました: {_png}");

            Assert.Single(Descendants<Hyperlink>(box));
            var container = Assert.Single(Descendants<InlineUIContainer>(box));
            var image = Assert.IsType<Image>(container.Child);
            Assert.NotNull(image.Source);
            Assert.Equal(4, image.Width); // 小さな画像は引き伸ばさない
        });
    }

    [Fact]
    public void 画像は_吹き出しの幅より広くならず_吹き出しは画像より狭くならない()
    {
        UiThread.Run(() =>
        {
            var big = Path.Combine(_dir, "大きい.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(
                1000, 500, 96, 96, PixelFormats.Bgra32, null, new byte[1000 * 500 * 4], 1000 * 4)));
            using (var stream = File.Create(big)) encoder.Save(stream);

            var box = Build($"`{big}`");
            var image = Assert.IsType<Image>(Assert.Single(Descendants<InlineUIContainer>(box)).Child);
            Assert.Equal(360, image.Width);

            box.Measure(new Size(2000, double.PositiveInfinity));
            Assert.True(box.DesiredSize.Width >= 360);
        });
    }

    [Fact]
    public void コードで囲んだパスと_Markdownのリンク_画像の記法も開ける()
    {
        UiThread.Run(() =>
        {
            Assert.Single(Descendants<Hyperlink>(Build($"`{_text}`")));
            Assert.Empty(Descendants<InlineUIContainer>(Build($"`{_text}`")));

            var named = Assert.Single(Descendants<Hyperlink>(Build($"[メモを開く]({_text})")));
            Assert.Equal("メモを開く", new TextRange(named.ContentStart, named.ContentEnd).Text);

            var markdownImage = Build($"![写真]({_png})");
            Assert.Single(Descendants<InlineUIContainer>(markdownImage));
            Assert.Empty(Descendants<Hyperlink>(markdownImage)); // 画像の記法は、画像だけ(リンクの文字は出さない)
        });
    }

    [Fact]
    public void 開けないリンク先は_文字だけを残す()
    {
        UiThread.Run(() =>
        {
            var box = Build("[怪しい](javascript:alert(1)) と ![絵](C:/ない/なにも.png)");
            Assert.Empty(Descendants<Hyperlink>(box));
            Assert.Empty(Descendants<InlineUIContainer>(box));
            var text = new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
            Assert.Contains("怪しい", text);
            Assert.Contains("絵", text);
        });
    }

    [Fact]
    public void 壊れた画像ファイルは_リンクだけ残り_落ちない()
    {
        UiThread.Run(() =>
        {
            var broken = Path.Combine(_dir, "壊れ.png");
            File.WriteAllText(broken, "これは画像ではない");

            var box = Build($"見て {broken}");
            Assert.Single(Descendants<Hyperlink>(box));
            Assert.Empty(Descendants<InlineUIContainer>(box));
        });
    }

    [Fact]
    public void 読み込んだ画像のファイルは_離される()
    {
        UiThread.Run(() => Build($"{_png}"));
        File.Delete(_png); // 掴んだままなら、ここで例外になる
        Assert.False(File.Exists(_png));
    }

    [Fact]
    public void リンクの本文は_RichTextBoxでクリックできる設定になっている()
    {
        UiThread.Run(() => Assert.True(Build("https://example.com").IsDocumentEnabled));
    }
}
