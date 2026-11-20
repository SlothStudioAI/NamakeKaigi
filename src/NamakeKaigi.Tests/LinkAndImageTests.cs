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
    [InlineData("file:///C:/Windows", LinkKind.Folder)]
    [InlineData("file://server/share/a.txt", LinkKind.None)]
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

    [Theory]
    [InlineData("https://user@example.com:8443/path~1/a%20b?k=1&x=!$'(*+,;=)#frag")]
    [InlineData("http://[::1]:8080/api?q=a%2Fb#top")]
    [InlineData("https://example.com/?a=1&b=~*+$@")]
    public void 特殊記号やポート番号やIPv6を含むURLを検出する(string url)
    {
        var text = $"接続先 {url} です";
        var link = Assert.Single(LinkDetector.FindLinks(text));

        Assert.Equal(url, link.Target);
        Assert.Equal(LinkKind.Web, link.Kind);
        Assert.Equal(LinkKind.Web, LinkDetector.Classify(url));
        Assert.Equal(url, LinkDetector.ResolveTarget(url));
    }

    [Theory]
    [InlineData("(https://example.com/a(b).).", "https://example.com/a(b)")]
    [InlineData("https://example.com/path.,;:!?'\"", "https://example.com/path")]
    [InlineData("((https://example.com/a(b(c)),;))", "https://example.com/a(b(c))")]
    [InlineData("\"https://example.com/q?a=1!\"", "https://example.com/q?a=1")]
    [InlineData("「https://example.com/path」", "https://example.com/path")]
    [InlineData("（https://example.com/path）", "https://example.com/path")]
    [InlineData("https://example.com/path、次の文", "https://example.com/path")]
    [InlineData("https://example.com/path。次の文", "https://example.com/path")]
    public void URLの末尾に連続する句読点や括弧と全角記号を外す(string input, string expected)
    {
        var link = Assert.Single(LinkDetector.FindLinks(input));
        Assert.Equal(expected, link.Target);
        Assert.Equal(LinkKind.Web, link.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n  ")]
    [InlineData("https://")]
    [InlineData("http://")]
    [InlineData("https://.")]
    [InlineData("https://!?")]
    [InlineData("http://a")]
    public void 無効なURLや空の入力は_FindLinksで誤検出しない(string? input)
    {
        Assert.Empty(LinkDetector.FindLinks(input!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    public void 無効なターゲットは_ClassifyがNoneで_ResolveTargetがnullになる(string? target)
    {
        Assert.Equal(LinkKind.None, LinkDetector.Classify(target!));
        Assert.Null(LinkDetector.ResolveTarget(target!));
    }

    [Fact]
    public void パス直前の英数字は除外し_日本語やコロンや全角括弧の直後は検出する()
    {
        Assert.Empty(LinkDetector.FindLinks($"abc{_text}"));
        Assert.Empty(LinkDetector.FindLinks($"123{_text}"));

        foreach (var text in new[] { $"場所:{_text}", $"「{_text}」", $"場所{_text}" })
        {
            var link = Assert.Single(LinkDetector.FindLinks(text));
            Assert.Equal(_text, link.Target);
            Assert.Equal(LinkKind.File, link.Kind);
        }
    }

    [Fact]
    public void スラッシュとバックスラッシュが混在する実在パスを検出する()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        var subFile = Path.Combine(sub, "note.txt");
        File.WriteAllText(subFile, "ok");

        var mixed = _dir.Replace('\\', '/') + @"\sub/note.txt";
        var link = Assert.Single(LinkDetector.FindLinks($"参照: {mixed} を確認"));

        Assert.Equal(mixed, link.Target);
        Assert.Equal(LinkKind.File, link.Kind);
        Assert.Equal(LinkKind.File, LinkDetector.Classify(mixed));
        Assert.Equal(mixed, LinkDetector.ResolveTarget(mixed));
    }

    [Fact]
    public void 実在フォルダ配下の存在しないファイルは_親フォルダに縮めず空になる()
    {
        // _folderは空白を含むためPathPatternが途中で止まる。空白なしフォルダでも親フォルダへ縮めないことを検証する
        var sub = Path.Combine(_dir, "existing_sub");
        Directory.CreateDirectory(sub);

        Assert.Empty(LinkDetector.FindLinks(Path.Combine(sub, "ない.txt")));
        Assert.Empty(LinkDetector.FindLinks(Path.Combine(_folder, "ない.txt")));
    }

    [Theory]
    [InlineData("...を確認")]
    [InlineData(" を確認")]
    [InlineData("を確認")]
    public void 実在パスの後ろにドットや空白や日本語が続いても_実在部分のみ切り出す(string suffix)
    {
        var text = _text + suffix;
        var link = Assert.Single(LinkDetector.FindLinks(text));

        Assert.Equal(_text, link.Target);
        Assert.Equal(_text.Length, link.Length);
        Assert.Equal(LinkKind.File, link.Kind);
    }

    [Fact]
    public void URLクエリ内のドライブレター風文字列は_URLとパスで重複検出しない()
    {
        var slashPath = _text.Replace('\\', '/');
        foreach (var url in new[] { "https://example.com/?p=C:/Windows", $"https://example.com/?p={slashPath}" })
        {
            var link = Assert.Single(LinkDetector.FindLinks(url));
            Assert.Equal(url, link.Target);
            Assert.Equal(LinkKind.Web, link.Kind);
        }
    }

    [Fact]
    public void 空白や特殊記号を含む実在フォルダとファイルを判定できる()
    {
        var specialFolder = Path.Combine(_dir, "フォルダ (1) [v2] -_+~@#$%&=");
        Directory.CreateDirectory(specialFolder);
        var specialFile = Path.Combine(specialFolder, "ファイル (1) [v2] -_+~@#$%&=.txt");
        File.WriteAllText(specialFile, "ok");

        Assert.Equal(LinkKind.Folder, LinkDetector.Classify(specialFolder));
        Assert.Equal(LinkKind.File, LinkDetector.Classify(specialFile));
        Assert.Equal(specialFolder, LinkDetector.ResolveTarget(specialFolder));
        Assert.Equal(specialFile, LinkDetector.ResolveTarget(specialFile));
        Assert.Equal(specialFile, LinkDetector.ResolveTarget($" \"{specialFile}\" "));
    }

    [Fact]
    public void ResolveTargetは_前後の空白や引用符や山括弧を外し_空ならnullを返す()
    {
        Assert.Equal(_text, LinkDetector.ResolveTarget($"  \"'{_text}'\"  "));
        Assert.Equal(_text, LinkDetector.ResolveTarget($" <\"{_text}\"> "));
        Assert.Equal(_folder, LinkDetector.ResolveTarget($" '<{_folder}>' "));
        Assert.Equal("https://example.com/a", LinkDetector.ResolveTarget("  <https://example.com/a>  "));

        foreach (var empty in new[] { " '' ", "\"\"", "<>", "'<>\" ", "", "   " })
        {
            Assert.Null(LinkDetector.ResolveTarget(empty));
        }
    }

    [Fact]
    public void ExecutableExtensionsの全拡張子と大文字小文字混在がRevealFileになる()
    {
        var extensions = new[]
        {
            ".bat", ".cmd", ".com", ".msi", ".msp", ".ps1", ".psm1", ".psd1", ".ps1xml",
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".scr", ".lnk", ".reg", ".hta",
            ".jar", ".cpl", ".dll", ".pif", ".msc", ".url", ".application", ".appref-ms",
            ".gadget", ".inf", ".pyz", ".pl", ".php", ".vb", ".mjs", ".cjs", ".chm",
            ".docm", ".xlsm", ".pptm",
            ".Ps1", ".DOCM", ".Xlsm", ".AppRef-Ms", ".Bat", ".Ps1Xml"
        };

        for (var i = 0; i < extensions.Length; i++)
        {
            var path = Path.Combine(_dir, $"exec_{i}{extensions[i]}");
            File.WriteAllText(path, "dummy");
            Assert.Equal(LinkKind.RevealFile, LinkDetector.Classify(path));
        }
    }

    [Theory]
    [InlineData(". . .. ")]
    [InlineData(" . . ")]
    [InlineData("...")]
    public void 実行ファイル名末尾に複数のドットと空白が混在してもRevealFileになる(string tail)
    {
        Assert.Equal(LinkKind.RevealFile, LinkDetector.Classify(_exe + tail));
    }

    [Theory]
    [InlineData("README")]
    [InlineData(".gitignore")]
    [InlineData("report.docx")]
    [InlineData("sheet.xlsx")]
    [InlineData("slides.pptx")]
    public void 拡張子なしやドット始まりやマクロなしOfficeファイルはFileになる(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, "data");
        Assert.Equal(LinkKind.File, LinkDetector.Classify(path));
    }

    [Theory]
    [InlineData("a.gif", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.JPEG", true)]
    [InlineData("a.webp", false)]
    [InlineData("a.svg", false)]
    [InlineData("a.ico", false)]
    [InlineData("a.tiff", false)]
    [InlineData("noext", false)]
    [InlineData(".gitignore", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a.png\0", false)]
    [InlineData("bad\0path", false)]
    [InlineData("invalid<>|path", false)]
    [InlineData(null, false)]
    public void IsImageFileは_対象画像拡張子のみtrueを返し例外を投げない(string? path, bool expected)
    {
        Assert.Equal(expected, LinkDetector.IsImageFile(path!));
    }

    [Fact]
    public void ラベルが空のMarkdownリンクは_ターゲット自身を表示する()
    {
        UiThread.Run(() =>
        {
            var webBox = Build("[](https://example.com)");
            var webLink = Assert.Single(Descendants<Hyperlink>(webBox));
            Assert.Equal("https://example.com", webLink.ToolTip);
            Assert.Equal("https://example.com", new TextRange(webLink.ContentStart, webLink.ContentEnd).Text);

            var fileBox = Build($"[]({_text})");
            var fileLink = Assert.Single(Descendants<Hyperlink>(fileBox));
            Assert.Equal(_text, fileLink.ToolTip);
            Assert.Equal(_text, new TextRange(fileLink.ContentStart, fileLink.ContentEnd).Text);
        });
    }

    [Fact]
    public void ラベルが空でリンク先が無効なMarkdownリンクは_リンクを作らず落ちない()
    {
        UiThread.Run(() =>
        {
            const string markdown = "[](C:/ない/ファイル.txt)";
            Assert.True(MarkdownSyntax.ContainsMarkdownSyntax(markdown));

            var box = Build(markdown);
            Assert.Empty(Descendants<Hyperlink>(box));
            Assert.Empty(Descendants<InlineUIContainer>(box));
        });
    }

    [Fact]
    public void 画像記法でも_非画像ファイルやWebURLは通常のリンクにフォールバックする()
    {
        UiThread.Run(() =>
        {
            var textBox = Build($"![テキスト]({_text})");
            Assert.Empty(Descendants<InlineUIContainer>(textBox));
            var textLink = Assert.Single(Descendants<Hyperlink>(textBox));
            Assert.Equal(_text, textLink.ToolTip);
            Assert.Equal("テキスト", new TextRange(textLink.ContentStart, textLink.ContentEnd).Text);

            var webBox = Build("![Web画像](https://example.com/a.png)");
            Assert.Empty(Descendants<InlineUIContainer>(webBox));
            var webLink = Assert.Single(Descendants<Hyperlink>(webBox));
            Assert.Equal("https://example.com/a.png", webLink.ToolTip);
            Assert.Equal("Web画像", new TextRange(webLink.ContentStart, webLink.ContentEnd).Text);
        });
    }

    [Fact]
    public void 同じ行に複数のMarkdownリンクとコード内パスと太字と下線が混在しても分解できる()
    {
        UiThread.Run(() =>
        {
            var line = $"**強調** と `{_text}` と [案内](https://example.com/guide) と <u>[リンク](https://example.com)</u> と []({_folder})";
            var box = Build(line);

            var bold = Assert.Single(Descendants<Bold>(box));
            Assert.Equal("強調", new TextRange(bold.ContentStart, bold.ContentEnd).Text);

            var underline = Assert.Single(Descendants<Underline>(box));
            var underlinedLink = Assert.Single(underline.Inlines.OfType<Hyperlink>());
            Assert.Equal("https://example.com", underlinedLink.ToolTip);
            Assert.Equal("リンク", new TextRange(underlinedLink.ContentStart, underlinedLink.ContentEnd).Text);

            var links = Descendants<Hyperlink>(box).ToList();
            Assert.Equal(4, links.Count);
            Assert.Equal(_text, links[0].ToolTip);
            Assert.Equal(" " + _text + " ", new TextRange(links[0].ContentStart, links[0].ContentEnd).Text);
            Assert.Equal("https://example.com/guide", links[1].ToolTip);
            Assert.Equal("案内", new TextRange(links[1].ContentStart, links[1].ContentEnd).Text);
            Assert.Equal("https://example.com", links[2].ToolTip);
            Assert.Equal("リンク", new TextRange(links[2].ContentStart, links[2].ContentEnd).Text);
            Assert.Equal(_folder, links[3].ToolTip);
            Assert.Equal(_folder, new TextRange(links[3].ContentStart, links[3].ContentEnd).Text);
        });
    }

    [Theory]
    [InlineData("HTTPS://example.com/path")]
    [InlineData("Http://example.com")]
    public void 大文字スキームのURLは_ClassifyとResolveTargetではWebになり_FindLinksでは検出しない(string url)
    {
        Assert.Equal(LinkKind.Web, LinkDetector.Classify(url));
        Assert.Equal(url, LinkDetector.ResolveTarget(url));
        Assert.Empty(LinkDetector.FindLinks($"参照: {url}"));
    }

    [Fact]
    public void 壊れた画像のMarkdown画像記法は_リンクにフォールバックし_落ちない()
    {
        UiThread.Run(() =>
        {
            var broken = Path.Combine(_dir, "壊れ記法.png");
            File.WriteAllText(broken, "これは画像ではない");

            var box = Build($"![壊れた画像]({broken})");
            Assert.Empty(Descendants<InlineUIContainer>(box));
            var link = Assert.Single(Descendants<Hyperlink>(box));
            Assert.Equal(broken, link.ToolTip);
            Assert.Equal("壊れた画像", new TextRange(link.ContentStart, link.ContentEnd).Text);
        });
    }

    [Fact]
    public void 名前がexeで終わる実在フォルダはFolderになり_exeを含む別拡張子のファイルはFileになる()
    {
        var exeNamedFolder = Path.Combine(_dir, "tool.exe");
        Directory.CreateDirectory(exeNamedFolder);
        var exeInNameFile = Path.Combine(_dir, "backup.exe.txt");
        File.WriteAllText(exeInNameFile, "memo");

        Assert.Equal(LinkKind.Folder, LinkDetector.Classify(exeNamedFolder));
        Assert.Equal(LinkKind.File, LinkDetector.Classify(exeInNameFile));
    }

    [Fact]
    public void fileスキームと行番号アンカー付きのパスも_ローカルパスとして解決できる()
    {
        var uriPath = new Uri(_text).AbsoluteUri; // file:///...
        Assert.Equal(LinkKind.File, LinkDetector.Classify(uriPath));
        Assert.Equal(_text, LinkDetector.ResolveTarget(uriPath));
        Assert.Equal(_text, LinkDetector.ResolveTarget($"{uriPath}#L10-L20"));
        Assert.Equal(_text, LinkDetector.ResolveTarget($"{_text}#L15"));
        Assert.Equal(_folder, LinkDetector.ResolveTarget(new Uri(_folder).AbsoluteUri));
    }
}
