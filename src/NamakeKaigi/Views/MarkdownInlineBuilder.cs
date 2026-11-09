using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NamakeKaigi.Services;
using static NamakeKaigi.Views.MarkdownTheme;

namespace NamakeKaigi.Views;

/// <summary>
/// 1行の文の中(太字・コード・下線・リンク・画像)を、文書のインライン要素に組み立てる。
/// 行をまたぐブロック(見出し・箇条書き・表など)の組み立ては MarkdownFormatter が行う。
/// </summary>
internal static class MarkdownInlineBuilder
{
    // 吹き出しの中に見せる画像の、幅と高さの上限(px)。これより大きい画像は縮めて見せ、クリックで元の大きさを開く
    private const double ImageMaxWidth = 360;
    private const double ImageMaxHeight = 300;

    // 画像を読み込むときの、横幅の上限(px)。表示の2倍まで(大きな写真でメモリを使いすぎないように)
    private const int ImageDecodeMaxWidth = 720;

    /// <summary>1行の中の **太字** と `コード` と &lt;u&gt;下線&lt;/u&gt; をインライン要素に分解して追加する。</summary>
    internal static void AddInlines(InlineCollection inlines, string line)
    {
        var i = 0;
        while (i < line.Length)
        {
            var nextBold = line.IndexOf("**", i, StringComparison.Ordinal);
            var nextCode = line.IndexOf('`', i);
            var nextUnderline = line.IndexOf("<u>", i, StringComparison.OrdinalIgnoreCase);

            // [文字](リンク先) と ![説明](画像): ほかの記号より前にあるとき
            var markdownLink = MarkdownSyntax.LinkPattern.Match(line, i);
            if (markdownLink.Success &&
                (nextBold < 0 || markdownLink.Index <= nextBold) &&
                (nextCode < 0 || markdownLink.Index <= nextCode) &&
                (nextUnderline < 0 || markdownLink.Index <= nextUnderline))
            {
                if (markdownLink.Index > i) AddPlain(inlines, line[i..markdownLink.Index]);
                AddMarkdownLink(inlines, markdownLink, markdownLink.Index + markdownLink.Length < line.Length);
                i = markdownLink.Index + markdownLink.Length;
                continue;
            }

            if (nextBold < 0 && nextCode < 0 && nextUnderline < 0)
            {
                AddPlain(inlines, line[i..]);
                break;
            }

            // 下線: 太字・コードより前に「<u>」があり、閉じの「</u>」もあるとき
            if (nextUnderline >= 0 && (nextBold < 0 || nextUnderline < nextBold) && (nextCode < 0 || nextUnderline < nextCode))
            {
                var endUnderline = line.IndexOf("</u>", nextUnderline + 3, StringComparison.OrdinalIgnoreCase);
                if (endUnderline > nextUnderline)
                {
                    if (nextUnderline > i) AddPlain(inlines, line[i..nextUnderline]);
                    var underline = new Underline();
                    AddInlines(underline.Inlines, line[(nextUnderline + 3)..endUnderline]);
                    inlines.Add(underline);
                    i = endUnderline + 4;
                    continue;
                }
            }

            if (nextCode >= 0 && (nextBold < 0 || nextCode < nextBold))
            {
                var endCode = line.IndexOf('`', nextCode + 1);
                if (endCode > nextCode)
                {
                    if (nextCode > i) AddPlain(inlines, line[i..nextCode]);
                    var codeText = line[(nextCode + 1)..endCode];
                    var codeRun = new Run(" " + codeText + " ")
                    {
                        FontFamily = MonoFont,
                        Background = InlineCodeBg,
                    };
                    // コードの中身がURL・実在するパスなら、リンクにする(画像ならその下に見せる)
                    if (LinkDetector.ResolveTarget(codeText) is { } codeTarget)
                    {
                        inlines.Add(CreateLink(codeTarget, codeRun));
                        AddImageIfPicture(inlines, codeTarget, endCode + 1 < line.Length);
                    }
                    else
                    {
                        inlines.Add(codeRun);
                    }
                    i = endCode + 1;
                    continue;
                }
            }

            if (nextBold >= 0)
            {
                var endBold = line.IndexOf("**", nextBold + 2, StringComparison.Ordinal);
                if (endBold > nextBold)
                {
                    if (nextBold > i) AddPlain(inlines, line[i..nextBold]);
                    inlines.Add(new Bold(new Run(line[(nextBold + 2)..endBold])));
                    i = endBold + 2;
                    continue;
                }
            }

            AddPlain(inlines, line[i..]);
            break;
        }
    }

    /// <summary>ふつうの文字を足す。中にURL・実在するパスがあれば、そこだけクリックで開くリンクにする(画像なら、その下に見せる)。</summary>
    /// <param name="inlines">足す先</param>
    /// <param name="text">ふつうの文字</param>
    internal static void AddPlain(InlineCollection inlines, string text)
    {
        var position = 0;
        foreach (var link in LinkDetector.FindLinks(text))
        {
            if (link.Start > position) inlines.Add(new Run(text[position..link.Start]));
            inlines.Add(CreateLink(link.Target, new Run(text.Substring(link.Start, link.Length))));
            AddImageIfPicture(inlines, link.Target, link.Start + link.Length < text.Length);
            position = link.Start + link.Length;
        }
        if (position < text.Length) inlines.Add(new Run(text[position..]));
    }

    /// <summary>[文字](リンク先) と ![説明](画像のパス) を足す。開けないリンク先は、文字だけ残す。</summary>
    /// <param name="inlines">足す先</param>
    /// <param name="match">見つけた記法</param>
    /// <param name="hasMore">そのあとに同じ行の文字が続くか</param>
    internal static void AddMarkdownLink(InlineCollection inlines, Match match, bool hasMore)
    {
        var isImage = match.Value.StartsWith('!');
        var label = match.Groups[1].Value;
        var target = LinkDetector.ResolveTarget(match.Groups[2].Value);

        if (target is null)
        {
            inlines.Add(new Run(label)); // 存在しない・開けない先は、文字だけ(説明があればそれを残す)
            return;
        }

        if (isImage && LinkDetector.Classify(target) == LinkKind.File && LinkDetector.IsImageFile(target) && CreateImage(target) is { } image)
        {
            AddImageBlock(inlines, image, hasMore);
            return;
        }

        inlines.Add(CreateLink(target, new Run(label.Length > 0 ? label : target)));
    }

    /// <summary>開く先が画像ファイルなら、リンクの下に画像を見せる。</summary>
    /// <param name="inlines">足す先</param>
    /// <param name="target">開く先</param>
    /// <param name="hasMore">そのあとに同じ行の文字が続くか</param>
    internal static void AddImageIfPicture(InlineCollection inlines, string target, bool hasMore)
    {
        if (LinkDetector.Classify(target) != LinkKind.File || !LinkDetector.IsImageFile(target)) return;
        if (CreateImage(target) is { } image) AddImageBlock(inlines, image, hasMore);
    }

    /// <summary>画像を、独立した行として足す(前後で改行する)。</summary>
    internal static void AddImageBlock(InlineCollection inlines, Image image, bool hasMore)
    {
        inlines.Add(new LineBreak());
        inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Bottom });
        if (hasMore) inlines.Add(new LineBreak());
    }

    /// <summary>クリックで開くリンクを作る(色は、テーマのリンクの色)。</summary>
    /// <param name="target">開く先(URL またはフルパス)</param>
    /// <param name="content">リンクの見た目(文字)</param>
    /// <returns>リンク</returns>
    internal static Hyperlink CreateLink(string target, Inline content)
    {
        var link = new Hyperlink(content)
        {
            Foreground = HeadingAccent,
            ToolTip = target,
            Cursor = Cursors.Hand,
        };
        link.Click += (_, _) => LinkOpener.Open(target);
        return link;
    }

    /// <summary>画像ファイルを読み込み、吹き出しに見せる大きさの Image にする。クリックで元の画像を開く。</summary>
    /// <param name="path">画像のフルパス</param>
    /// <returns>Image。読めない・壊れているときはnull</returns>
    internal static Image? CreateImage(string path)
    {
        try
        {
            var uri = new Uri(path);
            // 先に元の幅だけ調べる(小さな画像を引き伸ばさず、大きな画像だけ読み込み時に縮める)
            int originalWidth;
            using (var stream = File.OpenRead(path)) // 調べ終わったらすぐ離す(掴んだままだと、画像の上書き・削除ができない)
            {
                originalWidth = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None).Frames[0].PixelWidth;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // 読み込んだらファイルを離す(上書き・削除を邪魔しない)
            if (originalWidth > ImageDecodeMaxWidth) bitmap.DecodePixelWidth = ImageDecodeMaxWidth;
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Width = Math.Min(originalWidth, ImageMaxWidth),
                MaxHeight = ImageMaxHeight,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Cursor = Cursors.Hand,
                ToolTip = path,
                Margin = new Thickness(0, 2, 0, 2),
            };
            image.MouseLeftButtonUp += (_, _) => LinkOpener.Open(path);
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException
                                       or FileFormatException or UriFormatException or ArgumentException)
        {
            return null; // 画像として読めないときは、リンクだけ残る
        }
    }
}
