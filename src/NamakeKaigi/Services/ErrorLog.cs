using System.IO;
using System.Text;

namespace NamakeKaigi.Services;

/// <summary>
/// 想定外の例外を、%APPDATA%\NamakeKaigi\error.log に足す。アプリ全体の受け口(App)から使う。
/// 記録できなくても、アプリは止めない。
/// </summary>
public static class ErrorLog
{
    /// <summary>記録のファイル名(%APPDATA%\NamakeKaigi の中)。</summary>
    public const string FileName = "error.log";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly object Gate = new();

    /// <summary>標準の記録先(%APPDATA%\NamakeKaigi\error.log)を返す。</summary>
    /// <returns>ファイルのパス</returns>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SettingsLoader.AppDataFolderName, FileName);

    /// <summary>例外を1件、時刻と種類つきで足す。フォルダがなければ作る。書けなくても、何もしない。</summary>
    /// <param name="path">記録先のファイル</param>
    /// <param name="source">どこで受け止めたか(画面の操作 / 裏の処理 など)</param>
    /// <param name="ex">例外</param>
    /// <param name="now">記録する時刻</param>
    public static void Append(string path, string source, Exception ex, DateTime now)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var text = $"[{now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}{Environment.NewLine}{Environment.NewLine}";
            lock (Gate) File.AppendAllText(path, text, Utf8);
        }
        catch (Exception)
        {
            // 記録できなくても、アプリは止めない
        }
    }
}
