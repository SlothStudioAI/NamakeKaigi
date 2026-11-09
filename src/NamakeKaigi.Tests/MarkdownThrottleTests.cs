using System.Windows.Controls;
using System.Windows.Threading;
using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>返答が流れている間の、吹き出しの作り直しの間引き(MarkdownFormatter.StreamRenderInterval)の試験。</summary>
public class MarkdownThrottleTests
{
    /// <summary>流れている吹き出しの入れ物を作る。</summary>
    /// <param name="isStreaming">流れている最中ならtrue</param>
    private static ContentControl CreateHost(bool isStreaming) => new()
    {
        DataContext = new MessageViewModel(Participants.Claude, "", DateTime.Now) { IsStreaming = isStreaming },
    };

    [Fact]
    public void 流れている間は_間隔より早く届いた本文を1回にまとめて_最新の本文で作り直す()
    {
        UiThread.Run(() =>
        {
            var original = MarkdownFormatter.StreamRenderInterval;
            MarkdownFormatter.StreamRenderInterval = TimeSpan.FromMilliseconds(150);
            try
            {
                var host = CreateHost(isStreaming: true);
                MarkdownFormatter.SetMarkdown(host, "最初");
                var first = host.Content; // 最初の1回は、すぐ作る
                Assert.NotNull(first);

                MarkdownFormatter.SetMarkdown(host, "最初の続き");
                MarkdownFormatter.SetMarkdown(host, "最初の続きの続き");
                Assert.Same(first, host.Content); // 間隔の内なので、まだ作り直さない

                UiThread.Pump(400);
                Assert.NotSame(first, host.Content); // 間隔がたったら、1回だけ作り直す
                Assert.Contains("最初の続きの続き", ContentText(host));
            }
            finally
            {
                MarkdownFormatter.StreamRenderInterval = original;
            }
        });
    }

    [Fact]
    public void 流れていない吹き出しは_間引かずにすぐ作り直す()
    {
        UiThread.Run(() =>
        {
            var host = CreateHost(isStreaming: false);
            MarkdownFormatter.SetMarkdown(host, "ひとつめ");
            var first = host.Content;
            MarkdownFormatter.SetMarkdown(host, "ふたつめ");
            Assert.NotSame(first, host.Content);
        });
    }

    [Fact]
    public void 間隔が0なら_届くたびに作り直す()
    {
        UiThread.Run(() =>
        {
            var original = MarkdownFormatter.StreamRenderInterval;
            MarkdownFormatter.StreamRenderInterval = TimeSpan.Zero;
            try
            {
                var host = CreateHost(isStreaming: true);
                MarkdownFormatter.SetMarkdown(host, "あ");
                var first = host.Content;
                MarkdownFormatter.SetMarkdown(host, "あい");
                Assert.NotSame(first, host.Content);
            }
            finally
            {
                MarkdownFormatter.StreamRenderInterval = original;
            }
        });
    }

    /// <summary>作った中身から、文字だけを取り出す(試験で本文を確かめる用)。</summary>
    private static string ContentText(ContentControl host)
    {
        var box = host.Content as System.Windows.DependencyObject;
        var texts = new List<string>();
        void Walk(System.Windows.DependencyObject? node)
        {
            if (node is null) return;
            if (node is TextBlock tb) texts.Add(tb.Text);
            if (node is System.Windows.Documents.TextElement te && te is System.Windows.Documents.Run run) texts.Add(run.Text);
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<object>())
            {
                if (child is string s) texts.Add(s);
                else Walk(child as System.Windows.DependencyObject);
            }
        }
        if (box is RichTextBox rtb)
            return new System.Windows.Documents.TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;
        Walk(box);
        return string.Concat(texts);
    }
}
