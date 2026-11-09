using System.IO;
using System.Text.Json.Nodes;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>設定を2枚(既定の settings.json と、ユーザーの settings.local.json)に分けて読み書きする試験。</summary>
public class SettingsLocalTests
{
    private const string Defaults = """
        {
          "claude": { "executable": "claude", "model": "sonnet", "extraArgs": ["--tools", "Read"] },
          "agy": { "executable": "agy", "extraArgs": ["--mode", "plan"], "noToolNote": "既定の添え書き" },
          "workDir": "",
          "meetingsDir": "meetings",
          "maxInjectChars": 4000,
          "timeoutSec": 600
        }
        """;

    /// <summary>試験ごとの一時フォルダに、既定の設定ファイルを置く。</summary>
    private static (string DefaultsPath, string LocalPath) TempFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var defaults = Path.Combine(dir, "settings.json");
        File.WriteAllText(defaults, Defaults);
        return (defaults, Path.Combine(dir, "local", "settings.local.json")); // ユーザーの設定は、まだない
    }

    [Fact]
    public void ユーザーの設定がなければ_既定のまま読む()
    {
        var (defaults, local) = TempFiles();
        var settings = SettingsLoader.Load(defaults, local);
        Assert.Equal("sonnet", settings.Claude.Model);
        Assert.Equal("meetings", settings.MeetingsDir);
        Assert.False(File.Exists(local)); // 読むだけでは作らない
    }

    [Fact]
    public void ユーザーの設定は_項目ごとに既定の上に重なる()
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, """{ "claude": { "model": "opus" }, "workDir": "C:/work" }""");

        var settings = SettingsLoader.Load(defaults, local);
        Assert.Equal("opus", settings.Claude.Model);              // 重ねた値
        Assert.Equal("claude", settings.Claude.Executable);       // 同じ項目の、重ねていない値は既定のまま
        Assert.Equal(new[] { "--tools", "Read" }, settings.Claude.ExtraArgs);
        Assert.Equal("C:/work", settings.WorkDir);
        Assert.Equal(600, settings.TimeoutSec);
    }

    [Fact]
    public void 配列は_中身を足さずに置き換える()
    {
        var target = JsonNode.Parse("""{ "a": [1, 2, 3], "b": { "c": 1, "d": 2 } }""")!.AsObject();
        SettingsLoader.Merge(target, JsonNode.Parse("""{ "a": [9], "b": { "d": 5 } }""")!.AsObject());
        Assert.Equal("""{"a":[9],"b":{"c":1,"d":5}}""", target.ToJsonString());
    }

    [Fact]
    public void アプリで変えた設定は_ユーザーの設定にだけ書き_既定の設定は変えない()
    {
        var (defaults, local) = TempFiles();
        SettingsLoader.SaveModelAndEffort(local, "claude", "opus", "high");
        SettingsLoader.SaveWorkDir(local, @"C:\work\proj");

        Assert.Equal(Defaults, File.ReadAllText(defaults));
        var saved = JsonNode.Parse(File.ReadAllText(local))!.AsObject();
        Assert.Equal("""{"model":"opus","effort":"high"}""", saved["claude"]!.ToJsonString()); // 変えた所だけ
        Assert.Equal("C:/work/proj", (string?)saved["workDir"]);

        var settings = SettingsLoader.Load(defaults, local);
        Assert.Equal("opus", settings.Claude.Model);
        Assert.Equal("claude", settings.Claude.Executable);
    }

    [Fact]
    public void 設定を書いたあとに_一時ファイルが残らない()
    {
        var (_, local) = TempFiles();
        SettingsLoader.SaveWorkDir(local, @"C:\work\a");
        SettingsLoader.SaveWorkDir(local, @"C:\work\b"); // 2回目は、置き換え

        Assert.False(File.Exists(local + ".tmp"));
        Assert.Equal("C:/work/b", (string?)JsonNode.Parse(File.ReadAllText(local))!["workDir"]);
    }

    [Theory]
    [InlineData("{ 壊れている")]
    [InlineData("")]
    public void 壊れた_または空のユーザーの設定は_別の名前に退避して_既定の設定で読める(string content)
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, content);

        var backup = SettingsLoader.QuarantineBrokenLocal(local, new DateTime(2026, 11, 8, 20, 0, 0));

        Assert.NotNull(backup);
        Assert.EndsWith(".broken-20261108-200000", backup);
        Assert.False(File.Exists(local));
        Assert.Equal(content, File.ReadAllText(backup!)); // 消さずに残す
        Assert.Equal("sonnet", SettingsLoader.Load(defaults, local).Claude.Model);
    }

    [Fact]
    public void 壊れていない設定や_ファイルがないときは_何も退避しない()
    {
        var (_, local) = TempFiles();
        Assert.Null(SettingsLoader.QuarantineBrokenLocal(local, DateTime.Now)); // ファイルなし

        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, "{ // 手で書いた\n \"workDir\": \"C:/x\", }"); // コメント・末尾のカンマは許す
        Assert.Null(SettingsLoader.QuarantineBrokenLocal(local, DateTime.Now));
        Assert.True(File.Exists(local));
    }

    [Fact]
    public void 設定を読めないときの知らせに_ファイルの場所が入る()
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, "{ 壊れている");

        var ex = Assert.Throws<InvalidDataException>(() => SettingsLoader.Load(defaults, local));

        Assert.Contains(local, ex.Message);
    }

    [Fact]
    public void ユーザーの設定の標準の場所は_AppDataのNamakeKaigiの中()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NamakeKaigi", "settings.local.json");
        Assert.Equal(expected, SettingsLoader.DefaultLocalPath());
    }

    [Fact]
    public void パスは_環境変数を展開し_会議ログと予約の相対パスはexeのフォルダから数える()
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, """{ "agy": { "executable": "%USERPROFILE%/.gemini/bin/agy.exe" }, "scheduleFile": "schedule/schedule.json" }""");
        var baseDir = Path.Combine(Path.GetTempPath(), "app");

        var settings = SettingsLoader.ResolvePaths(SettingsLoader.Load(defaults, local), baseDir);
        Assert.Equal(Environment.GetEnvironmentVariable("USERPROFILE") + "/.gemini/bin/agy.exe", settings.Agy.Executable);
        Assert.Equal("claude", settings.Claude.Executable); // 名前だけなら、そのまま(PATHから探す)
        Assert.Equal(Path.Combine(baseDir, "meetings"), settings.MeetingsDir);
        Assert.Equal(Path.Combine(baseDir, "schedule", "schedule.json"), settings.ScheduleFile);
        Assert.Equal("", settings.WorkDir);
    }

    [Fact]
    public void 会議ログが空なら_exeの隣のmeetings_予約が空なら使わない()
    {
        var (defaults, local) = TempFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllText(local, """{ "meetingsDir": "", "scheduleFile": "" }""");
        var settings = SettingsLoader.ResolvePaths(SettingsLoader.Load(defaults, local), @"C:\app");
        Assert.Equal(@"C:\app\meetings", settings.MeetingsDir);
        Assert.Null(settings.ScheduleFile);
    }

    [Fact]
    public void 配布する既定の設定に_個人のパスが入っていない()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings.json"));
        Assert.DoesNotContain("C:/", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SlothStudio", text);
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }
}
