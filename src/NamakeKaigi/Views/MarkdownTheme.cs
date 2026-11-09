using System.Windows;
using System.Windows.Media;

namespace NamakeKaigi.Views;

/// <summary>
/// 吹き出しの中身(Markdown)の色とフォント。いまのテーマ(Application のリソース)から取り、無いときは緑の標準色にする。
/// ブロックの組み立てと、行の中の組み立ての、両方から使う。
/// </summary>
internal static class MarkdownTheme
{
    private static readonly SolidColorBrush FallbackCodeBlockBg = CreateBrush(0xF4, 0xF1, 0xDA);
    private static readonly SolidColorBrush FallbackCodeBlockBorder = CreateBrush(0xD5, 0xDC, 0xC8);
    private static readonly SolidColorBrush FallbackInlineCodeBg = CreateBrush(0xEC, 0xE8, 0xCE);
    private static readonly SolidColorBrush FallbackHeadingAccent = CreateBrush(0x2F, 0x6B, 0x45);
    internal static readonly FontFamily MonoFont = new("Consolas, Yu Gothic UI, Meiryo");

    internal static Brush CodeBlockBg => (Application.Current?.Resources["PanelInsetBrush"] as Brush) ?? FallbackCodeBlockBg;
    internal static Brush CodeBlockBorder => (Application.Current?.Resources["InputBorderBrush"] as Brush) ?? FallbackCodeBlockBorder;
    internal static Brush InlineCodeBg => (Application.Current?.Resources["PanelInsetBrush"] as Brush) ?? FallbackInlineCodeBg;
    internal static Brush HeadingAccent => (Application.Current?.Resources["LinkTextBrush"] as Brush)
                                          ?? (Application.Current?.Resources["AccentGreenBrush"] as Brush)
                                          ?? FallbackHeadingAccent;
    internal static Brush PrimaryText => (Application.Current?.Resources["TextPrimaryBrush"] as Brush) ?? Brushes.Black;
    internal static Brush MutedText => (Application.Current?.Resources["TextMutedBrush"] as Brush) ?? Brushes.Gray;
    internal static Brush TableBorder => (Application.Current?.Resources["PanelBorderBrush"] as Brush) ?? FallbackCodeBlockBorder;
    internal static Brush TableHeaderBg => (Application.Current?.Resources["PanelInsetBrush"] as Brush) ?? FallbackCodeBlockBg;

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
