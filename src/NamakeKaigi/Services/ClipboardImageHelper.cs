using System.IO;
using System.Windows.Media.Imaging;

namespace NamakeKaigi.Services;

/// <summary>クリップボードの画像を、会議フォルダ(log.md と同じフォルダ)の attachments フォルダへPNGとして保存する。</summary>
public static class ClipboardImageHelper
{
    /// <summary>BitmapSource を、ログと同じフォルダの attachments 内にPNGファイルとして保存し、フルパスを返す。</summary>
    /// <param name="image">クリップボードから取得した画像</param>
    /// <param name="logPath">いまの会議のログ(.md)のパス(親フォルダの特定に使う)</param>
    /// <param name="timestamp">ファイル名に使う時刻(省略時は現在時刻)</param>
    /// <returns>保存したPNGファイルのフルパス</returns>
    public static string SavePngToMeetingFolder(BitmapSource image, string logPath, DateTime? timestamp = null)
    {
        var meetingDir = Path.GetDirectoryName(Path.GetFullPath(logPath))
                         ?? Path.Combine(Path.GetTempPath(), "NamakeKaigi");
        var attachDir = Path.Combine(meetingDir, "attachments");
        Directory.CreateDirectory(attachDir);

        var now = timestamp ?? DateTime.Now;
        var fileName = $"clipboard_{now:yyyyMMdd_HHmmss_fff}.png";
        var fullPath = Path.Combine(attachDir, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(fullPath);
        encoder.Save(stream);

        return fullPath;
    }
}
