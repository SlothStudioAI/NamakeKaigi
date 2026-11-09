using System.Diagnostics;
using System.IO;
using System.Text;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using Xunit.Abstractions;

namespace NamakeKaigi.Tests;

/// <summary>
/// 会議の保存と、過去会議の一覧を作る時間を測る計測用の試験(合否は決めない。結果は試験の出力に出す。WBS 11.51)。
/// 実行: dotnet test --filter "Category=Benchmark&amp;FullyQualifiedName~StorageBenchmark" --logger "console;verbosity=detailed"
/// 実際の保存先で測るときは、環境変数 NAMAKE_REAL_MEETINGS に meetings フォルダのパスを入れる(読むだけ)。
/// </summary>
[Trait("Category", "Benchmark")]
public class StorageBenchmarkTests
{
    private static readonly int[] EntryCounts = { 100, 300, 1000, 3000 };
    private static readonly int[] MeetingCounts = { 40, 200, 500 };
    private const int EntriesPerMeeting = 50;
    private const int EntryChars = 800; // 実際の会議は、1件あたり約0.8KB
    private const int Repeat = 5;

    private readonly ITestOutputHelper _output;

    /// <summary>試験の出力先を受け取る。</summary>
    public StorageBenchmarkTests(ITestOutputHelper output) => _output = output;

    /// <summary>指定の件数の発言を作る。</summary>
    /// <param name="count">件数</param>
    /// <returns>発言の一覧</returns>
    private static List<ChatEntry> MakeEntries(int count)
    {
        var body = new string('あ', EntryChars);
        var list = new List<ChatEntry>(count);
        for (var i = 0; i < count; i++)
            list.Add(new ChatEntry(i % 3 == 0 ? Participants.UserId : "claude", $"{i}: {body}", DateTime.Now, null));
        return list;
    }

    /// <summary>平均と最大のミリ秒を、文字にする。</summary>
    private static string Summary(List<double> ms) => $"平均 {ms.Average():F1} ms / 最大 {ms.Max():F1} ms";

    [Fact]
    public void 状態ファイルの保存にかかる時間を_発言の件数ごとに測る()
    {
        var dir = Path.Combine(Path.GetTempPath(), "namake_bench_save_" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var count in EntryCounts)
            {
                var state = new MeetingState { StartedAt = DateTime.Now, Entries = MakeEntries(count) };
                var path = Path.Combine(dir, "meeting.json");
                MeetingStore.SaveState(path, state); // 1回目は、フォルダ作成や準備の分なので捨てる
                var times = new List<double>();
                for (var i = 0; i < Repeat; i++)
                {
                    var sw = Stopwatch.StartNew();
                    MeetingStore.SaveState(path, state);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                _output.WriteLine($"保存 {count,5}件 ({new FileInfo(path).Length / 1024,5} KB): {Summary(times)}");
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); // この試験が作った一時フォルダだけを消す
        }
    }

    [Fact]
    public void 過去会議の一覧を作る時間を_会議の数ごとに測る()
    {
        foreach (var meetingCount in MeetingCounts)
        {
            var root = Path.Combine(Path.GetTempPath(), "namake_bench_list_" + Guid.NewGuid().ToString("N"));
            try
            {
                var entries = MakeEntries(EntriesPerMeeting);
                for (var m = 0; m < meetingCount; m++)
                {
                    var stamp = DateTime.Now.AddMinutes(-m).ToString("yyyyMMdd_HHmmss");
                    var state = new MeetingState { StartedAt = DateTime.Now.AddMinutes(-m), Entries = entries };
                    MeetingStore.SaveState(Path.Combine(root, stamp + "_" + m, "meeting.json"), state);
                }

                var times = new List<double>();
                IReadOnlyList<SavedMeetingSummary> result = Array.Empty<SavedMeetingSummary>();
                for (var i = 0; i < 3; i++)
                {
                    var sw = Stopwatch.StartNew();
                    result = MeetingStore.ListMeetings(root);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                _output.WriteLine($"一覧 会議{meetingCount,4}件 x 発言{EntriesPerMeeting}件 (取得{result.Count}件): " +
                                  $"1回目 {times[0]:F1} ms / 2〜3回目 平均 {times.Skip(1).Average():F1} ms");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true); // この試験が作った一時フォルダだけを消す
            }
        }
    }

    [Fact]
    public void 実際の保存先があれば_過去会議の一覧を作る時間を測る()
    {
        var real = Environment.GetEnvironmentVariable("NAMAKE_REAL_MEETINGS");
        if (string.IsNullOrWhiteSpace(real) || !Directory.Exists(real))
        {
            _output.WriteLine("NAMAKE_REAL_MEETINGS が未設定、またはフォルダがないので、測りません。");
            return;
        }

        var times = new List<double>();
        IReadOnlyList<SavedMeetingSummary> result = Array.Empty<SavedMeetingSummary>();
        for (var i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            result = MeetingStore.ListMeetings(real); // 読むだけ
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        _output.WriteLine($"実際の保存先 {result.Count}件(発言{result.Sum(m => m.MessageCount)}件): " +
                          $"1回目 {times[0]:F1} ms / 2〜3回目 平均 {times.Skip(1).Average():F1} ms");
    }
}
