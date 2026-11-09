using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NamakeKaigi.Services;

namespace NamakeKaigi.Views;

/// <summary>吹き出しの文字列を、見出し・太字・コード・箇条書き・リンク・画像のMarkdownとして見やすく描く添付プロパティ。</summary>
public static class MarkdownFormatter
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached(
            "Markdown",
            typeof(string),
            typeof(MarkdownFormatter),
            new PropertyMetadata("", OnMarkdownChanged));

    /// <summary>Markdown文字列を取得する。</summary>
    public static string GetMarkdown(DependencyObject obj) => (string)obj.GetValue(MarkdownProperty);

    /// <summary>Markdown文字列を設定する。</summary>
    public static void SetMarkdown(DependencyObject obj, string value) => obj.SetValue(MarkdownProperty, value);

    // 描いた吹き出し(テーマが変わったら描き直す。消えた吹き出しは、弱い参照なので自然に外れる)
    private static readonly List<WeakReference<ContentControl>> Hosts = new();

    static MarkdownFormatter() => Models.ThemeCatalog.Applied += RebuildAll;

    /// <summary>返答が流れてくる間、吹き出しの中身を作り直す最短の間隔(0なら届くたびに作り直す)。設定ファイルから、起動時に入れる。</summary>
    public static TimeSpan StreamRenderInterval { get; set; } = TimeSpan.FromMilliseconds(200);

    // 流れている吹き出しごとの、最後に作り直した時刻と、遅らせている作り直しのタイマー(吹き出しが消えたら自然に外れる)
    private sealed class ThrottleState
    {
        public long LastRenderTicks;
        public DispatcherTimer? Timer;
    }

    private static readonly ConditionalWeakTable<ContentControl, ThrottleState> Throttles = new();

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl host) return;
        if (e.OldValue is null or "") Remember(host);
        RenderThrottled(host, e.NewValue as string ?? "");
    }

    /// <summary>
    /// 流れている間は、作り直しを間隔ごとにまとめる(全文を作り直すので、届くたびだと長い返答ほど重い)。
    /// 間隔より前に次の文字が届いたら、タイマーで最新の本文を1回だけ作り直す。流れていない吹き出しは、すぐ作り直す。
    /// </summary>
    /// <param name="host">吹き出しの本文の入れ物</param>
    /// <param name="text">本文</param>
    private static void RenderThrottled(ContentControl host, string text)
    {
        var streaming = host.DataContext is ViewModels.MessageViewModel { IsStreaming: true };
        var state = Throttles.GetOrCreateValue(host);
        if (!streaming && state.Timer is null || StreamRenderInterval <= TimeSpan.Zero)
        {
            Render(host, text);
            state.LastRenderTicks = Environment.TickCount64;
            return;
        }

        var wait = StreamRenderInterval - TimeSpan.FromMilliseconds(Environment.TickCount64 - state.LastRenderTicks);
        if (wait <= TimeSpan.Zero && state.Timer is null)
        {
            Render(host, text);
            state.LastRenderTicks = Environment.TickCount64;
            return;
        }
        if (state.Timer is not null) return; // 遅らせている作り直しが、そのとき最新の本文を使う

        state.Timer = new DispatcherTimer(DispatcherPriority.Background, host.Dispatcher) { Interval = wait };
        state.Timer.Tick += (_, _) =>
        {
            state.Timer!.Stop();
            state.Timer = null;
            Render(host, GetMarkdown(host));
            state.LastRenderTicks = Environment.TickCount64;
        };
        state.Timer.Start();
    }

    /// <summary>本文から中身を組み立てて、吹き出しに入れる(色は、いまのテーマから取る)。</summary>
    /// <param name="host">吹き出しの本文の入れ物</param>
    /// <param name="text">本文</param>
    private static void Render(ContentControl host, string text)
    {
        var fg = host.DataContext is ViewModels.MessageViewModel { IsError: true }
            ? (Application.Current?.Resources["DangerTextBrush"] as Brush)
            : null;
        host.Content = MarkdownBlockBuilder.BuildElement(text, fg);
    }

    /// <summary>描き直しの対象として覚える(同じ入れ物は1回だけ)。</summary>
    private static void Remember(ContentControl host)
    {
        lock (Hosts)
        {
            Hosts.RemoveAll(w => !w.TryGetTarget(out var h) || ReferenceEquals(h, host));
            Hosts.Add(new WeakReference<ContentControl>(host));
        }
    }

    /// <summary>テーマが変わったので、覚えている吹き出しを、新しい色で描き直す。</summary>
    private static void RebuildAll()
    {
        List<ContentControl> alive;
        lock (Hosts)
        {
            Hosts.RemoveAll(w => !w.TryGetTarget(out _));
            alive = Hosts.Select(w => w.TryGetTarget(out var h) ? h : null).OfType<ContentControl>().ToList();
        }
        foreach (var host in alive)
        {
            if (host.Dispatcher.CheckAccess()) Render(host, GetMarkdown(host));
            else host.Dispatcher.Invoke(() => Render(host, GetMarkdown(host)));
        }
    }

}
