using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using NamakeKaigi.Services;
using static NamakeKaigi.Services.MarkdownSyntax;
using static NamakeKaigi.Views.MarkdownInlineBuilder;
using static NamakeKaigi.Views.MarkdownTheme;

namespace NamakeKaigi.Views;

/// <summary>
/// 本文(Markdown)を、行をまたぐブロック(見出し・引用・箇条書き・コード・表・区切り線)に分けて、文書の部品に組み立てる。
/// 記法の判定は MarkdownSyntax、行の中(太字・リンク・画像)は MarkdownInlineBuilder、色は MarkdownTheme が受け持つ。
/// </summary>
public static class MarkdownBlockBuilder
{
    // 表の列幅の比率に使う、1列あたりの文字数(半角換算)の下限と上限(短い列が潰れず、長い列が幅を取りすぎないように)
    private const int TableColumnMinWeight = 2;
    private const int TableColumnMaxWeight = 40;

    // この文字数(半角換算)以下の列は、比率ではなく中身の幅に合わせる(「修正完了」のような短い列が折り返さないように)
    private const int TableAutoColumnMaxWidth = 12;

    /// <summary>テキストから吹き出しの中身(UIElement)を組み立てる。</summary>
    /// <param name="text">本文</param>
    /// <param name="foreground">文字色</param>
    /// <returns>描画するUIElement(Markdownなら、文字を選択できる MarkdownTextBox)</returns>
    public static UIElement BuildElement(string text, Brush? foreground = null)
    {
        if (!ContainsMarkdownSyntax(text))
        {
            return CreatePlainTextBox(text, foreground);
        }

        // 本文全体を1つの文書にまとめる(行をまたいで選択・コピーできるように)
        var box = new MarkdownTextBox { Foreground = foreground ?? PrimaryText };
        var blocks = box.Document.Blocks;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inCodeBlock = false;
        var codeLines = new List<string>();
        var gapBefore = false;    // 空行のあと(次のブロックの上を少し空ける)
        List? currentList = null; // 同じ種類・深さの箇条書きが続く間は、同じListに足す
        var currentListKey = "";
        Paragraph? currentQuote = null; // 同じ深さの引用が続く間は、同じ段落に改行でつなぐ
        var currentQuoteLevel = -1;

        void AddBlock(Block block)
        {
            if (gapBefore && blocks.Count > 0)
            {
                var m = block.Margin;
                block.Margin = new Thickness(m.Left, m.Top + 6, m.Right, m.Bottom);
            }
            gapBefore = false;
            blocks.Add(block);
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            var trimmed = rawLine.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLines.Clear();
                }
                else
                {
                    inCodeBlock = false;
                    AddBlock(CreateCodeBlock(codeLines));
                }
                currentList = null;
                continue;
            }

            if (inCodeBlock)
            {
                codeLines.Add(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                gapBefore = true;
                currentList = null;
                currentQuote = null;
                continue;
            }

            // 引用: 「> 」で始まる行。箇条書きの下に字下げされた引用は、その深さに合わせて右にずらす
            if (TryParseQuote(rawLine, out var quoteLevel, out var quoteContent))
            {
                currentList = null; // 引用のあとの箇条書きは、新しい入れ物にする(前の箇条書きに戻すと順番が崩れる)
                if (currentQuote == null || currentQuoteLevel != quoteLevel)
                {
                    currentQuote = CreateQuote(quoteLevel);
                    currentQuoteLevel = quoteLevel;
                    AddBlock(currentQuote);
                }
                else
                {
                    currentQuote.Inlines.Add(new LineBreak());
                }
                AddInlines(currentQuote.Inlines, quoteContent);
                continue;
            }
            currentQuote = null;

            if (TryParseListItem(rawLine, out var level, out var number, out var content))
            {
                var key = $"{level}:{number.HasValue}";
                if (currentList == null || currentListKey != key)
                {
                    currentList = CreateList(level, number);
                    currentListKey = key;
                    AddBlock(currentList);
                }
                var paragraph = CreateParagraph(content);
                paragraph.Foreground = foreground ?? PrimaryText; // 印(・や番号)だけを強調の色にする
                currentList.ListItems.Add(new ListItem(paragraph) { Foreground = HeadingAccent });
                continue;
            }
            currentList = null;

            // 表: 「| 見出し |」の次の行が「|---|」なら、そこから「|」で始まる行が続く所までを表にする
            if (trimmed.StartsWith('|') && index + 1 < lines.Length && IsTableSeparator(lines[index + 1].TrimStart()))
            {
                var rows = new List<IReadOnlyList<string>> { SplitTableRow(trimmed) };
                index += 2;
                while (index < lines.Length && lines[index].TrimStart().StartsWith('|'))
                {
                    rows.Add(SplitTableRow(lines[index].TrimStart()));
                    index++;
                }
                index--; // for文の index++ で、表の次の行へ進む
                AddBlock(CreateTable(rows, box));
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                AddBlock(CreateHorizontalRule());
                continue;
            }

            if (TryParseHeading(trimmed, out var headingLevel, out var headingContent))
            {
                AddBlock(CreateHeading(headingContent, HeadingFontSize(headingLevel)));
            }
            else
            {
                var paragraph = CreateParagraph(rawLine);
                paragraph.Margin = new Thickness(0, 1, 0, 2);
                AddBlock(paragraph);
            }
        }

        if (inCodeBlock && codeLines.Count > 0)
        {
            AddBlock(CreateCodeBlock(codeLines));
        }

        // 右クリック: 選んだ所のコピーと、全文(Markdownの記号ごと)のコピー
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "コピー", Command = ApplicationCommands.Copy, CommandTarget = box });
        var copyAllItem = new MenuItem { Header = "全文をコピー" };
        copyAllItem.Click += (_, _) => Clipboard.SetText(text);
        menu.Items.Add(copyAllItem);
        box.ContextMenu = menu;

        return box;
    }

    /// <summary>見出しの深さに応じた文字の大きさ。####以降は、本文より少し大きい程度にそろえる。</summary>
    /// <param name="level">見出しの深さ(1〜6)</param>
    /// <returns>文字の大きさ</returns>
    private static double HeadingFontSize(int level) => level switch
    {
        1 => 16.5,
        2 => 15.5,
        3 => 14.5,
        _ => 13.5,
    };

    /// <summary>引用の段落(左に線、薄い文字)を作る。字下げの深さは、箇条書きと同じ幅でずらす。</summary>
    /// <param name="level">字下げの深さ</param>
    /// <returns>段落</returns>
    private static Paragraph CreateQuote(int level)
    {
        return new Paragraph
        {
            Foreground = MutedText,
            BorderBrush = TableBorder,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(8, 1, 0, 1),
            Margin = new Thickness(4 + level * 18, 2, 0, 2),
        };
    }

    /// <summary>箇条書き・番号つきの入れ物を作る。折り返した2行目以降は、印の右にそろう。</summary>
    /// <param name="level">深さ</param>
    /// <param name="number">番号つきなら最初の番号</param>
    /// <returns>List</returns>
    private static List CreateList(int level, int? number)
    {
        var list = new List
        {
            MarkerStyle = number.HasValue ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(4 + level * 18, 1, 0, 2),
            Padding = new Thickness(number.HasValue ? 24 : 16, 0, 0, 0),
            MarkerOffset = 6,
        };
        if (number.HasValue) list.StartIndex = Math.Max(1, number.Value);
        return list;
    }

    /// <summary>Markdownを含まない本文を、選択できる読み取り専用のTextBoxで作る。</summary>
    private static TextBox CreatePlainTextBox(string text, Brush? foreground)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Foreground = foreground ?? PrimaryText,
        };
        box.PreviewMouseWheel += MarkdownTextBox.ForwardMouseWheel;
        return box;
    }

    /// <summary>見出しの段落を作る。</summary>
    private static Paragraph CreateHeading(string content, double fontSize)
    {
        var paragraph = new Paragraph
        {
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = HeadingAccent,
            Margin = new Thickness(0, 6, 0, 2),
        };
        AddInlines(paragraph.Inlines, content);
        return paragraph;
    }

    /// <summary>1行の文から、太字・コード・下線を含む段落を作る。</summary>
    private static Paragraph CreateParagraph(string line)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        AddInlines(paragraph.Inlines, line);
        return paragraph;
    }

    /// <summary>コードブロック(等幅・背景・枠つき)を作る。行は改行でつなぐ。</summary>
    private static Paragraph CreateCodeBlock(IReadOnlyList<string> codeLines)
    {
        var paragraph = new Paragraph
        {
            FontFamily = MonoFont,
            FontSize = 12.5,
            Background = CodeBlockBg,
            BorderBrush = CodeBlockBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 4),
        };
        for (var i = 0; i < codeLines.Count; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(codeLines[i]));
        }
        return paragraph;
    }

    /// <summary>区切り線(テーマの枠線の色の細い横線)を作る。</summary>
    private static BlockUIContainer CreateHorizontalRule()
    {
        return new BlockUIContainer(new Border
        {
            Height = 1,
            Background = TableBorder,
        })
        {
            Margin = new Thickness(0, 6, 0, 6),
        };
    }

    /// <summary>表を、枠線つきの文書の表として組み立てる。1行目は見出しとして太字・背景つきにする。</summary>
    /// <param name="rows">1行目が見出しの、セルの一覧</param>
    /// <param name="box">表を入れる本文(中身の幅に合わせる列は、文字の幅を測ってから決めてもらう)</param>
    /// <returns>表</returns>
    private static Table CreateTable(IReadOnlyList<IReadOnlyList<string>> rows, MarkdownTextBox box)
    {
        var columnCount = rows.Max(r => r.Count);
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = TableBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 4),
        };

        // 列幅は、列の最長の文字数に比例させる(「#」のような短い列が広がりすぎないように)。
        // 短い列は、中身の幅に合わせる(どちらも、幅は MarkdownTextBox が文字を測ってpxで決める)
        var weights = new List<double?>();
        for (var c = 0; c < columnCount; c++)
        {
            var longest = rows.Max(r => c < r.Count ? DisplayWidth(r[c]) : 0);
            weights.Add(longest <= TableAutoColumnMaxWidth
                ? null
                : Math.Clamp(longest, TableColumnMinWeight, TableColumnMaxWeight));
            table.Columns.Add(new TableColumn());
        }
        box.AddTable(table, weights);

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (var r = 0; r < rows.Count; r++)
        {
            var isHeader = r == 0;
            var row = new TableRow();
            if (isHeader)
            {
                row.Background = TableHeaderBg;
                row.FontWeight = FontWeights.Bold;
            }
            for (var c = 0; c < columnCount; c++)
            {
                // 右と下にだけ線を引き、最後の列・行は外枠に任せる
                row.Cells.Add(new TableCell(CreateParagraph(c < rows[r].Count ? rows[r][c] : ""))
                {
                    BorderBrush = TableBorder,
                    BorderThickness = new Thickness(0, 0, c < columnCount - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    Padding = new Thickness(6, 4, 6, 4),
                });
            }
            group.Rows.Add(row);
        }
        return table;
    }
}
