using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NamakeKaigi.Views;

/// <summary>
/// 吹き出しの本文を、文字の選択・コピーができる形で出す、読み取り専用のRichTextBox。
/// RichTextBoxは中身の幅に合わせて縮まないため、中身の文字の幅を測って、短い返答では吹き出しを狭くする。
/// </summary>
public sealed class MarkdownTextBox : RichTextBox
{
    // 測った幅の誤差で、最後の1文字だけが折り返さないようにする余裕(px)
    private const double WidthSafetyMargin = 8;

    // 表ごとの列の決め方。Weights が null の列は中身の幅に合わせ、数値の列は残りの幅をその比率で分け合う。
    // (文書の表は、固定の幅と比率の幅を混ぜると、固定の幅も比率として扱われるため、すべての列をpxで決める)
    private sealed record TableLayout(Table Table, IReadOnlyList<double?> Weights)
    {
        public double[] Natural { get; set; } = Array.Empty<double>();
    }

    private readonly List<TableLayout> _tables = new();

    private double _cachedNaturalWidth = double.NaN;
    private double _cachedFontSize = double.NaN;
    private FontFamily? _cachedFontFamily;

    /// <summary>読み取り専用・枠なし・背景なしで作る。</summary>
    public MarkdownTextBox()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true; // リンク(Hyperlink)をクリックで開けるように(読み取り専用でも、文字の選択はできる)
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            IsHyphenationEnabled = false,
        };
        PreviewMouseWheel += ForwardMouseWheel;
    }

    /// <summary>表の列の決め方を登録する。列幅は、文字の幅を測ってから決める。</summary>
    /// <param name="table">表</param>
    /// <param name="weights">列ごとの比率。nullの列は中身の幅に合わせる</param>
    public void AddTable(Table table, IReadOnlyList<double?> weights) => _tables.Add(new TableLayout(table, weights));

    /// <summary>中身の幅(折り返さないときの幅)を上限に、使える幅の中で大きさを決める。</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var natural = GetNaturalWidth();
        var width = Math.Min(natural, constraint.Width);
        LayoutTables(width);
        var size = base.MeasureOverride(new Size(width, constraint.Height));
        return new Size(width, size.Height);
    }

    /// <summary>中身を折り返さずに並べたときの幅を返す。フォントが変わらない間は、測った結果を使い回す。</summary>
    /// <returns>幅(px)</returns>
    public double GetNaturalWidth()
    {
        if (!double.IsNaN(_cachedNaturalWidth) && _cachedFontSize == FontSize && Equals(_cachedFontFamily, FontFamily))
        {
            return _cachedNaturalWidth;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 表の列ごとに、一番広いセルの幅を測っておく(表の幅と、列幅の決定に使う)
        foreach (var layout in _tables)
        {
            var rows = layout.Table.RowGroups.SelectMany(g => g.Rows).ToList();
            layout.Natural = Enumerable.Range(0, layout.Weights.Count)
                .Select(c => Math.Ceiling(rows.Where(r => c < r.Cells.Count)
                    .Select(r => MeasureCell(r.Cells[c], pixelsPerDip)).DefaultIfEmpty(0).Max()) + 2)
                .ToArray();
        }

        _cachedNaturalWidth = Math.Ceiling(MeasureBlocks(Document.Blocks, pixelsPerDip)) + PagePaddingWidth + WidthSafetyMargin;
        _cachedFontSize = FontSize;
        _cachedFontFamily = FontFamily;
        return _cachedNaturalWidth;
    }

    /// <summary>
    /// 表の列幅を、使える幅に合わせてpxで決める。中身に合わせる列は測った幅のまま、
    /// 比率の列は、残りの幅に収まるなら測った幅、収まらないなら残りの幅を比率で分け合う。
    /// </summary>
    /// <param name="width">本文の幅</param>
    private void LayoutTables(double width)
    {
        foreach (var layout in _tables)
        {
            var table = layout.Table;
            var available = width - PagePaddingWidth - WidthSafetyMargin - Horizontal(table.Margin) - Horizontal(table.BorderThickness);
            var fixedSum = 0.0;
            var flexibleSum = 0.0;
            var weightSum = 0.0;
            for (var c = 0; c < layout.Weights.Count; c++)
            {
                if (layout.Weights[c] is { } weight)
                {
                    flexibleSum += layout.Natural[c];
                    weightSum += weight;
                }
                else
                {
                    fixedSum += layout.Natural[c];
                }
            }

            var rest = Math.Max(0, available - fixedSum);
            for (var c = 0; c < layout.Weights.Count; c++)
            {
                var columnWidth = layout.Weights[c] is { } weight && flexibleSum > rest
                    ? Math.Floor(rest * weight / weightSum)
                    : layout.Natural[c];
                // 同じ幅を入れ直すと、測り直しが繰り返されるため、変わったときだけ入れる
                if (!table.Columns[c].Width.IsAbsolute || table.Columns[c].Width.Value != columnWidth)
                {
                    table.Columns[c].Width = new GridLength(Math.Max(1, columnWidth));
                }
            }
        }
    }

    /// <summary>ブロックの並びのうち、一番広いものの幅を測る。</summary>
    private double MeasureBlocks(IEnumerable<Block> blocks, double pixelsPerDip)
    {
        var widest = 0.0;
        foreach (var block in blocks)
        {
            var inner = block switch
            {
                Paragraph p => MeasureInlines(p.Inlines, pixelsPerDip),
                List list => list.ListItems.Select(i => MeasureBlocks(i.Blocks, pixelsPerDip)).DefaultIfEmpty(0).Max(),
                Section s => MeasureBlocks(s.Blocks, pixelsPerDip),
                Table t => _tables.FirstOrDefault(l => l.Table == t)?.Natural.Sum() ?? 0,
                _ => 0, // 区切り線など(幅は周りに合わせる)
            };
            widest = Math.Max(widest, inner + Horizontal(block.Margin) + Horizontal(block.Padding) + Horizontal(block.BorderThickness));
        }
        return widest;
    }

    /// <summary>セルの中身の幅に、セルの余白と線の太さを足して返す。</summary>
    private double MeasureCell(TableCell cell, double pixelsPerDip)
    {
        return MeasureBlocks(cell.Blocks, pixelsPerDip) + Horizontal(cell.Padding) + Horizontal(cell.BorderThickness);
    }

    /// <summary>インラインを1行に並べた幅を測る。改行(LineBreak)があれば、一番長い行の幅を返す。</summary>
    private static double MeasureInlines(InlineCollection inlines, double pixelsPerDip)
    {
        var widest = 0.0;
        var current = 0.0;
        foreach (var inline in Flatten(inlines))
        {
            if (inline is LineBreak)
            {
                widest = Math.Max(widest, current);
                current = 0;
            }
            else if (inline is Run run && run.Text.Length > 0)
            {
                var typeface = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);
                var formatted = new FormattedText(run.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, run.FontSize, Brushes.Black, pixelsPerDip);
                current += formatted.WidthIncludingTrailingWhitespace;
            }
            else if (inline is InlineUIContainer { Child: FrameworkElement { Width: > 0 } element })
            {
                current += element.Width; // 画像など(吹き出しが画像より狭くならないように)
            }
        }
        return Math.Max(widest, current);
    }

    /// <summary>太字・下線などの入れ子をほどいて、Run と LineBreak の並びにする。</summary>
    private static IEnumerable<Inline> Flatten(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var child in Flatten(span.Inlines)) yield return child;
            }
            else
            {
                yield return inline;
            }
        }
    }

    private static double Horizontal(Thickness t) => t.Left + t.Right;

    /// <summary>
    /// 文書の左右の余白(PagePadding)の合計。RichTextBoxは、文書を入れたときに余白を左右5pxずつに戻すため、
    /// 作るときに0を指定しても残る。測った文字の幅にこれを足さないと、最後の1文字が折り返す。
    /// </summary>
    private double PagePaddingWidth
    {
        get
        {
            var padding = Horizontal(Document.PagePadding);
            return double.IsNaN(padding) ? 0 : padding;
        }
    }

    /// <summary>
    /// ホイールを、外側(会議の画面のスクロール)へ渡す。
    /// RichTextBox・TextBoxは、スクロールする必要がなくてもホイールを受け止めてしまい、画面がスクロールしなくなるため。
    /// </summary>
    /// <param name="sender">吹き出しの本文</param>
    /// <param name="e">ホイールの操作</param>
    public static void ForwardMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject source) return;
        if (VisualTreeHelper.GetParent(source) is not UIElement parent) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        });
    }
}
