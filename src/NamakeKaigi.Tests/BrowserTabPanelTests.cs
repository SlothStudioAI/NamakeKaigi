using NamakeKaigi.Views;

namespace NamakeKaigi.Tests;

/// <summary>BrowserTabPanel のタブ幅配分ロジックの試験。</summary>
public class BrowserTabPanelTests
{
    [Fact]
    public void 収まる場合は_本来の希望幅のままになる()
    {
        var desired = new[] { 120.0, 180.0, 150.0 };
        var result = BrowserTabPanel.AllocateWidths(desired, 500.0);

        Assert.Equal(desired, result);
    }

    [Fact]
    public void はみ出す場合は_長いタブから縮んで_合計が上限以下に収まる()
    {
        var desired = new[] { 100.0, 300.0, 300.0 };
        var result = BrowserTabPanel.AllocateWidths(desired, 500.0);

        Assert.True(result.Sum() <= 500.0 + 0.01);
        Assert.Equal(100.0, result[0], precision: 1); // 短いタブはそのまま
        Assert.True(result[1] < 300.0 && result[2] < 300.0);
    }
}
