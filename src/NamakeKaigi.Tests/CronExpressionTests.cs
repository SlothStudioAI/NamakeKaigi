using NamakeKaigi.Services;
using Xunit;

namespace NamakeKaigi.Tests;

/// <summary>cron式の読み取りと、次の実行時刻の計算の試験(夜の自動制作で使う式を含む)。</summary>
public class CronExpressionTests
{
    [Theory]
    [InlineData("0 0,2 * * *", "2026-11-03 23:30", "2026-11-04 00:00")]
    [InlineData("0 0,2 * * *", "2026-11-04 00:00", "2026-11-04 02:00")] // ちょうどの時刻は「より後」なので次へ
    [InlineData("0 1,3 * * *", "2026-11-04 03:00", "2026-11-05 01:00")]
    [InlineData("0 5,7 * * *", "2026-11-04 05:30", "2026-11-04 07:00")]
    [InlineData("0 4 * * *", "2026-11-04 04:00", "2026-11-05 04:00")]
    [InlineData("CRON_TZ=Asia/Tokyo 0 6 * * *", "2026-11-04 00:10", "2026-11-04 06:00")] // 先頭のタイムゾーンは無視
    public void Next_夜の予約の式で次の時刻を返す(string cron, string after, string expected)
    {
        var next = CronExpression.Parse(cron).Next(DateTime.Parse(after));
        Assert.Equal(DateTime.Parse(expected), next);
    }

    [Theory]
    [InlineData("*/15 * * * *", "2026-11-03 10:07", "2026-11-03 10:15")]
    [InlineData("*/15 * * * *", "2026-11-03 10:45", "2026-11-03 11:00")]
    [InlineData("0 9-17/4 * * *", "2026-11-03 10:00", "2026-11-03 13:00")] // 9,13,17時
    [InlineData("30 8 * * 1-5", "2026-11-06 09:00", "2026-11-09 08:30")] // 金曜の朝のあと → 月曜
    [InlineData("0 0 1 * *", "2026-11-03 00:00", "2026-12-01 00:00")]
    [InlineData("0 12 * * 7", "2026-11-03 00:00", "2026-11-08 12:00")] // 7も日曜(2026-11-08は日曜)
    public void Next_刻みと範囲と曜日(string cron, string after, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), CronExpression.Parse(cron).Next(DateTime.Parse(after)));
    }

    [Fact]
    public void Next_日と曜日を両方指定したら_どちらかに当たれば実行()
    {
        // 毎月1日、または月曜日
        var cron = CronExpression.Parse("0 0 1 * 1");
        Assert.Equal(DateTime.Parse("2026-11-09 00:00"), cron.Next(DateTime.Parse("2026-11-02 00:00"))); // 次の月曜
        Assert.Equal(DateTime.Parse("2026-12-01 00:00"), cron.Next(DateTime.Parse("2026-11-30 00:00"))); // 12/1は火曜だが「1日」に当たる
    }

    [Fact]
    public void Matches_分まで一致で_秒は見ない()
    {
        var cron = CronExpression.Parse("0 0,2 * * *");
        Assert.True(cron.Matches(DateTime.Parse("2026-11-04 02:00:45")));
        Assert.False(cron.Matches(DateTime.Parse("2026-11-04 02:01:00")));
        Assert.False(cron.Matches(DateTime.Parse("2026-11-04 01:00:00")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 0 * *")] // 4項目
    [InlineData("0 0 * * * *")] // 6項目
    [InlineData("60 0 * * *")] // 分が範囲外
    [InlineData("0 24 * * *")] // 時が範囲外
    [InlineData("0 0 0 * *")] // 日は1から
    [InlineData("0 0 * 13 *")] // 月が範囲外
    [InlineData("0 0 * * 8")] // 曜日が範囲外
    [InlineData("a 0 * * *")]
    [InlineData("5-1 0 * * *")] // 範囲が逆
    [InlineData("*/0 * * * *")] // 刻みが0
    [InlineData("0,,2 * * * *")] // 空の要素
    public void TryParse_正しくない式は失敗して理由を返す(string cron)
    {
        Assert.False(CronExpression.TryParse(cron, out var expr, out var error));
        Assert.Null(expr);
        Assert.NotEmpty(error);
        Assert.Throws<FormatException>(() => CronExpression.Parse(cron));
    }

    [Fact]
    public void Next_当たる日がない式は_上限でnull()
    {
        // 2月30日は存在しない
        Assert.Null(CronExpression.Parse("0 0 30 2 *").Next(DateTime.Parse("2026-01-01 00:00")));
    }
}
