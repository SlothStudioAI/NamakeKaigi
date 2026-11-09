using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;
using NamakeKaigi.Views;
using Xunit.Abstractions;

namespace NamakeKaigi.Tests;

/// <summary>
/// 「会話が増えると重い」原因を測る計測用の試験(合否は決めない。結果は試験の出力に出す)。
/// 実行: dotnet test --filter Category=Benchmark --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "Benchmark")]
public class PerformanceBenchmarkTests
{
    // 履歴の件数(いままでの会話の長さ)
    private static readonly int[] HistoryCounts = { 0, 50, 150, 300 };

    // 流れてくる返答: 1回に届く文字数と、合計の文字数
    private const int ChunkChars = 30;
    private const int StreamTotalChars = 3000;

    private static readonly Size WindowSize = new(1040, 720);

    private readonly ITestOutputHelper _output;

    /// <summary>試験の出力先を受け取る。</summary>
    public PerformanceBenchmarkTests(ITestOutputHelper output) => _output = output;

    /// <summary>Markdownらしい本文(見出し・箇条書き・表・コード・強調を含む)を、指定の文字数以上で作る。</summary>
    /// <param name="minChars">最小の文字数</param>
    /// <returns>本文</returns>
    private static string SampleMarkdown(int minChars)
    {
        const string unit =
            "## 調べた結果\n\n" +
            "- **原因**: `MarkdownFormatter` が全文を作り直している\n" +
            "- 対策は、流れている間は軽い表示にして、終わってから整える\n\n" +
            "| 項目 | 状態 |\n| --- | --- |\n| 発言の数 | 多い |\n| 描き直し | 毎回 |\n\n" +
            "```csharp\nvar x = Build(text);\n```\n\n" +
            "普通の文章がここに入ります。日本語の長めの文章で、折り返しの計算にも時間がかかるかを見ます。\n\n";
        var sb = new StringBuilder();
        while (sb.Length < minChars) sb.Append(unit);
        return sb.ToString();
    }

    /// <summary>履歴つきの画面を作り、画面の配置まで済ませる。</summary>
    /// <param name="historyCount">履歴の件数</param>
    /// <param name="vm">作った画面の元データ(出力)</param>
    /// <param name="window">作った窓(出力)</param>
    /// <returns>作るのにかかったミリ秒</returns>
    private static double BuildWindow(int historyCount, out MainViewModel vm, out Window window)
    {
        var createdVm = new MainViewModel(() => new FakeSession());
        Window? createdWindow = null;
        var body = SampleMarkdown(600);
        var sw = Stopwatch.StartNew();
        UiThread.Run(() =>
        {
            for (var i = 0; i < historyCount; i++)
            {
                var speaker = i % 2 == 0 ? Participants.Claude : Participants.Gemini;
                createdVm.Messages.Add(new MessageViewModel(speaker, body, DateTime.Now));
            }
            createdWindow = new MainWindow { DataContext = createdVm };
            var root = (FrameworkElement)createdWindow.Content;
            root.Measure(WindowSize);
            root.Arrange(new Rect(WindowSize));
            root.UpdateLayout();
        });
        sw.Stop();
        vm = createdVm;
        window = createdWindow!;
        return sw.Elapsed.TotalMilliseconds;
    }

    // 流れてくる間の計測: 届く間隔(ms)と、作り直しの最短間隔(ms)の組み合わせ
    private const int ChunkIntervalMs = 50;
    private static readonly int[] RenderIntervalsMs = { 0, 200 };

    [Fact]
    public void 履歴の件数ごとに_画面を開く時間と_返答が流れる間の画面スレッドの忙しさを測る()
    {
        _output.WriteLine("履歴件数 | 画面を開く(ms) | 作り直しの最短間隔(ms) | 流れる間の画面の忙しさ合計(ms) | うち1更新あたり平均(ms)");
        var original = MarkdownFormatter.StreamRenderInterval;
        try
        {
            foreach (var count in HistoryCounts)
            foreach (var intervalMs in RenderIntervalsMs)
            {
                MarkdownFormatter.StreamRenderInterval = TimeSpan.FromMilliseconds(intervalMs);
                var openMs = BuildWindow(count, out var vm, out var window);

                var streamingText = SampleMarkdown(StreamTotalChars)[..StreamTotalChars];
                double busy = 0;
                var updates = 0;
                UiThread.Run(() =>
                {
                    var root = (FrameworkElement)window.Content;
                    var streaming = new MessageViewModel(Participants.Claude, "", DateTime.Now) { IsStreaming = true };
                    vm.Messages.Add(streaming);
                    for (var len = ChunkChars; len <= StreamTotalChars; len += ChunkChars)
                    {
                        var sw = Stopwatch.StartNew();
                        streaming.Text = streamingText[..len]; // 受信のたびに、本文が増える
                        root.UpdateLayout();                    // 画面が配置し直される
                        busy += sw.Elapsed.TotalMilliseconds;
                        sw.Restart();
                        UiThread.Pump(ChunkIntervalMs);                   // 次の文字が届くまで。間引いた作り直しは、ここで動く
                        busy += Math.Max(0, sw.Elapsed.TotalMilliseconds - ChunkIntervalMs);
                        updates++;
                    }
                    streaming.IsStreaming = false;
                    UiThread.Pump(intervalMs + ChunkIntervalMs);          // 最後の作り直しを待つ
                });
                _output.WriteLine($"{count,8} | {openMs,13:F0} | {intervalMs,21} | {busy,29:F0} | {busy / updates,22:F1}");
            }
        }
        finally
        {
            MarkdownFormatter.StreamRenderInterval = original;
        }
    }

    // 返答の長さごとの計測(WBS 11.51): 流れる間の画面スレッドの忙しさが、返答の長さで増えるか
    // 20000文字は約4分かかるので、環境変数 NAMAKE_BENCH_LONG があるときだけ測る
    private static readonly int[] ReplyLengths =
        Environment.GetEnvironmentVariable("NAMAKE_BENCH_LONG") is null ? new[] { 1000, 3000, 8000 } : new[] { 1000, 3000, 8000, 20000 };

    [Fact]
    public void 返答の長さごとに_流れる間の画面スレッドの忙しさと_一番長い引っかかりを測る()
    {
        _output.WriteLine("返答の長さ(文字) | 更新の回数 | 忙しさ合計(ms) | 1更新あたり平均(ms) | 一番長い1回(ms) | 後半の1更新あたり平均(ms)");
        var original = MarkdownFormatter.StreamRenderInterval;
        try
        {
            MarkdownFormatter.StreamRenderInterval = TimeSpan.FromMilliseconds(200); // いまの既定
            foreach (var total in ReplyLengths)
            {
                BuildWindow(0, out var vm, out var window);
                var streamingText = SampleMarkdown(total)[..total];
                var perUpdate = new List<double>();
                UiThread.Run(() =>
                {
                    var root = (FrameworkElement)window.Content;
                    var streaming = new MessageViewModel(Participants.Claude, "", DateTime.Now) { IsStreaming = true };
                    vm.Messages.Add(streaming);
                    for (var len = ChunkChars; len <= total; len += ChunkChars)
                    {
                        var sw = Stopwatch.StartNew();
                        streaming.Text = streamingText[..len];
                        root.UpdateLayout();
                        var busy = sw.Elapsed.TotalMilliseconds;
                        sw.Restart();
                        UiThread.Pump(ChunkIntervalMs);
                        busy += Math.Max(0, sw.Elapsed.TotalMilliseconds - ChunkIntervalMs);
                        perUpdate.Add(busy);
                    }
                    streaming.IsStreaming = false;
                    UiThread.Pump(200 + ChunkIntervalMs);
                });
                var half = perUpdate.Skip(perUpdate.Count / 2).ToList();
                _output.WriteLine($"{total,16} | {perUpdate.Count,10} | {perUpdate.Sum(),14:F0} | {perUpdate.Average(),19:F1} | {perUpdate.Max(),15:F0} | {half.Average(),24:F1}");
            }
        }
        finally
        {
            MarkdownFormatter.StreamRenderInterval = original;
        }
    }

    [Fact]
    public void 会議室を切り替える時間を測る_まとめて見るときと別々に見るとき()
    {
        _output.WriteLine("履歴件数(各部屋) | 見え方 | 切り替え(ms)");
        foreach (var count in HistoryCounts.Where(c => c > 0))
        foreach (var split in new[] { false, true })
        {
            BuildWindow(count, out var vmA, out var window);
            var vmB = new MainViewModel(() => new FakeSession());
            var body = SampleMarkdown(600);
            double ms = 0;
            UiThread.Run(() =>
            {
                for (var i = 0; i < count; i++)
                {
                    var speaker = i % 3 == 0 ? Participants.Claude : i % 3 == 1 ? Participants.Gemini : Participants.Chappy;
                    vmB.Messages.Add(new MessageViewModel(speaker, body, DateTime.Now));
                    (speaker.Id == Participants.ClaudeId ? vmB.ClaudeMessages
                        : speaker.Id == Participants.GeminiId ? vmB.GeminiMessages : vmB.ChappyMessages)
                        .Add(new MessageViewModel(speaker, body, DateTime.Now));
                }
                vmA.IsSplitView = split;
                vmB.IsSplitView = split;
                var root = (FrameworkElement)window.Content;
                root.UpdateLayout();
                var sw = Stopwatch.StartNew();
                window.DataContext = vmB; // 会議室(タブ)を切り替える
                root.UpdateLayout();
                ms = sw.Elapsed.TotalMilliseconds;
            });
            _output.WriteLine($"{count,16} | {(split ? "別々に見る" : "まとめて見る"),-8} | {ms,10:F0}");
        }
    }

    [Fact]
    public void 会議室の切り替えの内訳を測る_何が重いかを切り分ける()
    {
        const int count = 100;
        _output.WriteLine("切り替え先の中身 | 切り替え(ms)");
        foreach (var variant in new[] { "空", "全体の発言だけ", "各AIの列の発言だけ" })
        {
            BuildWindow(count, out _, out var window);
            var vmB = new MainViewModel(() => new FakeSession());
            var body = SampleMarkdown(600);
            double ms = 0;
            UiThread.Run(() =>
            {
                for (var i = 0; i < count; i++)
                {
                    if (variant == "全体の発言だけ")
                        vmB.Messages.Add(new MessageViewModel(Participants.Claude, body, DateTime.Now));
                    if (variant == "各AIの列の発言だけ")
                        vmB.ClaudeMessages.Add(new MessageViewModel(Participants.Claude, body, DateTime.Now));
                }
                var root = (FrameworkElement)window.Content;
                root.UpdateLayout();
                var sw = Stopwatch.StartNew();
                window.DataContext = vmB;
                var afterSet = sw.Elapsed.TotalMilliseconds;
                root.UpdateLayout();
                ms = sw.Elapsed.TotalMilliseconds;
                _output.WriteLine($"  ({variant}: DataContext設定まで {afterSet:F0}ms)");
            });
            _output.WriteLine($"{variant,-14} | {ms,10:F0}");
        }
    }
}
