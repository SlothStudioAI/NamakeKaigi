using System.IO;
using NamakeKaigi.Models;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>agy の自動更新を止める(意図せず黒い窓を出さない)ことと、意図して更新を確認する部品の試験。</summary>
public class AgyUpdateTests
{
    private const string Echo = "echo [%AGY_CLI_DISABLE_AUTO_UPDATE%]";

    [Fact]
    public async Task 発言ごとの起動では_自動更新を止める環境変数が付く()
    {
        var lines = new List<string>();
        await new ProcessRunner().RunLinesAsync("cmd.exe", new[] { "/c", Echo }, Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10), lines.Add, CancellationToken.None);
        Assert.Equal("[true]", lines.Single().Trim());
    }

    [Fact]
    public async Task 標準入力を渡す起動でも_自動更新を止める環境変数が付く()
    {
        var lines = new List<string>();
        await new ProcessRunner().RunWithInputAsync("cmd.exe", new[] { "/c", Echo }, Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10), "", lines.Add, CancellationToken.None);
        Assert.Equal("[true]", lines.Single().Trim());
    }

    [Fact]
    public async Task やりとりする起動でも_自動更新を止める環境変数が付く()
    {
        var lines = new List<string>();
        await new ProcessRunner().RunConversationAsync("cmd.exe", new[] { "/c", Echo }, Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10), "", (line, _) => { lines.Add(line); return Task.CompletedTask; },
            CancellationToken.None);
        Assert.Equal("[true]", lines.Single().Trim());
    }

    [Fact]
    public async Task 起動したままの起動でも_自動更新を止める環境変数が付く()
    {
        using var session = new ProcessRunner().StartSession("cmd.exe", new[] { "/c", Echo }, Environment.CurrentDirectory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal("[true]", (await session.ReadLineAsync(cts.Token))?.Trim());
    }

    [Fact]
    public void 更新係の記録を読む()
    {
        Assert.Equal((true, "Already on the latest version."),
            AgyUpdater.ReadStatus("""{"success":true,"message":"Already on the latest version."}"""));
        Assert.Equal((false, "network error"), AgyUpdater.ReadStatus("""{"success":false,"message":"network error"}"""));
        Assert.Equal((false, ""), AgyUpdater.ReadStatus("{}"));
        Assert.False(AgyUpdater.ReadStatus("壊れた記録").Success);
    }

    [Fact]
    public void 結果を画面の文にする()
    {
        Assert.Equal("最新の版です: Already on the latest version.",
            AgyUpdater.Describe(new AgyUpdateResult(true, "Already on the latest version.", false)));
        Assert.Equal("更新がありました: Updated.", AgyUpdater.Describe(new AgyUpdateResult(true, "Updated.", true)));
        Assert.StartsWith("確認できませんでした", AgyUpdater.Describe(new AgyUpdateResult(false, "network error", false)));
    }

    [Fact]
    public void 記録ファイルの場所は_省略なら標準で_環境変数を展開する()
    {
        var settings = new AgySettings("agy", Array.Empty<string>(), "");
        var expected = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!,
            ".gemini", "antigravity-cli", "updater", "update_status.json"));
        Assert.Equal(expected, AgyUpdater.StatusFilePath(settings));
        Assert.Equal(Path.GetFullPath("C:/x/status.json"),
            AgyUpdater.StatusFilePath(settings with { UpdateStatusFile = "C:/x/status.json" }));
    }

    /// <summary>試験用の設定(記録ファイルは、試験ごとの一時フォルダに置く)。</summary>
    private static AgySettings TempSettings(out string statusFile, int timeoutSec = 30)
    {
        var dir = Path.Combine(Path.GetTempPath(), "agyupd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        statusFile = Path.Combine(dir, "update_status.json");
        return new AgySettings(Path.Combine(dir, "agy.exe"), Array.Empty<string>(), "",
            UpdateStatusFile: statusFile, UpdateCheckTimeoutSec: timeoutSec);
    }

    [Fact]
    public async Task 確認_記録が最新なら_更新なし()
    {
        var settings = TempSettings(out var status);
        var result = await new AgyUpdater(settings, (_, _) =>
        {
            File.WriteAllText(status, """{"success":true,"message":"Already on the latest version."}""");
            return Task.CompletedTask;
        }).CheckAsync();
        Assert.Equal(new AgyUpdateResult(true, "Already on the latest version.", false), result);
    }

    [Fact]
    public async Task 確認_exeが変わらなくても_記録の文が最新でなければ_更新ありにする()
    {
        var settings = TempSettings(out var status);
        var result = await new AgyUpdater(settings, (_, _) =>
        {
            File.WriteAllText(status, """{"success":true,"message":"Updated to 1.6.0. Restart to apply."}""");
            return Task.CompletedTask;
        }).CheckAsync();
        Assert.True(result.Updated);
    }

    [Fact]
    public async Task 確認_記録が書き換わらなければ_失敗にする()
    {
        var settings = TempSettings(out _);
        var result = await new AgyUpdater(settings, (_, _) => Task.CompletedTask).CheckAsync();
        Assert.False(result.Success);
        Assert.Contains("書き換わりませんでした", result.Message);
    }

    [Fact]
    public async Task 確認_終わらなければ_時間切れにする()
    {
        var settings = TempSettings(out _, timeoutSec: 1);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            new AgyUpdater(settings, (_, ct) => Task.Delay(Timeout.Infinite, ct)).CheckAsync());
    }

    [Fact]
    public async Task 確認_画面を閉じたら_時間切れではなくキャンセルにする()
    {
        var settings = TempSettings(out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AgyUpdater(settings, (_, ct) => Task.Delay(Timeout.Infinite, ct)).CheckAsync(cts.Token));
    }

    [Fact]
    public async Task 確認_待ち時間の設定が0なら_標準の上限を使う()
    {
        var settings = TempSettings(out var status, timeoutSec: 0);
        var result = await new AgyUpdater(settings, async (_, _) =>
        {
            await Task.Delay(100); // 0秒のまま使うと、ここで時間切れになる
            File.WriteAllText(status, """{"success":true,"message":"Already on the latest version."}""");
        }).CheckAsync();
        Assert.True(result.Success);
    }

    [Fact]
    public async Task 確認_自動更新を止める環境変数は_外して起動する()
    {
        var settings = TempSettings(out var status);
        Environment.SetEnvironmentVariable(ProcessRunner.AgyDisableAutoUpdateVariable, "true");
        try
        {
            System.Diagnostics.ProcessStartInfo? started = null;
            await new AgyUpdater(settings, (info, _) =>
            {
                started = info;
                File.WriteAllText(status, "{}");
                return Task.CompletedTask;
            }).CheckAsync();
            Assert.False(started!.Environment.ContainsKey(ProcessRunner.AgyDisableAutoUpdateVariable));
            Assert.Equal(AgyUpdater.DefaultUpdateCheckArgs, started.ArgumentList);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessRunner.AgyDisableAutoUpdateVariable, null);
        }
    }

    [Fact]
    public async Task 確認は_同時に1つだけ()
    {
        var settings = TempSettings(out var status);
        var release = new TaskCompletionSource();
        var first = new AgyUpdater(settings, async (_, _) =>
        {
            await release.Task;
            File.WriteAllText(status, "{}");
        }).CheckAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AgyUpdater(settings, (_, _) => Task.CompletedTask).CheckAsync());

        release.SetResult();
        await first;
        // 1つ目が終われば、また確認できる
        var again = await new AgyUpdater(settings, (_, _) =>
        {
            File.WriteAllText(status, "{}");
            return Task.CompletedTask;
        }).CheckAsync();
        Assert.False(again.Success);
    }
}
