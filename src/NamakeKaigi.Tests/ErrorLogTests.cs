using System.IO;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>想定外の例外の記録(ErrorLog)の試験。</summary>
public class ErrorLogTests
{
    [Fact]
    public void 例外を時刻と種類つきで足し_フォルダがなければ作る()
    {
        var dir = Path.Combine(Path.GetTempPath(), "namake_errlog_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sub", ErrorLog.FileName);
        try
        {
            ErrorLog.Append(path, "画面の操作", new InvalidOperationException("壊れた"), new DateTime(2026, 11, 8, 19, 0, 0));
            ErrorLog.Append(path, "裏の処理(Task)", new IOException("書けない"), new DateTime(2026, 11, 8, 19, 0, 1));

            var text = File.ReadAllText(path);
            Assert.Contains("[2026-11-08 19:00:00] (画面の操作)", text);
            Assert.Contains("InvalidOperationException: 壊れた", text);
            Assert.Contains("[2026-11-08 19:00:01] (裏の処理(Task))", text);
            Assert.Contains("IOException: 書けない", text);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 記録先に書けなくても_例外を投げない()
    {
        // ファイルを置くべき場所が、すでにファイルになっている(フォルダを作れない)
        var file = Path.Combine(Path.GetTempPath(), "namake_errlog_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try
        {
            var ex = Record.Exception(() => ErrorLog.Append(Path.Combine(file, "error.log"), "試験", new Exception("x"), DateTime.Now));
            Assert.Null(ex);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
