using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;
using Xunit;

namespace NamakeKaigi.Tests;

/// <summary>バージョン表示の試験。</summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("0.9.0", "0.9.0")]
    [InlineData("0.9.0+fb662f181519b7fb0bd0b5eccfa3bb0e9cc26f79", "0.9.0")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1")]
    [InlineData("  0.9.0  ", "0.9.0")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 版の文字から_コミットIDなどを除いて数字だけにする(string? raw, string expected)
    {
        Assert.Equal(expected, AppVersion.Normalize(raw));
    }

    [Fact]
    public void 表示用は_vを付ける_空なら空()
    {
        Assert.Equal("v0.9.0", AppVersion.ToLabel("0.9.0"));
        Assert.Equal("", AppVersion.ToLabel(""));
    }

    [Fact]
    public void 実行中のアプリの版は_csprojの0_9_0で_コミットIDを含まない()
    {
        Assert.Equal("0.9.0", AppVersion.Current);
        Assert.DoesNotContain("+", AppVersion.Current);
    }

    [Fact]
    public void タイトルバーの版は_看板とは別の項目で_簡易表示でも出る()
    {
        var vm = new MainViewModel(() => new FakeSession());
        Assert.Equal("v0.9.0", vm.WindowHeaderVersion);
        Assert.Equal("NamakeKaigi — Meeting-Driven Development", vm.WindowHeaderTitle); // 看板の文言は変えない
    }
}
