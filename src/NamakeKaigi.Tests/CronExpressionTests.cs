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

    /// <summary>31日指定で小の月をスキップし、7月と8月は連続で遷移することを確認する。</summary>
    [Fact]
    public void Next_31日指定で小の月をスキップし7月8月は連続で遷移する()
    {
        var cron = CronExpression.Parse("0 0 31 * *");
        var expectedDates = new[]
        {
            "2026-01-31 00:00",
            "2026-03-31 00:00",
            "2026-05-31 00:00",
            "2026-07-31 00:00",
            "2026-08-31 00:00",
            "2026-10-31 00:00",
            "2026-12-31 00:00",
        };

        var current = DateTime.Parse("2026-01-30 00:00");
        foreach (var expected in expectedDates)
        {
            var next = cron.Next(current);
            Assert.Equal(DateTime.Parse(expected), next);
            current = next!.Value;
        }
    }

    /// <summary>30日指定で平年と閏年の2月をスキップして3月30日へ遷移することを確認する。</summary>
    [Theory]
    [InlineData("2026-01-30 00:00", "2026-03-30 00:00")] // 平年
    [InlineData("2024-01-30 00:00", "2024-03-30 00:00")] // 閏年
    public void Next_30日指定で平年と閏年の2月をスキップする(string after, string expected)
    {
        var cron = CronExpression.Parse("0 0 30 * *");
        Assert.Equal(DateTime.Parse(expected), cron.Next(DateTime.Parse(after)));
    }

    /// <summary>存在しない日付(4/31, 6/31, 9/31, 11/31)でNextがnullを返すことを確認する。</summary>
    [Theory]
    [InlineData("0 0 31 4 *")]
    [InlineData("0 0 31 6 *")]
    [InlineData("0 0 31 9 *")]
    [InlineData("0 0 31 11 *")]
    public void Next_存在しない日付の式はnullを返す(string cronText)
    {
        Assert.Null(CronExpression.Parse(cronText).Next(DateTime.Parse("2026-01-01 00:00")));
    }

    /// <summary>短いlimitDaysを指定したとき探索上限を超えるとnullを返すことを確認する。</summary>
    [Fact]
    public void Next_短いlimitDaysの上限を超えるとnullを返す()
    {
        var monthly = CronExpression.Parse("0 0 15 * *");
        Assert.Null(monthly.Next(DateTime.Parse("2026-11-01 00:00"), limitDays: 10));

        var specific = CronExpression.Parse("0 1 2 * *");
        Assert.Null(specific.Next(DateTime.Parse("2026-11-01 00:00"), limitDays: 1));
        Assert.Equal(DateTime.Parse("2026-11-02 01:00"), specific.Next(DateTime.Parse("2026-11-01 00:00"), limitDays: 2));
    }

    /// <summary>2月29日指定で平年から次の閏年へ、さらに次の4年後の閏年へ遷移することを確認する。</summary>
    [Fact]
    public void Next_2月29日指定で平年から次の閏年へ遷移する()
    {
        var cron = CronExpression.Parse("0 0 29 2 *");
        Assert.Equal(DateTime.Parse("2028-02-29 00:00"), cron.Next(DateTime.Parse("2026-01-01 00:00")));

        var first = cron.Next(DateTime.Parse("2024-02-28 23:59"));
        Assert.Equal(DateTime.Parse("2024-02-29 00:00"), first);
        Assert.Equal(DateTime.Parse("2028-02-29 00:00"), cron.Next(first!.Value));
    }

    /// <summary>平年と閏年の2月28日で毎日0時指定の次の日が分岐することを確認する。</summary>
    [Theory]
    [InlineData("2026-02-28 23:00", "2026-03-01 00:00")] // 平年
    [InlineData("2028-02-28 23:00", "2028-02-29 00:00")] // 閏年
    public void Next_2月28日の毎日0時は平年なら3月1日で閏年なら2月29日になる(string after, string expected)
    {
        var cron = CronExpression.Parse("0 0 * * *");
        Assert.Equal(DateTime.Parse(expected), cron.Next(DateTime.Parse(after)));
    }

    /// <summary>100年に1度の平年(2100年)を跨ぐ2月29日指定が既定の上限でnullになることを確認する。</summary>
    [Fact]
    public void Next_2100年の平年を跨ぐ2月29日指定は既定の上限でnullになる()
    {
        var cron = CronExpression.Parse("0 0 29 2 *");
        Assert.Null(cron.Next(DateTime.Parse("2096-02-29 00:00")));
    }

    /// <summary>23時59分59秒の秒切り捨てと1分加算による日跨ぎ・月跨ぎ・年越しを確認する。</summary>
    [Theory]
    [InlineData("2026-11-13 23:59:59", "2026-11-14 00:00:00")] // 日跨ぎ
    [InlineData("2026-11-30 23:59:59", "2026-12-01 00:00:00")] // 月跨ぎ
    [InlineData("2026-12-31 23:59:59", "2027-01-01 00:00:00")] // 年越し
    public void Next_23時59分59秒から日跨ぎ月跨ぎ年越しが正しく遷移する(string after, string expected)
    {
        var cron = CronExpression.Parse("* * * * *");
        Assert.Equal(DateTime.Parse(expected), cron.Next(DateTime.Parse(after)));
    }

    /// <summary>深夜帯リスト(30 23,0,1 * * *)が23:30から翌日0:30・1:30・23:30へ遷移することを確認する。</summary>
    [Fact]
    public void Next_深夜帯リストが23時30分から翌日0時30分と1時30分へ遷移する()
    {
        var cron = CronExpression.Parse("30 23,0,1 * * *");
        var t1 = cron.Next(DateTime.Parse("2026-11-13 23:00"));
        Assert.Equal(DateTime.Parse("2026-11-13 23:30"), t1);

        var t2 = cron.Next(t1!.Value);
        Assert.Equal(DateTime.Parse("2026-11-14 00:30"), t2);

        var t3 = cron.Next(t2!.Value);
        Assert.Equal(DateTime.Parse("2026-11-14 01:30"), t3);

        var t4 = cron.Next(t3!.Value);
        Assert.Equal(DateTime.Parse("2026-11-14 23:30"), t4);
    }

    /// <summary>入力したDateTimeKindがNextの戻り値でも維持されることを確認する。</summary>
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Next_afterのDateTimeKindが戻り値でも維持される(DateTimeKind kind)
    {
        var after = new DateTime(2026, 11, 13, 23, 30, 15, kind);
        var next1 = CronExpression.Parse("0 4 * * *").Next(after);
        Assert.NotNull(next1);
        Assert.Equal(kind, next1!.Value.Kind);

        var next2 = CronExpression.Parse("0 0 1 12 *").Next(after);
        Assert.NotNull(next2);
        Assert.Equal(kind, next2!.Value.Kind);
    }

    /// <summary>単一数値と刻みの組み合わせ(5/10)が5から55まで10分刻みで一致することを確認する。</summary>
    [Fact]
    public void Matches_単一数値と刻みの式が正しい分にのみ一致する()
    {
        var cron = CronExpression.Parse("5/10 * * * *");
        var matched = Enumerable.Range(0, 60)
            .Where(m => cron.Matches(new DateTime(2026, 11, 13, 10, m, 0)))
            .ToArray();
        Assert.Equal(new[] { 5, 15, 25, 35, 45, 55 }, matched);
    }

    /// <summary>範囲・刻み・カンマ複合式(0,10-20/3,59)が該当する分にのみ一致することを確認する。</summary>
    [Fact]
    public void Matches_範囲と刻みとカンマの複合式が正しい分にのみ一致する()
    {
        var cron = CronExpression.Parse("0,10-20/3,59 * * * *");
        var matched = Enumerable.Range(0, 60)
            .Where(m => cron.Matches(new DateTime(2026, 11, 13, 10, m, 0)))
            .ToArray();
        Assert.Equal(new[] { 0, 10, 13, 16, 19, 59 }, matched);
    }

    /// <summary>曜日0と7のどちらも日曜に一致し、他の曜日には一致しないことを確認する。</summary>
    [Theory]
    [InlineData("0 12 * * 0")]
    [InlineData("0 12 * * 7")]
    public void Matches_曜日0と7のどちらも日曜に一致し月曜には一致しない(string cronText)
    {
        var cron = CronExpression.Parse(cronText);
        // 2026-11-08は日曜、2026-11-09は月曜
        Assert.True(cron.Matches(new DateTime(2026, 11, 8, 12, 0, 0)));
        Assert.False(cron.Matches(new DateTime(2026, 11, 9, 12, 0, 0)));
    }

    /// <summary>複数空白・タブおよび大小文字のCRON_TZを除いてTextが正規化されることを確認する。</summary>
    [Theory]
    [InlineData("  0\t\t0,2  *   *\t*  ", "0 0,2 * * *")]
    [InlineData("CRON_TZ=Asia/Tokyo\t0  6 * * *", "0 6 * * *")]
    [InlineData("  cron_tz=UTC   */15\t*\t*\t*  * ", "*/15 * * * *")]
    public void TryParse_複数空白やタブとCRON_TZを除いてTextが正規化される(string input, string expectedText)
    {
        Assert.True(CronExpression.TryParse(input, out var expr, out var error));
        Assert.Equal("", error);
        Assert.NotNull(expr);
        Assert.Equal(expectedText, expr!.Text);
    }

    /// <summary>nullや空白のみ、CRON_TZのみ、不正な負数や区切り記号の式が失敗して理由を返すことを確認する。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   \t  ")]
    [InlineData("CRON_TZ=Asia/Tokyo")]
    [InlineData("-1 * * * *")]
    [InlineData("*/-1 * * * *")]
    [InlineData("1/2/3 * * * *")]
    [InlineData("1-2-3 * * * *")]
    [InlineData("1- * * * *")]
    [InlineData("-5 * * * *")]
    [InlineData(",1 * * * *")]
    [InlineData("1, * * * *")]
    [InlineData("0 0 * 0 *")]
    [InlineData("0 0 32 * *")]
    [InlineData("0 0 * * 8")]
    public void TryParse_異常系境界値は失敗して理由を返す(string? cron)
    {
        Assert.False(CronExpression.TryParse(cron, out var expr, out var error));
        Assert.Null(expr);
        Assert.NotEmpty(error);
    }
}
