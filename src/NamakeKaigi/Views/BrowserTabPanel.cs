using System.Windows;
using System.Windows.Controls;

namespace NamakeKaigi.Views;

/// <summary>
/// ブラウザのタブバーのように、タブが収まる間は本来の幅で並べ、
/// 横幅が足りなくなったら長いタブから優先的に縮めて1行に収めるパネル。
/// </summary>
public sealed class BrowserTabPanel : Panel
{
    /// <summary>右横に並べる「＋」ボタンなどのために空けておく幅。</summary>
    public static readonly DependencyProperty ReservedTrailingWidthProperty =
        DependencyProperty.Register(
            nameof(ReservedTrailingWidth),
            typeof(double),
            typeof(BrowserTabPanel),
            new FrameworkPropertyMetadata(42.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>右横に並べる「＋」ボタンなどのために空けておく幅（px）。</summary>
    public double ReservedTrailingWidth
    {
        get => (double)GetValue(ReservedTrailingWidthProperty);
        set => SetValue(ReservedTrailingWidthProperty, value);
    }

    private const double MinTabWidth = 48.0;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return new Size(0, 0);

        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        var desired = new double[count];
        var maxHeight = 0.0;
        var totalDesired = 0.0;

        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(infinite);
            desired[i] = child.DesiredSize.Width;
            totalDesired += desired[i];
            if (child.DesiredSize.Height > maxHeight) maxHeight = child.DesiredSize.Height;
        }

        if (!double.IsInfinity(availableSize.Width))
        {
            var limit = Math.Max(0, availableSize.Width - Math.Max(0, ReservedTrailingWidth));
            if (totalDesired > limit)
            {
                var allocated = AllocateWidths(desired, limit);
                maxHeight = 0.0;
                for (var i = 0; i < count; i++)
                {
                    var child = InternalChildren[i];
                    child.Measure(new Size(allocated[i], availableSize.Height));
                    if (child.DesiredSize.Height > maxHeight) maxHeight = child.DesiredSize.Height;
                }
                return new Size(Math.Min(limit, allocated.Sum()), maxHeight);
            }
        }

        return new Size(totalDesired, maxHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0) return finalSize;

        var widths = new double[count];
        var total = 0.0;
        for (var i = 0; i < count; i++)
        {
            widths[i] = InternalChildren[i].DesiredSize.Width;
            total += widths[i];
        }

        if (total > finalSize.Width && finalSize.Width > 0)
            widths = AllocateWidths(widths, finalSize.Width);

        var x = 0.0;
        for (var i = 0; i < count; i++)
        {
            var w = widths[i];
            InternalChildren[i].Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        return new Size(x, finalSize.Height);
    }

    /// <summary>
    /// 合計幅が limit 以下になるよう、長いタブから順に上限キャップを下げて割り当てる。
    /// 短いタブは本来の幅を保ち、長いタブだけが自然に縮む。
    /// </summary>
    public static double[] AllocateWidths(IReadOnlyList<double> desired, double limit)
    {
        var n = desired.Count;
        var result = new double[n];
        if (n == 0 || limit <= 0) return result;

        var total = 0.0;
        for (var i = 0; i < n; i++) total += desired[i];
        if (total <= limit)
        {
            for (var i = 0; i < n; i++) result[i] = desired[i];
            return result;
        }

        // 上限 cap を二分探索して sum(min(desired[i], cap)) == limit となる cap を求める
        var low = 0.0;
        var high = desired.Max();
        for (var step = 0; step < 32; step++)
        {
            var mid = (low + high) * 0.5;
            var sum = 0.0;
            for (var i = 0; i < n; i++) sum += Math.Min(desired[i], mid);
            if (sum > limit) high = mid;
            else low = mid;
        }

        var minAllowed = Math.Min(MinTabWidth, limit / n);
        var allocatedSum = 0.0;
        for (var i = 0; i < n; i++)
        {
            result[i] = Math.Max(minAllowed, Math.Min(desired[i], low));
            allocatedSum += result[i];
        }

        // minAllowed の下限でわずかに超えた場合だけ均等スケーリングで確実に収める
        if (allocatedSum > limit && allocatedSum > 0)
        {
            var scale = limit / allocatedSum;
            for (var i = 0; i < n; i++) result[i] *= scale;
        }
        return result;
    }
}
