using System.Collections.Concurrent;
using System.IO;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>会議フォルダごとに summary.md を自動生成し、起動時の未作成分バックフィルも行う。</summary>
public sealed class MeetingSummaryBackfillService
{
    /// <summary>summary.md を自動生成する最小発言数。</summary>
    public const int MinMessageCount = 4;

    private readonly MeetingSummaryService _summaryService;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>サービスを作る。</summary>
    /// <param name="summaryService">要約生成サービス</param>
    public MeetingSummaryBackfillService(MeetingSummaryService summaryService)
    {
        _summaryService = summaryService;
    }

    /// <summary>
    /// 指定した会議フォルダの summary.md を必要に応じて生成・更新する。
    /// 条件: 要約機能が有効、発言数が4件以上、自動実行ではない、かつ未作成または meeting.json が summary.md より新しい場合。
    /// </summary>
    /// <param name="folderPath">会議フォルダのパス</param>
    /// <param name="settings">要約設定</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>生成・更新した要約テキスト(スキップ時は既存の要約またはnull)</returns>
    public async Task<string?> EnsureSummaryAsync(
        string folderPath,
        SummarySettings settings,
        CancellationToken ct = default)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return null;

        var fullPath = Path.GetFullPath(folderPath);
        var gate = _locks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var summaryPath = Path.Combine(fullPath, MeetingStore.SummaryFileName);
            var statePath = Path.Combine(fullPath, MeetingStore.StateFileName);
            var existing = MeetingStore.ReadSummary(fullPath);

            // summary.md が既にあり、meeting.json より新しければ再生成不要
            if (existing is not null && File.Exists(summaryPath))
            {
                if (!File.Exists(statePath) || File.GetLastWriteTimeUtc(statePath) <= File.GetLastWriteTimeUtc(summaryPath))
                    return existing;
            }

            var state = MeetingStore.LoadState(fullPath);
            if (state.Entries.Count < MinMessageCount) return existing;
            if (MeetingStore.IsScheduledMeeting(fullPath, state)) return existing;

            var title = MarkdownLogWriter.ReadTitle(Path.Combine(fullPath, MeetingStore.LogFileName));
            var generated = await _summaryService.SummarizeAsync(state.Entries, settings, title, ct);
            MeetingStore.SaveSummary(fullPath, generated);
            return generated;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 過去会議をすべて確認し、summary.md が未作成の対象会議をバックグラウンドで順次生成する。
    /// </summary>
    /// <param name="meetingsDir">meetings フォルダ</param>
    /// <param name="settings">要約設定</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>新規に生成した件数</returns>
    public async Task<int> BackfillAllAsync(
        string meetingsDir,
        SummarySettings settings,
        CancellationToken ct = default)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(meetingsDir) || !Directory.Exists(meetingsDir))
            return 0;

        var meetings = MeetingStore.ListMeetings(meetingsDir);
        var count = 0;
        foreach (var m in meetings)
        {
            if (ct.IsCancellationRequested) break;
            if (m.HasSummary || m.IsScheduled || m.MessageCount < MinMessageCount) continue;

            try
            {
                var created = await EnsureSummaryAsync(m.FolderPath, settings, ct);
                if (!string.IsNullOrWhiteSpace(created)) count++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // 1件の失敗で全体のバックフィルを止めない
            }
        }
        return count;
    }
}
