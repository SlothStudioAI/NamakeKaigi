using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>入っていないAIのカードを出さない機能(起動時にCLIを探す)の試験。</summary>
public class CliLocatorTests
{
    /// <summary>名前をPATHから探す偽物(名前→見つかる場所。表にない名前は、そのまま返す=見つからない)。</summary>
    private static Func<string, string> Resolver(params (string Name, string Path)[] onPath) =>
        name => onPath.FirstOrDefault(p => p.Name == name).Path ?? name;

    private static AppSettings Settings(string claude = "claude", string agy = @"C:\Users\x\.gemini\bin\agy.exe", string codex = "codex") =>
        new(new ClaudeSettings(claude, null, Array.Empty<string>()), new AgySettings(agy, Array.Empty<string>(), ""),
            "", "meetings", 4000, 600, Codex: new CodexSettings(codex));

    [Fact]
    public void 設定の場所にあれば_そこを使う()
    {
        var path = CliLocator.Find(@"C:\tools\agy.exe", "agy", exists: p => p == @"C:\tools\agy.exe", resolve: n => n);
        Assert.Equal(@"C:\tools\agy.exe", path);
    }

    [Fact]
    public void 設定の場所になければ_PATHから名前で探し直す()
    {
        var path = CliLocator.Find(@"C:\Users\x\.gemini\bin\agy.exe", "agy",
            exists: p => p == @"C:\bin\agy.exe", resolve: Resolver(("agy", @"C:\bin\agy.exe")));
        Assert.Equal(@"C:\bin\agy.exe", path);
    }

    [Fact]
    public void どこにもなければ_null()
    {
        Assert.Null(CliLocator.Find("codex", "codex", exists: _ => false, resolve: n => n));
    }

    [Fact]
    public void 見つかったAIだけを返し_見つかった場所を設定に入れる()
    {
        var (settings, installed) = CliLocator.Detect(Settings(),
            exists: p => p is @"C:\npm\claude.exe" or @"C:\npm\codex.cmd",
            resolve: Resolver(("claude", @"C:\npm\claude.exe"), ("codex", @"C:\npm\codex.cmd")));

        Assert.Equal(new[] { Participants.ClaudeId, Participants.ChappyId }, installed);
        Assert.Equal(@"C:\npm\claude.exe", settings.Claude.Executable);
        Assert.Equal(@"C:\Users\x\.gemini\bin\agy.exe", settings.Agy.Executable); // 見つからないものは、設定のまま
        Assert.Equal(@"C:\npm\codex.cmd", settings.Codex!.Executable);
    }

    [Fact]
    public void 入っていないAIは_カードを出さず_参加もしない()
    {
        var vm = new MainViewModel(() => new FakeSession(),
            installedAis: new[] { Participants.ClaudeId, Participants.ChappyId });

        Assert.False(vm.GeminiMember.IsInstalled);
        Assert.False(vm.GeminiPresent);
        Assert.True(vm.ClaudePresent && vm.ChappyPresent);
        Assert.True(vm.Members.First(m => m.Participant.Id == Participants.UserId).IsInstalled);
    }

    [Fact]
    public void 入っているAIが1人なら_1on1で始まる()
    {
        var vm = new MainViewModel(() => new FakeSession(), installedAis: new[] { Participants.GeminiId });

        Assert.True(vm.GeminiPresent);
        Assert.False(vm.ClaudePresent || vm.ChappyPresent);
        Assert.False(vm.ShowEveryoneTarget);
        Assert.Equal(ChatTarget.Gemini, vm.Target);
    }

    [Fact]
    public void 設定で選んだ人が入っていなければ_入っている人で始める()
    {
        var vm = new MainViewModel(() => new FakeSession(),
            defaultParticipants: new[] { Participants.GeminiId }, installedAis: new[] { Participants.ClaudeId });

        Assert.True(vm.ClaudePresent);
        Assert.False(vm.GeminiPresent);
    }
}
