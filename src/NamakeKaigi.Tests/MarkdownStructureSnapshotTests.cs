using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>
/// 吹き出しの組み立て(MarkdownBlockBuilder.BuildElement)の結果の構造を記録して、リファクタリングで変わらないことを確かめる試験。
/// 記録は Snapshots/markdown_structure.txt。意図して見た目を変えたときだけ、環境変数 MD_SNAPSHOT_UPDATE=1 で書き直す。
/// </summary>
public class MarkdownStructureSnapshotTests
{
    private const string UpdateVariable = "MD_SNAPSHOT_UPDATE";

    // 試験の対象にする本文(記法をひと通り含める)。画像のパスは {IMG} に差し替える
    private static readonly (string Name, string Text)[] Samples =
    {
        ("plain", "ただの文章です。記号は入っていません。"),
        ("heading", "# 見出し1\n## 見出し2\n### 見出し3\n#### 見出し4\n本文"),
        ("inline", "**太字**と`コード`と<u>下線</u>が、1行に **a** `b` <u>c</u> と並ぶ。"),
        ("list", "- りんご\n- みかん\n  - ふじ\n    - 深さ3\n1. 最初\n2. 次\n\n- 空行のあとの箇条書き"),
        ("quote", "> 引用1\n> 引用2\n> > 深い引用\n\n本文\n\n- 箇条書き\n  > 箇条書きの下の引用"),
        ("code", "前の文\n```csharp\nvar x = 1;\n  indented();\n```\n後の文\n```\n閉じていないコード"),
        ("table", "| 項目 | 状態 |\n| --- | :---: |\n| 発言の数 | 多い |\n| a\\|b | **太字** |\n\n表のあとの文"),
        ("table_wide", "| # | 説明がとても長い列の見出し |\n|---|---|\n| 1 | これは長い長い説明の文章で、列幅の比率が掛かる |"),
        ("rule", "上\n---\n中\n* * *\n下\n___"),
        ("links", "https://example.com/path を開く。[名前](https://example.com/a) と [無い](C:/no/such/file.txt) と `https://example.com/b`"),
        ("image", "![説明]({IMG}) の後ろに文字\n`{IMG}`"),
        ("mixed", "# 結論\n\n- **原因**: `MarkdownFormatter` が全文を作り直す\n- 対策:\n  1. 間引く\n  2. 仮想化する\n\n| 項目 | 値 |\n|---|---|\n| 速度 | 速い |\n\n> 注意\n\n```\ncode\n```\n---\n終わり"),
    };

    /// <summary>色を、比べやすい文字列にする。</summary>
    private static string Brush(Brush? b) => b switch
    {
        null => "null",
        SolidColorBrush s => s.Color.ToString(),
        _ => b.GetType().Name,
    };

    /// <summary>インライン要素を、構造がわかる文字列にする。</summary>
    private static void DumpInline(StringBuilder sb, Inline inline, string indent, string imageDir)
    {
        switch (inline)
        {
            case Run r:
                sb.AppendLine($"{indent}Run \"{r.Text}\" font={r.FontFamily?.Source} bg={Brush(r.Background)}");
                break;
            case Hyperlink h:
                sb.AppendLine($"{indent}Hyperlink tip={h.ToolTip} fg={Brush(h.Foreground)}");
                foreach (var child in h.Inlines) DumpInline(sb, child, indent + "  ", imageDir);
                break;
            case Span s: // Bold・Underline など
                sb.AppendLine($"{indent}{s.GetType().Name}");
                foreach (var child in s.Inlines) DumpInline(sb, child, indent + "  ", imageDir);
                break;
            case LineBreak:
                sb.AppendLine($"{indent}LineBreak");
                break;
            case InlineUIContainer c:
                var image = c.Child as Image;
                sb.AppendLine($"{indent}InlineUIContainer {c.Child?.GetType().Name} width={image?.Width} maxH={image?.MaxHeight} tip={image?.ToolTip?.ToString()?.Replace(imageDir, "{DIR}")}");
                break;
            default:
                sb.AppendLine($"{indent}{inline.GetType().Name}");
                break;
        }
    }

    /// <summary>ブロック要素を、構造がわかる文字列にする。</summary>
    private static void DumpBlock(StringBuilder sb, Block block, string indent, string imageDir)
    {
        switch (block)
        {
            case Paragraph p:
                sb.AppendLine($"{indent}Paragraph fs={p.FontSize} fw={p.FontWeight} margin={p.Margin} pad={p.Padding} border={p.BorderThickness} " +
                              $"fg={Brush(p.Foreground)} bg={Brush(p.Background)} borderBrush={Brush(p.BorderBrush)} font={p.FontFamily?.Source}");
                foreach (var inline in p.Inlines) DumpInline(sb, inline, indent + "  ", imageDir);
                break;
            case System.Windows.Documents.List l:
                sb.AppendLine($"{indent}List marker={l.MarkerStyle} start={l.StartIndex} margin={l.Margin} pad={l.Padding} offset={l.MarkerOffset}");
                foreach (var item in l.ListItems)
                {
                    sb.AppendLine($"{indent}  ListItem fg={Brush(item.Foreground)}");
                    foreach (var child in item.Blocks) DumpBlock(sb, child, indent + "    ", imageDir);
                }
                break;
            case Table t:
                sb.AppendLine($"{indent}Table columns={t.Columns.Count} margin={t.Margin} border={t.BorderThickness} borderBrush={Brush(t.BorderBrush)}");
                foreach (var group in t.RowGroups)
                foreach (var row in group.Rows)
                {
                    sb.AppendLine($"{indent}  Row bg={Brush(row.Background)} fw={row.FontWeight}");
                    foreach (var cell in row.Cells)
                    {
                        sb.AppendLine($"{indent}    Cell border={cell.BorderThickness} pad={cell.Padding}");
                        foreach (var child in cell.Blocks) DumpBlock(sb, child, indent + "      ", imageDir);
                    }
                }
                break;
            case BlockUIContainer c:
                sb.AppendLine($"{indent}BlockUIContainer {c.Child?.GetType().Name} height={(c.Child as Border)?.Height} margin={c.Margin}");
                break;
            default:
                sb.AppendLine($"{indent}{block.GetType().Name}");
                break;
        }
    }

    /// <summary>組み立てた吹き出しを、構造がわかる文字列にする。</summary>
    private static string Dump(UIElement element, string imageDir)
    {
        var sb = new StringBuilder();
        switch (element)
        {
            case MarkdownTextBox box:
                sb.AppendLine($"MarkdownTextBox fg={Brush(box.Foreground)} menu={box.ContextMenu?.Items.Count}");
                foreach (var block in box.Document.Blocks) DumpBlock(sb, block, "  ", imageDir);
                break;
            case TextBox text:
                sb.AppendLine($"TextBox text=\"{text.Text}\" readOnly={text.IsReadOnly} fg={Brush(text.Foreground)}");
                break;
            default:
                sb.AppendLine(element.GetType().Name);
                break;
        }
        return sb.ToString();
    }

    /// <summary>小さな画像ファイルを作って、そのフルパスを返す。</summary>
    private static string CreateImageFile(string dir)
    {
        var path = Path.Combine(dir, "sample.png");
        var pixels = new byte[4 * 4 * 4];
        Array.Fill(pixels, (byte)200);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path.Replace('\\', '/');
    }

    private static string SnapshotPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", "markdown_structure.txt");

    [Fact]
    public void 吹き出しの組み立ての構造が_記録と同じ()
    {
        using var temp = new TempDir();
        var imagePath = CreateImageFile(temp.Path);
        var actual = new StringBuilder();
        UiThread.Run(() =>
        {
            // 色はテーマで変わる。ほかの試験が切り替えた状態を引き継がないよう、既定のテーマにそろえてから組み立てる
            // (同じ画面スレッドの1回の処理の中なので、ほかの試験は途中に入れない)
            Models.ThemeCatalog.Apply(Models.ThemeCatalog.DefaultThemeId);
            foreach (var (name, text) in Samples)
            {
                actual.AppendLine($"=== {name}");
                var element = MarkdownBlockBuilder.BuildElement(text.Replace("{IMG}", imagePath));
                actual.Append(Dump(element, temp.Path.Replace('\\', '/')));
            }
        });
        // 一時フォルダのパスは毎回変わるので、固定の印に差し替える(リンクの先・画像の説明など、どこに出ても)
        var result = actual.ToString().Replace("\r\n", "\n").Replace(temp.Path.Replace('\\', '/'), "{DIR}");

        var path = SnapshotPath();
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, result, new UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(path), $"記録がありません。{UpdateVariable}=1 で作ってください: {path}");
        Assert.Equal(File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n"), result);
    }
}
