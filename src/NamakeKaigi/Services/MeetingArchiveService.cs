using System.IO;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>一定日数以上前の古い会議フォルダを圧縮せず meetings/_archive/yyyy-MM/ へ整理する。</summary>
public static class MeetingArchiveService
{
    /// <summary>
    /// 指定日数より古い会議を meetings/_archive/yyyy-MM/ へ移動する。
    /// 最近開き直して更新された会議は残す(開始日時と最終更新日時の両方が基準より古いものだけ移動)。
    /// </summary>
    /// <param name="meetingsDir">meetings フォルダのパス</param>
    /// <param name="archiveDays">アーカイブ対象の日数(0以下なら何もしない)</param>
    /// <param name="now">現在時刻</param>
    /// <returns>移動した会議フォルダの件数</returns>
    public static int ArchiveOldMeetings(string meetingsDir, int archiveDays, DateTime now)
    {
        if (archiveDays <= 0 || string.IsNullOrWhiteSpace(meetingsDir) || !Directory.Exists(meetingsDir))
            return 0;

        var cutoff = now.AddDays(-archiveDays);
        var meetings = MeetingStore.ListMeetings(meetingsDir);
        var moved = 0;

        foreach (var m in meetings)
        {
            if (m.StartedAt >= cutoff) continue;
            if (!Directory.Exists(m.FolderPath)) continue;

            // 最近開いて会話した会議は残す
            var lastWrite = LatestWriteTime(m.FolderPath, m.StartedAt);
            if (lastWrite >= cutoff) continue;

            var monthDir = Path.Combine(meetingsDir, MeetingStore.ArchiveDirName, m.StartedAt.ToString("yyyy-MM"));
            var rel = m.FolderName.Replace('/', Path.DirectorySeparatorChar);
            var dest = UniqueDestPath(Path.Combine(monthDir, rel));

            try
            {
                var destParent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destParent)) Directory.CreateDirectory(destParent);

                var srcParent = Path.GetDirectoryName(m.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                Directory.Move(m.FolderPath, dest);
                moved++;

                // 空になった起動時刻フォルダ(yyyyMMdd_HHmmss)があれば片付ける
                if (!string.IsNullOrEmpty(srcParent)
                    && !string.Equals(Path.GetFullPath(srcParent), Path.GetFullPath(meetingsDir), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(srcParent)
                    && Directory.GetFileSystemEntries(srcParent).Length == 0)
                {
                    Directory.Delete(srcParent);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ロック中や権限エラー時はスキップして起動を止めない
            }
        }

        return moved;
    }

    private static DateTime LatestWriteTime(string folderPath, DateTime fallback)
    {
        try
        {
            var latest = Directory.GetLastWriteTime(folderPath);
            foreach (var name in new[] { MeetingStore.StateFileName, MeetingStore.LogFileName, MeetingStore.SummaryFileName })
            {
                var p = Path.Combine(folderPath, name);
                if (File.Exists(p))
                {
                    var t = File.GetLastWriteTime(p);
                    if (t > latest) latest = t;
                }
            }
            return latest;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    private static string UniqueDestPath(string path)
    {
        if (!Directory.Exists(path)) return path;
        var n = 2;
        while (Directory.Exists($"{path} ({n})")) n++;
        return $"{path} ({n})";
    }
}
