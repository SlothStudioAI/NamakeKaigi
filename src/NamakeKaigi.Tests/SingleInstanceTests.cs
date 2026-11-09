using NamakeKaigi.Services;
using Xunit;

namespace NamakeKaigi.Tests;

/// <summary>二重起動の防止の試験。</summary>
public class SingleInstanceTests
{
    private static string NewName() => "NamakeKaigiTest_" + Guid.NewGuid().ToString("N");

    [Fact]
    public void 同じ名前の後の起動は_最初の起動ではない()
    {
        var name = NewName();
        using var first = SingleInstance.TryAcquire(name);
        using var second = SingleInstance.TryAcquire(name);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void 先の起動が名前を手放せば_次は最初の起動になれる()
    {
        var name = NewName();
        var first = SingleInstance.TryAcquire(name);
        Assert.True(first.IsFirst);
        first.Dispose();

        using var next = SingleInstance.TryAcquire(name);
        Assert.True(next.IsFirst);
    }

    [Fact]
    public void 名前が違えば_どちらも最初の起動()
    {
        using var a = SingleInstance.TryAcquire(NewName());
        using var b = SingleInstance.TryAcquire(NewName());

        Assert.True(a.IsFirst && b.IsFirst);
    }

    [Theory]
    [InlineData("--allow-multiple", true)]
    [InlineData("--ALLOW-MULTIPLE", true)]
    [InlineData("--other", false)]
    public void 起動引数で_二重起動を許せる(string arg, bool expected)
    {
        Assert.Equal(expected, SingleInstance.AllowsMultiple(new[] { arg }));
    }

    [Fact]
    public void 引数がなければ_二重起動は許さない()
    {
        Assert.False(SingleInstance.AllowsMultiple(Array.Empty<string>()));
    }
}
