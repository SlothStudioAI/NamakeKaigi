using System.IO;
using System.Text.Json.Nodes;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>設定画面の「場所」で、パスを読み書きする試験。</summary>
public class SettingsPathsTests
{
    private const string Defaults = """
        {
          "claude": { "executable": "claude", "model": "sonnet" },
          "agy": { "executable": "%USERPROFILE%/.gemini/bin/agy.exe" },
          "codex": { "executable": "codex" },
          "workDir": "",
          "meetingsDir": "meetings",
          "maxInjectChars": 4000,
          "timeoutSec": 600
        }
        """;

    /// <summary>試験ごとの一時フォルダに、既定の設定ファイルを置く。</summary>
    private static (string DefaultsPath, string LocalPath) TempFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paths_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var defaults = Path.Combine(dir, "settings.json");
        File.WriteAllText(defaults, Defaults);
        return (defaults, Path.Combine(dir, "local", "settings.local.json"));
    }

    [Fact]
    public void 読むときは_既定の上にユーザーの設定を重ねた値で_環境変数は展開しない()
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, """{ "workDir": "C:/work", "claude": { "executable": "C:/bin/claude.exe" } }""");

        var paths = SettingsLoader.LoadPaths(defaults, local);
        Assert.Equal("C:/work", paths.WorkDir);
        Assert.Equal("meetings", paths.MeetingsDir);
        Assert.Equal("", paths.ScheduleFile);
        Assert.Equal("C:/bin/claude.exe", paths.ClaudeExecutable);
        Assert.Equal("%USERPROFILE%/.gemini/bin/agy.exe", paths.AgyExecutable);
        Assert.Equal("codex", paths.CodexExecutable);
    }

    [Fact]
    public void 保存すると_変えた値だけがユーザーの設定に入り_区切りは斜線になる()
    {
        var (defaults, local) = TempFiles();
        var paths = SettingsLoader.LoadPaths(defaults, local) with
        {
            WorkDir = @"D:\work",
            ScheduleFile = "D:/schedule.json",
            CodexExecutable = @"D:\tools\codex.cmd",
        };

        SettingsLoader.SavePaths(defaults, local, paths);

        var root = JsonNode.Parse(File.ReadAllText(local))!.AsObject();
        Assert.Equal("D:/work", (string?)root["workDir"]);
        Assert.Equal("D:/schedule.json", (string?)root["scheduleFile"]);
        Assert.Equal("D:/tools/codex.cmd", (string?)root["codex"]!["executable"]);
        Assert.False(root.ContainsKey("meetingsDir")); // 既定と同じ値は書かない
        Assert.False(root.ContainsKey("claude"));
        Assert.False(root.ContainsKey("agy"));
    }

    [Fact]
    public void 空にして保存すると_ユーザーの設定から消えて既定に戻り_ほかの設定は残る()
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, """
            { "workDir": "C:/work", "meetingsDir": "C:/logs", "theme": "mint",
              "agy": { "executable": "C:/agy.exe", "model": "pro" } }
            """);

        var paths = SettingsLoader.LoadPaths(defaults, local) with { MeetingsDir = "", AgyExecutable = "  " };
        SettingsLoader.SavePaths(defaults, local, paths);

        var root = JsonNode.Parse(File.ReadAllText(local))!.AsObject();
        Assert.False(root.ContainsKey("meetingsDir"));
        Assert.Equal("C:/work", (string?)root["workDir"]);
        Assert.Equal("mint", (string?)root["theme"]);                  // 場所以外の設定は、そのまま
        Assert.False(root["agy"]!.AsObject().ContainsKey("executable"));
        Assert.Equal("pro", (string?)root["agy"]!["model"]);           // 同じ項目のほかの値も、そのまま
        Assert.Equal("meetings", SettingsLoader.Load(defaults, local).MeetingsDir);
    }
}
