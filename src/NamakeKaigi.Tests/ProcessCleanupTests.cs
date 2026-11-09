using System.Diagnostics;
using NamakeKaigi.Services;

namespace NamakeKaigi.Tests;

/// <summary>発言ごとに起動するCLIが、失敗で抜けたときに残らないことの試験(実際にプロセスを起動する)。</summary>
public class ProcessCleanupTests
{
    // 自分のプロセス番号を1行出してから、60秒眠るだけのPowerShell
    private static readonly string[] SleepScript =
        { "-NoProfile", "-Command", "[Console]::Out.WriteLine($PID); [Console]::Out.Flush(); Start-Sleep -Seconds 60" };

    /// <summary>指定のプロセスが、一定の時間内に終わるかを待つ。</summary>
    private static bool WaitUntilGone(int pid, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true; // もう存在しない
            }
            Thread.Sleep(100);
        }
        return false;
    }

    [Fact]
    public async Task 行を受ける処理が例外を投げても_起動したCLIは残らない()
    {
        var pid = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new ProcessRunner().RunLinesAsync(
            "powershell.exe", SleepScript, Environment.CurrentDirectory, TimeSpan.FromSeconds(30),
            line =>
            {
                pid = int.Parse(line.Trim());
                throw new InvalidOperationException("解析に失敗");
            }, CancellationToken.None));

        Assert.Equal("解析に失敗", ex.Message);
        Assert.NotEqual(0, pid);
        Assert.True(WaitUntilGone(pid, TimeSpan.FromSeconds(10)), "CLIが残っている");
    }

    [Fact]
    public async Task やりとりする起動でも_行を受ける処理が例外を投げたら_CLIは残らない()
    {
        var pid = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProcessRunner().RunConversationAsync(
            "powershell.exe", SleepScript, Environment.CurrentDirectory, TimeSpan.FromSeconds(30), "",
            (line, _) =>
            {
                pid = int.Parse(line.Trim());
                throw new InvalidOperationException("解析に失敗");
            }, CancellationToken.None));

        Assert.NotEqual(0, pid);
        Assert.True(WaitUntilGone(pid, TimeSpan.FromSeconds(10)), "CLIが残っている");
    }
}
